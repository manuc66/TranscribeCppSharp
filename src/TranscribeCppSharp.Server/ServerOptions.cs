namespace TranscribeCppSharp.Server;

using System.Globalization;

/// <summary>
/// What the server was started with. Parsed from the command line, with the API
/// key also readable from the environment.
/// </summary>
/// <remarks>
/// Every default here is stated in <see cref="HelpText"/> and in the docs page,
/// because a server that silently accepts more than it was asked to (downloads,
/// open access, gigabytes of upload) is a server nobody can reason about.
/// </remarks>
internal sealed class ServerOptions
{
    /// <summary>
    /// Largest <c>--max-upload-mb</c> whose byte count still fits in a long.
    /// </summary>
    /// <remarks>
    /// The value is multiplied by 1024 * 1024 to become <see cref="MaxUploadBytes"/>.
    /// An unchecked overflow turned that negative, and Kestrel then refused to start
    /// with an <c>ArgumentOutOfRangeException</c> and a stack trace — an operator
    /// typo should be a one-line error, not a crash.
    /// </remarks>
    internal const long MaxUploadMbLimit = long.MaxValue / (1024 * 1024);

    /// <summary>Environment variable read when <c>--api-key</c> is absent.</summary>
    internal const string ApiKeyVariable = "TRANSCRIBE_SERVER_API_KEY";

    /// <summary>The help text, printed by <c>--help</c> and quoted by the docs.</summary>
    internal const string HelpText = """
        An OpenAI-compatible speech-to-text endpoint over transcribe.cpp.

        Usage: transcribe-server --model <alias|path> [options]

          --model <m>          required: a GGUF file path or a curated alias
                               (an alias is downloaded only with --allow-download)
          --backend <b>        auto (default), cpu, cpu-accel, metal, vulkan,
                               cuda, rocm. 'auto' uses a GPU when one initializes
          --chunk <sec>        max window per native call (default 300); longer
                               audio is split with a 1 s overlap and the seam is
                               deduplicated. The model's own limit wins when lower
          --diarize            request speaker attribution (default off). With
                               --diarizer it runs as a second pass; otherwise the
                               ASR model is asked, and may not support it
          --diarizer <m>       a DIARIZE-role model (e.g. a Sortformer GGUF) run
                               over the whole file after the transcript, then
                               joined to the segments. Both models stay loaded
          --api-key <k>        require 'Authorization: Bearer <k>'. Prefer
                               TRANSCRIBE_SERVER_API_KEY: a command line is
                               visible in 'ps'. Without a key, anyone who can
                               reach the port can transcribe
          --allow-download     let a request fetch the alias when it is not cached
          --max-upload-mb <n>  refuse uploads larger than n MiB (default 1024)
          --max-pending <n>    refuse with 429 when n requests are already
                               being handled (default 4), before the upload is
                               written to disk
          --allowed-host <h>   also refuse requests whose Host is not h (repeat
                               or separate with ';'); the defense against DNS
                               rebinding, off by default because a reverse proxy
                               forwards its own name
          --urls <u>           listen address (default http://127.0.0.1:8080)
          --help               show this help

        Endpoints:
          POST /v1/audio/transcriptions  multipart: file, model, language,
                                         response_format (json|text|verbose_json|
                                         srt|vtt),
                                         timestamp_granularities[] (segment|word),
                                         diarize
          POST /v1/audio/translations    the same fields; runs transcribe.cpp's
                                         translate task
          GET  /v1/models                the single model this server serves
          GET  /health                   liveness and whether the model is loaded

        Requests are serialized: transcribe.cpp allows one run per model at a
        time, so a second request waits instead of corrupting the first.

        Refused with a 400 rather than silently ignored: prompt, temperature,
        and every field not listed above. verbose_json carries the fields this
        library has; OpenAI's avg_logprob, compression_ratio and token arrays
        are not produced because transcribe.cpp does not return them.
        """;

    /// <summary>GGUF path or curated alias to serve.</summary>
    internal required string Model { get; init; }

    /// <summary>Compute backend name; <c>auto</c> by default.</summary>
    internal string Backend { get; init; } = "auto";

    /// <summary>Largest window handed to one native call, in seconds.</summary>
    internal int ChunkSeconds { get; init; } = 300;

    /// <summary>Bearer token required on every endpoint but <c>/health</c>.</summary>
    internal string? ApiKey { get; init; }

    /// <summary>
    /// Whether <see cref="ApiKey"/> came from <c>--api-key</c> rather than the
    /// environment, so the banner can say it is visible to other local users.
    /// </summary>
    internal bool ApiKeyFromCommandLine { get; init; }

    /// <summary>Whether a request may trigger a model download.</summary>
    internal bool AllowDownloads { get; init; }

    /// <summary>Default speaker attribution for a request that does not say.</summary>
    internal bool Diarize { get; init; }

    /// <summary>
    /// A DIARIZE-role model run as a second pass, or null to rely on the ASR
    /// model's own attribution.
    /// </summary>
    internal string? Diarizer { get; init; }

    /// <summary>Upload cap, in bytes.</summary>
    internal long MaxUploadBytes { get; init; } = 1024L * 1024 * 1024;

    /// <summary>
    /// How many requests may be in flight before new ones are refused with 429.
    /// </summary>
    /// <remarks>
    /// The native library allows one run at a time, so every extra request is a
    /// queued upload on disk. This is the bound on that queue: a client that
    /// ignores it gets a 429 instead of a gigabyte staged in the temp directory.
    /// </remarks>
    internal int MaxPendingRequests { get; init; } = 4;

    /// <summary>
    /// Host names accepted in the <c>Host</c> header, or empty for any.
    /// </summary>
    /// <remarks>
    /// Off by default: <c>curl http://127.0.0.1:8080</c> and a reverse proxy
    /// forwarding its own name both work, and only the operator knows which names
    /// are legitimate. Setting it is what closes DNS rebinding, which the
    /// <c>Origin</c> check cannot see (the browser thinks the rebound name is the
    /// origin it asked for).
    /// </remarks>
    internal string[] AllowedHosts { get; init; } = [];

    /// <summary>Listen addresses.</summary>
    internal string[] Urls { get; init; } = ["http://127.0.0.1:8080"];

    /// <summary>
    /// Parses the command line. Returns false with a message the caller prints;
    /// <paramref name="help"/> is true when <c>--help</c> was the request.
    /// </summary>
    internal static bool TryParse(
        string[] args,
        Func<string, string?>? environment,
        out ServerOptions? options,
        out string? error,
        out bool help)
    {
        environment ??= Environment.GetEnvironmentVariable;
        Func<string, string?> readEnvironment = environment;

        string? model = null;
        string backend = "auto";
        int chunkSeconds = 300;
        string? apiKey = null;
        bool allowDownloads = false;
        bool diarize = false;
        string diarizer = string.Empty;
        long maxUploadMb = 1024;
        int maxPending = 4;
        var allowedHosts = new List<string>();
        var urls = new List<string> { "http://127.0.0.1:8080" };

        options = null;
        error = null;
        help = false;

        for (int i = 0; i < args.Length; i++)
        {
            string argument = args[i];
            switch (argument)
            {
                case "--help" or "-h":
                    help = true;
                    return true;

                case "--model":
                    if (!TakeValue(args, ref i, argument, out model))
                    {
                        error = $"{argument} expects a model path or alias";
                        return false;
                    }

                    break;

                case "--backend":
                    if (!TakeValue(args, ref i, argument, out backend))
                    {
                        error = $"{argument} expects one of: auto, cpu, cpu-accel, metal, vulkan, cuda, rocm";
                        return false;
                    }

                    break;

                case "--chunk":
                    if (!TakeValue(args, ref i, argument, out string? chunkText)
                        || !int.TryParse(chunkText, NumberStyles.Integer, CultureInfo.InvariantCulture, out chunkSeconds)
                        || chunkSeconds < 2)
                    {
                        error = $"{argument} expects a whole number of seconds, at least 2";
                        return false;
                    }

                    break;

                case "--api-key":
                    if (!TakeValue(args, ref i, argument, out apiKey))
                    {
                        error = $"{argument} expects a token";
                        return false;
                    }

                    break;

                case "--allow-download":
                    allowDownloads = true;
                    break;

                case "--diarize":
                    diarize = true;
                    break;

                case "--diarizer":
                    if (!TakeValue(args, ref i, argument, out diarizer))
                    {
                        error = $"{argument} expects a model path or alias";
                        return false;
                    }

                    break;

                case "--max-upload-mb":
                    if (!TakeValue(args, ref i, argument, out string? sizeText)
                        || !long.TryParse(sizeText, NumberStyles.Integer, CultureInfo.InvariantCulture, out maxUploadMb)
                        || maxUploadMb < 1
                        || maxUploadMb > MaxUploadMbLimit)
                    {
                        error = $"{argument} expects a whole number of MiB between 1 and {MaxUploadMbLimit}";
                        return false;
                    }

                    break;

                case "--max-pending":
                    if (!TakeValue(args, ref i, argument, out string? pendingText)
                        || !int.TryParse(pendingText, NumberStyles.Integer, CultureInfo.InvariantCulture, out maxPending)
                        || maxPending < 1)
                    {
                        error = $"{argument} expects a whole number of requests, at least 1";
                        return false;
                    }

                    break;

                case "--allowed-host":
                    if (!TakeValue(args, ref i, argument, out string? hostText)
                        || hostText.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) is not { Length: > 0 } hosts)
                    {
                        error = $"{argument} expects a host name such as 127.0.0.1 or transcribe.example.com";
                        return false;
                    }

                    allowedHosts.AddRange(hosts);
                    break;

                case "--urls":
                    if (!TakeValue(args, ref i, argument, out string? urlText))
                    {
                        error = $"{argument} expects a listen address such as http://127.0.0.1:8080";
                        return false;
                    }

                    urls.Clear();
                    urls.AddRange(urlText.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                    if (urls.Count == 0)
                    {
                        error = $"{argument} expects at least one address";
                        return false;
                    }

                    break;

                default:
                    error = $"unknown argument '{argument}' (see --help)";
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(model))
        {
            error = "--model is required: pass a GGUF path or a curated alias (see --help)";
            return false;
        }

        bool keyFromCommandLine = !string.IsNullOrWhiteSpace(apiKey);
        apiKey = string.IsNullOrWhiteSpace(apiKey) ? readEnvironment(ApiKeyVariable) : apiKey;

        var candidate = new ServerOptions
        {
            Model = model.Trim(),
            Backend = backend.Trim().ToLowerInvariant(),
            ChunkSeconds = chunkSeconds,
            ApiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim(),
            ApiKeyFromCommandLine = keyFromCommandLine,
            AllowDownloads = allowDownloads,
            Diarize = diarize,
            Diarizer = string.IsNullOrWhiteSpace(diarizer) ? null : diarizer.Trim(),
            MaxUploadBytes = maxUploadMb * 1024 * 1024,
            MaxPendingRequests = maxPending,
            AllowedHosts = [.. allowedHosts],
            Urls = [.. urls],
        };

        if (!TryParseBackendName(candidate.Backend, out _))
        {
            error = $"unknown --backend '{backend}' (expected one of: auto, cpu, cpu-accel, metal, vulkan, cuda, rocm)";
            return false;
        }

        options = candidate;
        return true;
    }

    /// <summary>
    /// Maps a <c>--backend</c> value to the native request. Kept free of native
    /// calls so it is testable on a machine with no GPU.
    /// </summary>
    internal static bool TryParseBackendName(string name, out TranscribeCppSharp.Interop.BackendRequest request)
    {
        TranscribeCppSharp.Interop.BackendRequest? parsed = name switch
        {
            "auto" => TranscribeCppSharp.Interop.BackendRequest.BackendAuto,
            "cpu" => TranscribeCppSharp.Interop.BackendRequest.BackendCpu,
            "cpu-accel" => TranscribeCppSharp.Interop.BackendRequest.BackendCpuAccel,
            "metal" => TranscribeCppSharp.Interop.BackendRequest.BackendMetal,
            "vulkan" => TranscribeCppSharp.Interop.BackendRequest.BackendVulkan,
            "cuda" => TranscribeCppSharp.Interop.BackendRequest.BackendCuda,
            "rocm" => TranscribeCppSharp.Interop.BackendRequest.BackendRocm,
            _ => null,
        };

        request = parsed ?? TranscribeCppSharp.Interop.BackendRequest.BackendAuto;
        return parsed is not null;
    }

    private static bool TakeValue(string[] args, ref int index, string name, out string value)
    {
        if (index + 1 >= args.Length)
        {
            value = string.Empty;
            return false;
        }

        value = args[++index];
        return !(name == "--model" && string.IsNullOrWhiteSpace(value));
    }
}
