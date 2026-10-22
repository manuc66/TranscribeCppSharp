namespace TranscribeCppSharp.Server.Tests;

using System.Net.Http;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// A backend that answers instantly, so the HTTP contract can be tested on a
/// machine with no model, no native runtime and no GPU.
/// </summary>
internal sealed class FakeBackend : ITranscriptionBackend
{
    /// <summary>Every request the endpoints produced, in order.</summary>
    internal List<TranscriptionRequest> Requests { get; } = [];

    public string ModelId => "fake-model";

    public bool IsModelLoaded => true;

    public Task<TranscriptionOutput> TranscribeAsync(TranscriptionRequest request)
    {
        Requests.Add(request);
        return Task.FromResult(new TranscriptionOutput(
            "hello world",
            "en",
            12.5,
            [
                new TranscriptionSegment(0, 0, 6.25, "hello", request.Diarize ? 2 : null),
                new TranscriptionSegment(1, 6.25, 12.5, "world", request.Diarize ? 2 : null),
            ],
            [
                new TranscriptionWord("hello", 0, 1.2),
                new TranscriptionWord("world", 6.3, 7.4),
            ]));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>A running server on a loopback port, plus the client pointed at it.</summary>
internal sealed class TestServer : IAsyncDisposable
{
    private TestServer(WebApplication app, HttpClient client)
    {
        App = app;
        Client = client;
    }

    internal WebApplication App { get; }

    internal HttpClient Client { get; }

    internal static async Task<TestServer> StartAsync(ServerOptions options, ITranscriptionBackend backend)
    {
        WebApplication app = ServerApp.Build(options, backend);
        await app.StartAsync();

        IServer server = app.Services.GetRequiredService<IServer>();
        string address = server.Features.Get<IServerAddressesFeature>()!.Addresses.First();
        var client = new HttpClient { BaseAddress = new Uri(address) };
        return new TestServer(app, client);
    }

    /// <summary>A multipart body with the fields a client would send.</summary>
    internal static MultipartFormDataContent Form(
        string model = "fake-model",
        string? format = null,
        string? language = null,
        bool? diarize = null,
        bool wantWords = false,
        bool includeFile = true,
        string? prompt = null,
        string? temperature = null)
    {
        var form = new MultipartFormDataContent();
        if (includeFile)
        {
            var file = new ByteArrayContent(Encoding.UTF8.GetBytes("not really a wav; the fake backend never reads it"));
            file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("audio/wav");
            form.Add(file, "file", "clip.wav");
        }

        form.Add(new StringContent(model), "model");
        if (format is not null)
        {
            form.Add(new StringContent(format), "response_format");
        }

        if (language is not null)
        {
            form.Add(new StringContent(language), "language");
        }

        if (diarize is not null)
        {
            form.Add(new StringContent(diarize.Value ? "true" : "false"), "diarize");
        }

        if (wantWords)
        {
            form.Add(new StringContent("word"), "timestamp_granularities[]");
        }

        if (prompt is not null)
        {
            form.Add(new StringContent(prompt), "prompt");
        }

        if (temperature is not null)
        {
            form.Add(new StringContent(temperature), "temperature");
        }

        return form;
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await App.StopAsync();
        await App.DisposeAsync();
    }
}
