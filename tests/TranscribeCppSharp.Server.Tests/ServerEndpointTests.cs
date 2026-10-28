namespace TranscribeCppSharp.Server.Tests;

using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using Xunit;

public sealed class ServerEndpointTests
{
    /// <summary>What a request returned, read before the server is disposed.</summary>
    private sealed record Reply(HttpStatusCode Status, string Body, string? MediaType)
    {
        internal JsonNode Json() => JsonNode.Parse(Body)!;
    }

    private static ServerOptions Options(string? apiKey = null, bool diarize = false) => new()
    {
        Model = "fake-model",
        ApiKey = apiKey,
        Diarize = diarize,
        Urls = ["http://127.0.0.1:0"],
    };

    private static async Task<Reply> WithServerAsync(
        ServerOptions options,
        ITranscriptionBackend backend,
        Func<TestServer, Task<HttpResponseMessage>> send)
    {
        await using TestServer server = await TestServer.StartAsync(options, backend);
        using HttpResponseMessage response = await send(server);
        return new Reply(
            response.StatusCode,
            await response.Content.ReadAsStringAsync(),
            response.Content.Headers.ContentType?.MediaType);
    }

    private static Task<Reply> PostAsync(
        ServerOptions options,
        FakeBackend backend,
        MultipartFormDataContent form,
        string path = "/v1/audio/transcriptions")
        => WithServerAsync(options, backend, server => server.Client.PostAsync(path, form));

    [Fact]
    public async Task Transcription_returns_the_text_as_json()
    {
        var backend = new FakeBackend();
        Reply reply = await PostAsync(Options(), backend, TestServer.Form());

        Assert.Equal(HttpStatusCode.OK, reply.Status);
        Assert.Equal("hello world", reply.Json()["text"]!.GetValue<string>());

        TranscriptionRequest request = Assert.Single(backend.Requests);
        Assert.Equal(TranscriptionTask.Transcribe, request.Task);
        Assert.Null(request.Language);
        Assert.False(request.WantWords);
    }

    [Fact]
    public async Task Text_format_returns_a_plain_body()
    {
        Reply reply = await PostAsync(Options(), new FakeBackend(), TestServer.Form(format: "text"));

        Assert.Equal(HttpStatusCode.OK, reply.Status);
        Assert.Equal("text/plain", reply.MediaType);
        Assert.Equal("hello world", reply.Body);
    }

    [Fact]
    public async Task Verbose_json_carries_segments_words_and_the_speaker()
    {
        Reply reply = await PostAsync(
            Options(diarize: true),
            new FakeBackend(),
            TestServer.Form(format: "verbose_json", language: "fr", wantWords: true));

        Assert.Equal(HttpStatusCode.OK, reply.Status);
        JsonNode root = reply.Json();

        Assert.Equal("transcribe", root["task"]!.GetValue<string>());
        Assert.Equal("en", root["language"]!.GetValue<string>());
        Assert.Equal(12.5, root["duration"]!.GetValue<double>());
        Assert.Equal(2, root["segments"]!.AsArray().Count);
        Assert.Equal(2, root["segments"]![0]!["speaker"]!.GetValue<int>());
        Assert.Equal("hello", root["words"]![0]!["word"]!.GetValue<string>());
    }

    [Fact]
    public async Task Verbose_json_omits_the_speaker_when_diarization_is_off()
    {
        Reply reply = await PostAsync(Options(), new FakeBackend(), TestServer.Form(format: "verbose_json"));

        Assert.Null(reply.Json()["segments"]![0]!["speaker"]);
    }

    [Fact]
    public async Task An_unknown_model_is_refused()
    {
        Reply reply = await PostAsync(Options(), new FakeBackend(), TestServer.Form(model: "some-other-model"));

        Assert.Equal(HttpStatusCode.NotFound, reply.Status);
        Assert.Equal("model_not_found", reply.Json()["error"]!["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_missing_file_is_refused()
    {
        Reply reply = await PostAsync(Options(), new FakeBackend(), TestServer.Form(includeFile: false));

        Assert.Equal(HttpStatusCode.BadRequest, reply.Status);
        Assert.Equal("file", reply.Json()["error"]!["param"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("srt")]
    [InlineData("vtt")]
    public async Task Subtitle_formats_return_cues(string format)
    {
        Reply reply = await PostAsync(Options(), new FakeBackend(), TestServer.Form(format: format));

        Assert.Equal(HttpStatusCode.OK, reply.Status);
        Assert.Contains("hello", reply.Body, StringComparison.Ordinal);
        Assert.Contains("world", reply.Body, StringComparison.Ordinal);
        Assert.Contains(
            format == "srt" ? "00:00:00,000 --> 00:00:06,250" : "00:00:00.000 --> 00:00:06.250",
            reply.Body,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Vtt_starts_with_the_webvtt_header()
    {
        Reply reply = await PostAsync(Options(), new FakeBackend(), TestServer.Form(format: "vtt"));

        Assert.StartsWith("WEBVTT\n", reply.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Subtitle_speaker_labels_follow_the_diarize_setting()
    {
        Reply withSpeakers = await PostAsync(
            Options(diarize: true),
            new FakeBackend(),
            TestServer.Form(format: "srt"));
        Reply withoutSpeakers = await PostAsync(
            Options(diarize: false),
            new FakeBackend(),
            TestServer.Form(format: "srt"));

        Assert.Contains("Speaker 2: hello", withSpeakers.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("Speaker", withoutSpeakers.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unknown_response_format_is_refused()
    {
        Reply reply = await PostAsync(Options(), new FakeBackend(), TestServer.Form(format: "docx"));

        Assert.Equal(HttpStatusCode.BadRequest, reply.Status);
        JsonNode error = reply.Json()["error"]!;
        Assert.Equal("unsupported_response_format", error["code"]!.GetValue<string>());
        Assert.Contains("verbose_json", error["message"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Prompt_and_nonzero_temperature_are_refused_rather_than_ignored()
    {
        Reply prompt = await PostAsync(Options(), new FakeBackend(), TestServer.Form(prompt: "a hint"));
        Reply temperature = await PostAsync(Options(), new FakeBackend(), TestServer.Form(temperature: "0.4"));

        Assert.Equal(HttpStatusCode.BadRequest, prompt.Status);
        Assert.Equal(HttpStatusCode.BadRequest, temperature.Status);
    }

    [Fact]
    public async Task Temperature_zero_is_accepted()
    {
        Reply reply = await PostAsync(Options(), new FakeBackend(), TestServer.Form(temperature: "0"));

        Assert.Equal(HttpStatusCode.OK, reply.Status);
    }

    [Fact]
    public async Task Translations_run_the_translate_task()
    {
        var backend = new FakeBackend();
        Reply reply = await PostAsync(
            Options(),
            backend,
            TestServer.Form(),
            path: "/v1/audio/translations");

        Assert.Equal(HttpStatusCode.OK, reply.Status);
        Assert.Equal(TranscriptionTask.Translate, Assert.Single(backend.Requests).Task);
    }

    [Fact]
    public async Task The_diarize_field_beats_the_server_default()
    {
        var backend = new FakeBackend();
        await PostAsync(Options(diarize: false), backend, TestServer.Form(diarize: true));

        Assert.True(Assert.Single(backend.Requests).Diarize);
    }

    [Fact]
    public async Task Word_granularity_is_forwarded()
    {
        var backend = new FakeBackend();
        await PostAsync(Options(), backend, TestServer.Form(wantWords: true));

        Assert.True(Assert.Single(backend.Requests).WantWords);
    }

    [Fact]
    public async Task An_upload_over_the_limit_is_refused_with_413()
    {
        var limited = new ServerOptions
        {
            Model = "fake-model",
            Urls = ["http://127.0.0.1:0"],
            MaxUploadBytes = 64,
        };

        Reply reply = await PostAsync(limited, new FakeBackend(), TestServer.Form());

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, reply.Status);
        Assert.Equal("payload_too_large", reply.Json()["error"]!["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task The_api_key_is_required_but_health_stays_open()
    {
        await using TestServer server = await TestServer.StartAsync(Options(apiKey: "s3cret"), new FakeBackend());

        Reply anonymous = await ReadAsync(await server.Client.PostAsync(
            "/v1/audio/transcriptions",
            TestServer.Form()));

        using var authorizedRequest = new HttpRequestMessage(HttpMethod.Post, "/v1/audio/transcriptions")
        {
            Content = TestServer.Form(),
        };
        authorizedRequest.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "s3cret");
        Reply authorized = await ReadAsync(await server.Client.SendAsync(authorizedRequest));

        Reply health = await ReadAsync(await server.Client.GetAsync("/health"));

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.Status);
        Assert.Equal(HttpStatusCode.OK, authorized.Status);
        Assert.Equal(HttpStatusCode.OK, health.Status);
    }

    [Fact]
    public async Task A_wrong_api_key_is_refused()
    {
        await using TestServer server = await TestServer.StartAsync(Options(apiKey: "s3cret"), new FakeBackend());
        using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/models");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "nope");

        Reply reply = await ReadAsync(await server.Client.SendAsync(request));

        Assert.Equal(HttpStatusCode.Unauthorized, reply.Status);
    }

    [Fact]
    public async Task Models_lists_the_single_model_this_server_serves()
    {
        Reply reply = await WithServerAsync(
            Options(),
            new FakeBackend(),
            server => server.Client.GetAsync("/v1/models"));

        Assert.Equal(HttpStatusCode.OK, reply.Status);
        JsonNode root = reply.Json();
        Assert.Equal("list", root["object"]!.GetValue<string>());
        Assert.Equal("fake-model", root["data"]![0]!["id"]!.GetValue<string>());
        Assert.Equal("transcribe.cpp", root["data"]![0]!["owned_by"]!.GetValue<string>());
    }

    [Fact]
    public async Task Health_reports_whether_the_model_is_loaded()
    {
        Reply reply = await WithServerAsync(
            Options(),
            new FakeBackend(),
            server => server.Client.GetAsync("/health"));

        Assert.Equal("ok", reply.Json()["status"]!.GetValue<string>());
        Assert.True(reply.Json()["loaded"]!.GetValue<bool>());
    }

    [Fact]
    public async Task ACrossSiteOriginIsRefusedBeforeTheAudioIsRead()
    {
        var backend = new FakeBackend();
        await using TestServer server = await TestServer.StartAsync(Options(), backend);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/audio/transcriptions")
        {
            Content = TestServer.Form(),
        };
        request.Headers.Add("Origin", "https://evil.example");

        using HttpResponseMessage response = await server.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("cross_site_request", Code(await ReadAsync(response)));
        Assert.Empty(backend.Requests);
    }

    [Fact]
    public async Task ASandboxedFrameSendsOriginNullAndIsRefused()
    {
        var backend = new FakeBackend();
        await using TestServer server = await TestServer.StartAsync(Options(), backend);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/audio/transcriptions")
        {
            Content = TestServer.Form(),
        };
        request.Headers.Add("Origin", "null");

        using HttpResponseMessage response = await server.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(backend.Requests);
    }

    [Fact]
    public async Task ASameOriginBrowserRequestIsAccepted()
    {
        // The check must not get in the way of a page served from the same
        // address (a local UI, or a proxy that forwards Origin unchanged).
        var backend = new FakeBackend();
        await using TestServer server = await TestServer.StartAsync(Options(), backend);
        string origin = server.Client.BaseAddress!.GetLeftPart(UriPartial.Authority);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/audio/transcriptions")
        {
            Content = TestServer.Form(),
        };
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Sec-Fetch-Site", "same-origin");

        using HttpResponseMessage response = await server.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(backend.Requests);
    }

    [Fact]
    public async Task AHostOutsideTheAllowedListIsRefused()
    {
        var backend = new FakeBackend();
        var options = new ServerOptions
        {
            Model = "fake-model",
            Urls = ["http://127.0.0.1:0"],
            AllowedHosts = ["127.0.0.1"],
        };
        await using TestServer server = await TestServer.StartAsync(options, backend);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/audio/transcriptions")
        {
            Content = TestServer.Form(),
        };
        request.Headers.Host = "rebound.example";

        using HttpResponseMessage response = await server.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("host_not_allowed", Code(await ReadAsync(response)));
        Assert.Empty(backend.Requests);
    }

    [Fact]
    public async Task ARequestBeyondTheLimitIsRefusedWith429()
    {
        var backend = new BlockingBackend();
        var options = new ServerOptions
        {
            Model = "fake-model",
            Urls = ["http://127.0.0.1:0"],
            MaxPendingRequests = 1,
        };
        await using TestServer server = await TestServer.StartAsync(options, backend);

        Task<HttpResponseMessage> first = server.Client.PostAsync(
            "/v1/audio/transcriptions", TestServer.Form());
        await backend.Entered;

        using HttpResponseMessage second = await server.Client.PostAsync(
            "/v1/audio/transcriptions", TestServer.Form());

        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(5), second.Headers.RetryAfter?.Delta);
        Assert.Equal(1, backend.Calls);

        backend.Release();
        using HttpResponseMessage firstResponse = await first;
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
    }

    [Fact]
    public async Task AFailureInsideTheBackendIsReportedWithoutItsDetail()
    {
        var backend = new ThrowingBackend(
            () => new IOException("no space left on /tmp/a-path-the-client-does-not-need"));

        Reply reply = await WithServerAsync(
            Options(),
            backend,
            server => server.Client.PostAsync("/v1/audio/transcriptions", TestServer.Form()));

        Assert.Equal(HttpStatusCode.InternalServerError, reply.Status);
        Assert.Equal("audio_io", reply.Json()["error"]!["code"]!.GetValue<string>());
        Assert.DoesNotContain("a-path-the-client-does-not-need", reply.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResponsesAreNotSniffed()
    {
        // The transcript goes out as text/plain or application/x-subrip, and it
        // is whatever the audio said: without nosniff a browser is free to
        // second-guess the type it was given.
        await using TestServer server = await TestServer.StartAsync(Options(), new FakeBackend());
        using HttpResponseMessage response = await server.Client.GetAsync("/health");

        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
    }

    private static string? Code(Reply reply) => reply.Json()["error"]?["code"]?.GetValue<string>();

    private static async Task<Reply> ReadAsync(HttpResponseMessage response)
    {
        using (response)
        {
            return new Reply(
                response.StatusCode,
                await response.Content.ReadAsStringAsync(),
                response.Content.Headers.ContentType?.MediaType);
        }
    }
}
