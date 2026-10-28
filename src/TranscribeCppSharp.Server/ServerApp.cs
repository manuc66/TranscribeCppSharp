namespace TranscribeCppSharp.Server;

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Primitives;
using TranscribeCppSharp.Audio;
using TranscribeCppSharp.SharedSource;

/// <summary>
/// The HTTP surface: OpenAI's transcription routes, plus health and the model
/// list.
/// </summary>
/// <remarks>
/// Kept apart from <see cref="ServerHost"/> so tests can build the same
/// application with a fake backend and drive it over a real loopback socket.
/// </remarks>
internal static class ServerApp
{
    private const string DefaultResponseFormat = "json";

    /// <summary>The two spellings OpenAI clients use for the granularity field.</summary>
    private static readonly string[] GranularityFields =
        ["timestamp_granularities[]", "timestamp_granularities"];

    /// <summary>Property naming for every JSON body: OpenAI uses snake_case.</summary>
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    /// <summary>Builds the application, routes and auth included.</summary>
    internal static WebApplication Build(ServerOptions options, ITranscriptionBackend backend)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls(options.Urls);
        builder.WebHost.ConfigureKestrel(kestrel =>
            kestrel.Limits.MaxRequestBodySize = options.MaxUploadBytes);

        // Kestrel's body limit is not the whole story: the multipart reader has
        // its own cap, 128 MiB by default, which would reject an upload the
        // --max-upload-mb setting says is allowed. Both are set from the same
        // number so the documented limit is the real one.
        builder.Services.Configure<FormOptions>(form =>
        {
            form.MultipartBodyLengthLimit = options.MaxUploadBytes;
            form.ValueLengthLimit = (int)Math.Min(options.MaxUploadBytes, int.MaxValue);
        });

        WebApplication app = builder.Build();

        // The text and subtitle responses carry the transcript verbatim, and it
        // is whatever the audio said. Declaring the content type only helps if
        // the browser is told not to second-guess it, so no response is sniffed.
        app.Use(async (context, next) =>
        {
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            await next(context);
        });

        // DNS rebinding: after evil.example resolves to 127.0.0.1, the browser
        // sends Origin and Host as the same name, so the Origin check below
        // cannot see it. The Host header is the only thing left, and it is opt-in
        // because a reverse proxy legitimately forwards its own name.
        string[] allowedHosts = options.AllowedHosts;
        if (allowedHosts.Length > 0)
        {
            app.Use(async (context, next) =>
            {
                string host = BareHost(context.Request.Host.Host);
                if (!allowedHosts.Contains(host, StringComparer.OrdinalIgnoreCase))
                {
                    await WriteErrorAsync(
                        context,
                        StatusCodes.Status403Forbidden,
                        $"host '{host}' is not one of --allowed-host; add it there if this is your server",
                        param: null,
                        code: "host_not_allowed");
                    return;
                }

                await next(context);
            });
        }

        // A multipart POST is a CORS "simple request": a page on any site can
        // make the browser send one to 127.0.0.1 with no preflight, and the
        // response being unreadable does not stop the server from transcribing
        // the uploaded blob. Origin, and Sec-Fetch-Site which current browsers
        // send, is what tells such a request apart from a direct client.
        app.Use(async (context, next) =>
        {
            if (IsCrossSite(context.Request))
            {
                await WriteErrorAsync(
                    context,
                    StatusCodes.Status403Forbidden,
                    "cross-site requests are refused: this endpoint is for direct clients",
                    param: null,
                    code: "cross_site_request");
                return;
            }

            await next(context);
        });

        if (!string.IsNullOrEmpty(options.ApiKey))
        {
            string expected = "Bearer " + options.ApiKey;
            app.Use(async (context, next) =>
            {
                // Health stays open so a supervisor can tell "not up yet" from
                // "up but refusing"; it exposes no audio and no transcript.
                if (context.Request.Path.Equals("/health", StringComparison.OrdinalIgnoreCase))
                {
                    await next(context);
                    return;
                }

                string provided = context.Request.Headers.Authorization.ToString();
                if (!FixedTimeEquals(provided, expected))
                {
                    await WriteErrorAsync(
                        context,
                        StatusCodes.Status401Unauthorized,
                        "a bearer token is required; send 'Authorization: Bearer <token>'",
                        param: null,
                        code: "invalid_api_key");
                    return;
                }

                await next(context);
            });
        }

        app.MapGet("/health", () => Results.Json(
            new HealthResponse("ok", backend.ModelId, backend.IsModelLoaded),
            Json));

        app.MapGet("/v1/models", () => Results.Json(
            new ModelList("list", [new ModelCard(backend.ModelId, "model", "transcribe.cpp")]),
            Json));

        // The native library allows one run at a time, so every accepted request
        // is a queued upload on disk. This bounds that queue, and it is taken
        // before the body is read: a saturated server must not stage a gigabyte
        // per client that ignores the 429.
        var admission = new SemaphoreSlim(options.MaxPendingRequests);

        app.MapPost("/v1/audio/transcriptions", (Func<HttpContext, Task<IResult>>)(context =>
            HandleAsync(context, options, backend, TranscriptionTask.Transcribe, admission)));

        app.MapPost("/v1/audio/translations", (Func<HttpContext, Task<IResult>>)(context =>
            HandleAsync(context, options, backend, TranscriptionTask.Translate, admission)));

        return app;
    }

    private static async Task<IResult> HandleAsync(
        HttpContext context,
        ServerOptions options,
        ITranscriptionBackend backend,
        TranscriptionTask task,
        SemaphoreSlim admission)
    {
        if (!context.Request.HasFormContentType)
        {
            return Error(
                StatusCodes.Status400BadRequest,
                "the request must be multipart/form-data with a 'file' part",
                param: "file");
        }

        if (!await admission.WaitAsync(TimeSpan.Zero, context.RequestAborted))
        {
            context.Response.Headers.RetryAfter = "5";
            return Error(
                StatusCodes.Status429TooManyRequests,
                $"the server is already handling {options.MaxPendingRequests} request(s); "
                + "retry in a few seconds",
                param: null,
                code: "too_many_requests");
        }

        try
        {
            return await TranscribeAsync(context, options, backend, task);
        }
        finally
        {
            admission.Release();
        }
    }

    /// <summary>Reads the upload, runs it, and shapes the answer.</summary>
    private static async Task<IResult> TranscribeAsync(
        HttpContext context,
        ServerOptions options,
        ITranscriptionBackend backend,
        TranscriptionTask task)
    {
        IFormCollection form;
        try
        {
            form = await context.Request.ReadFormAsync(context.RequestAborted);
        }
        catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            // Kestrel refuses the body while the form is being read, so this is
            // reached here rather than as a body-limit error of its own. 413 is
            // what the client asked for and what OpenAI-compatible clients expect;
            // reporting it as a 400 would blame the multipart syntax instead.
            return Error(
                StatusCodes.Status413PayloadTooLarge,
                $"the upload is larger than this server accepts "
                + $"({options.MaxUploadBytes / (1024 * 1024)} MiB, see --max-upload-mb)",
                param: "file",
                code: "payload_too_large");
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException)
        {
            return Error(
                StatusCodes.Status400BadRequest,
                $"the multipart body could not be read: {ex.Message}",
                param: null);
        }

        if (IsPresent(form, "prompt"))
        {
            return Error(
                StatusCodes.Status400BadRequest,
                "the 'prompt' field is not wired to transcribe.cpp by this server; remove it",
                param: "prompt",
                code: "unsupported_parameter");
        }

        if (IsPresent(form, "temperature") && !IsZero(form["temperature"].ToString()))
        {
            return Error(
                StatusCodes.Status400BadRequest,
                "the 'temperature' field is not wired to transcribe.cpp by this server; omit it or send 0",
                param: "temperature",
                code: "unsupported_parameter");
        }

        IFormFile? file = form.Files.GetFile("file");
        if (file is null || file.Length == 0)
        {
            return Error(
                StatusCodes.Status400BadRequest,
                "the 'file' part is required and must not be empty",
                param: "file");
        }

        string? requestedModel = form["model"].ToString();
        if (!string.IsNullOrWhiteSpace(requestedModel)
            && !string.Equals(requestedModel, backend.ModelId, StringComparison.Ordinal))
        {
            return Error(
                StatusCodes.Status404NotFound,
                $"this server serves '{backend.ModelId}', not '{requestedModel}'; "
                + "start another instance for another model",
                param: "model",
                code: "model_not_found");
        }

        string responseFormat = form["response_format"].ToString().Trim().ToLowerInvariant();
        if (responseFormat.Length == 0)
        {
            responseFormat = DefaultResponseFormat;
        }

        if (responseFormat is not ("json" or "text" or "verbose_json" or "srt" or "vtt"))
        {
            return Error(
                StatusCodes.Status400BadRequest,
                $"response_format '{responseFormat}' is not implemented by this server; "
                + "use json, text, verbose_json, srt or vtt",
                param: "response_format",
                code: "unsupported_response_format");
        }

        if (!TryReadDiarize(form, options.Diarize, out bool diarize))
        {
            return Error(
                StatusCodes.Status400BadRequest,
                "the 'diarize' field must be true or false",
                param: "diarize");
        }

        bool wantWords = WantsWords(form);

        string? language = form["language"].ToString().Trim();
        if (string.IsNullOrEmpty(language))
        {
            language = null;
        }

        string audioPath = Path.Combine(
            Path.GetTempPath(),
            $"transcribe-server-{Guid.NewGuid():N}{SafeExtension(file.FileName)}");

        TranscriptionOutput output;
        try
        {
            await using (FileStream stream = PrivateTempFile.Create(audioPath))
            {
                await file.CopyToAsync(stream, context.RequestAborted);
            }

            output = await backend.TranscribeAsync(new TranscriptionRequest(
                audioPath,
                language,
                task,
                diarize,
                wantWords,
                context.RequestAborted));
        }
        catch (ModelUnavailableException ex)
        {
            return Error(StatusCodes.Status503ServiceUnavailable, ex.Message, param: "model", code: "model_unavailable");
        }
        catch (TranscribeException ex)
        {
            // The native detail can carry paths and internal state; it goes to
            // the log, and the client gets a sentence it can act on.
            Console.Error.WriteLine($"transcribe-server: native error: {ex.Message}");
            return Error(
                StatusCodes.Status500InternalServerError,
                "the native transcription failed; the server log has the detail",
                param: null,
                code: "native_error");
        }
        catch (InvalidOperationException ex)
        {
            return Error(StatusCodes.Status422UnprocessableEntity, ex.Message, param: "file", code: "unprocessable_audio");
        }
        catch (AudioLoadException ex)
        {
            return Error(StatusCodes.Status422UnprocessableEntity, ex.Message, param: "file", code: "undecodable_audio");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"transcribe-server: audio i/o error: {ex}");
            return Error(
                StatusCodes.Status500InternalServerError,
                "the uploaded audio could not be read; the server log has the detail",
                param: null,
                code: "audio_io");
        }
        finally
        {
            TryDelete(audioPath);
        }

        return responseFormat switch
        {
            "text" => Results.Text(output.Text, "text/plain; charset=utf-8"),
            "srt" or "vtt" => Subtitles(output, responseFormat, diarize),
            "verbose_json" => Results.Json(
                new VerboseResponse(
                    task == TranscriptionTask.Translate ? "translate" : "transcribe",
                    output.Language,
                    output.DurationSeconds,
                    output.Text,
                    output.Segments
                        .Select(s => new VerboseSegment(s.Id, s.StartSeconds, s.EndSeconds, s.Text, s.Speaker))
                        .ToList(),
                    output.Words
                        .Select(w => new VerboseWord(w.Text, w.StartSeconds, w.EndSeconds))
                        .ToList()),
                Json),
            _ => Results.Json(new TextResponse(output.Text), Json),
        };
    }

    /// <summary>Renders the merged segments as SubRip or WebVTT.</summary>
    private static IResult Subtitles(TranscriptionOutput output, string format, bool diarize)
    {
        SubtitleCue[] cues = output.Segments
            .Select(s => new SubtitleCue(s.StartSeconds * 1000, s.EndSeconds * 1000, s.Speaker ?? 0, s.Text))
            .ToArray();

        var writer = new StringWriter(CultureInfo.InvariantCulture);
        SubtitleWriter.Write(
            writer,
            format == "srt" ? SubtitleFormat.Srt : SubtitleFormat.Vtt,
            cues,
            speakerLabels: diarize);

        return Results.Text(
            writer.ToString(),
            format == "srt" ? "application/x-subrip; charset=utf-8" : "text/vtt; charset=utf-8");
    }

    private static bool IsPresent(IFormCollection form, string name)
        => form.TryGetValue(name, out StringValues value)
           && !string.IsNullOrWhiteSpace(value.ToString());

    private static bool IsZero(string text)
        => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
           && value == 0;

    private static bool TryReadDiarize(IFormCollection form, bool fallback, out bool diarize)
    {
        diarize = fallback;
        if (!form.TryGetValue("diarize", out StringValues value)
            || string.IsNullOrWhiteSpace(value.ToString()))
        {
            return true;
        }

        string text = value.ToString().Trim();
        if (bool.TryParse(text, out bool parsed))
        {
            diarize = parsed;
            return true;
        }

        if (text == "1")
        {
            diarize = true;
            return true;
        }

        if (text == "0")
        {
            diarize = false;
            return true;
        }

        return false;
    }

    private static bool WantsWords(IFormCollection form)
    {
        // OpenAI clients send either repetitions of 'timestamp_granularities[]'
        // or the bare name; both are accepted.
        foreach (string name in GranularityFields)
        {
            if (!form.TryGetValue(name, out StringValues values))
            {
                continue;
            }

            foreach (string? value in values)
            {
                if (string.Equals(value, "word", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string SafeExtension(string fileName)
    {
        string extension = Path.GetExtension(fileName);
        if (extension.Length is < 2 or > 10)
        {
            return string.Empty;
        }

        foreach (char c in extension)
        {
            if (c != '.' && !char.IsAsciiLetterOrDigit(c))
            {
                return string.Empty;
            }
        }

        return extension.ToLowerInvariant();
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A temp file that cannot be removed is not worth failing a request
            // that already produced its transcript; the OS cleans the directory.
        }
    }

    /// <summary>
    /// Whether the request comes from a page on another origin.
    /// </summary>
    /// <remarks>
    /// A browser sends <c>Origin</c> on a cross-origin POST, and current ones
    /// also send <c>Sec-Fetch-Site</c>; a direct client sends neither. Both are
    /// checked because each covers a case the other misses: <c>Origin: null</c>
    /// from a sandboxed frame is not a URI, and an older browser may omit
    /// <c>Sec-Fetch-Site</c>.
    /// </remarks>
    private static bool IsCrossSite(HttpRequest request)
    {
        string fetchSite = request.Headers["Sec-Fetch-Site"].ToString();
        if (string.Equals(fetchSite, "cross-site", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string origin = request.Headers.Origin.ToString();
        return origin.Length > 0 && !IsSameOrigin(origin, request);
    }

    /// <summary>Whether <paramref name="origin"/> is the origin this request was sent to.</summary>
    private static bool IsSameOrigin(string origin, HttpRequest request)
    {
        if (!Uri.TryCreate(origin, UriKind.Absolute, out Uri? uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return false;
        }

        int port = request.Host.Port ?? (request.IsHttps ? 443 : 80);
        return uri.Port == port
            && string.Equals(uri.Scheme, request.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(BareHost(uri.Host), BareHost(request.Host.Host), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The host without the brackets <see cref="Uri"/> and HostString keep for IPv6.</summary>
    private static string BareHost(string host) => host.Trim('[', ']');

    private static bool FixedTimeEquals(string provided, string expected)
    {
        byte[] a = Encoding.UTF8.GetBytes(provided);
        byte[] b = Encoding.UTF8.GetBytes(expected);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }

    private static IResult Error(int status, string message, string? param, string? code = null)
        => Results.Json(
            new ErrorEnvelope(new ErrorBody(message, "invalid_request_error", param, code)),
            Json,
            statusCode: status);

    private static async Task WriteErrorAsync(
        HttpContext context,
        int status,
        string message,
        string? param,
        string? code)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json; charset=utf-8";
        await context.Response.WriteAsJsonAsync(
            new ErrorEnvelope(new ErrorBody(message, "invalid_request_error", param, code)),
            Json,
            context.RequestAborted);
    }
}
