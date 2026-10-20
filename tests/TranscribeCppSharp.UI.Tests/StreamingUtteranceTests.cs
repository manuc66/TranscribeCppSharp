using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using TranscribeCppSharp.Models;
using TranscribeCppSharp.UI.Models;
using TranscribeCppSharp.UI.Services;
using TranscribeCppSharp.UI.ViewModels;
using Xunit;

// Interop has its own Task and StreamUpdate; the ones this file means are these.
using Task = System.Threading.Tasks.Task;
using StreamUpdate = TranscribeCppSharp.UI.Models.StreamUpdate;

namespace TranscribeCppSharp.UI.Tests;

/// <summary>
/// A long live stream is closed into utterances of bounded length, so the native
/// stream's working set — which grows with the utterance — does not grow with the
/// whole session. The committed text of each closed utterance is kept, so the
/// display stays one transcript.
/// </summary>
/// <remarks>
/// The decision half is exercised directly. The native calls that close an
/// utterance (finalize/reset/begin) live in
/// <see cref="TranscriptionService.StreamTranscribeAsync"/> and are not reachable
/// from a test: they need a microphone. The wiring test below only stands in for
/// the service and checks what the view model passes to it.
/// </remarks>
[Collection("UiGlobalState")]
public class StreamingUtteranceTests
{
    [Fact]
    public void ZeroKeepsASingleUtteranceForTheWholeSession()
    {
        var rotation = new UtteranceRotation(0);

        Assert.False(rotation.IsEnabled);
        Assert.False(rotation.AddFed(int.MaxValue));
        Assert.False(rotation.AddFed(16000));
        Assert.Empty(rotation.ClosedCommittedText);
    }

    [Fact]
    public void AnUtteranceIsClosedAfterItsConfiguredAudio()
    {
        var rotation = new UtteranceRotation(2); // 2 s = 32 000 samples
        Assert.True(rotation.IsEnabled);

        Assert.False(rotation.AddFed(16000));
        Assert.True(rotation.AddFed(16000));

        // The counter restarts, so half an utterance again is not enough.
        Assert.False(rotation.AddFed(16000));
        Assert.True(rotation.AddFed(16000));
    }

    [Fact]
    public void SurplusAudioCarriesIntoTheNextUtterance()
    {
        var rotation = new UtteranceRotation(1); // 16 000 samples

        Assert.True(rotation.AddFed(20000));  // 4 000 carried
        Assert.False(rotation.AddFed(11000)); // 15 000, not yet
        Assert.True(rotation.AddFed(1000));   // 16 000, closes
    }

    [Fact]
    public void ClosedTextIsJoinedWithoutGlueingWords()
    {
        var rotation = new UtteranceRotation(1);

        rotation.Close("hello");
        rotation.Close("world");
        rotation.Close(" again"); // its own leading space is not doubled

        Assert.Equal("hello world again", rotation.ClosedCommittedText);
    }

    [Fact]
    public void ATrailingSpaceIsNotDoubled()
    {
        var rotation = new UtteranceRotation(1);

        rotation.Close("hi ");
        rotation.Close("there");

        Assert.Equal("hi there", rotation.ClosedCommittedText);
    }

    [Fact]
    public void AnEmptyCloseAddsNothing()
    {
        var rotation = new UtteranceRotation(1);

        rotation.Close(string.Empty);
        rotation.Close("word");

        Assert.Equal("word", rotation.ClosedCommittedText);
    }

    [Fact]
    public void TheUtteranceLengthDefaultsToThirtySeconds()
    {
        using var settings = new UiSettingsScope();
        StreamingViewModel viewModel = WithEmptyCache(new StubTranscriptionService());

        Assert.Equal(30, viewModel.UtteranceSeconds);
    }

    [AvaloniaFact]
    public async Task TheConfiguredUtteranceLengthReachesTheService()
    {
        using var settings = new UiSettingsScope();
        var service = new CapturingStreamingService();
        StreamingViewModel viewModel = WithEmptyCache(service);
        WaitUntil(() => !viewModel.IsCheckingCapabilities);

        viewModel.UtteranceSeconds = 45;

        Task run = viewModel.StartStreamingCommand.ExecuteAsync(null);
        WaitUntil(() => service.LastOptions is not null);
        await run;

        Assert.Equal(45, service.LastOptions!.UtteranceSeconds);
    }

    [AvaloniaFact]
    public async Task ZeroReachesTheServiceAndDisablesRotation()
    {
        using var settings = new UiSettingsScope();
        var service = new CapturingStreamingService();
        StreamingViewModel viewModel = WithEmptyCache(service);
        WaitUntil(() => !viewModel.IsCheckingCapabilities);

        viewModel.UtteranceSeconds = 0;

        Task run = viewModel.StartStreamingCommand.ExecuteAsync(null);
        WaitUntil(() => service.LastOptions is not null);
        await run;

        Assert.Equal(0, service.LastOptions!.UtteranceSeconds);
    }

    /// <summary>A view model over an empty model cache, so no weights are probed.</summary>
    private static StreamingViewModel WithEmptyCache(ITranscriptionService service)
    {
        string previous = ModelStore.CacheRootOverride ?? string.Empty;
        bool hadOverride = ModelStore.CacheRootOverride is not null;
        ModelStore.CacheRootOverride = Path.Combine(
            Path.GetTempPath(), "tcsharp-utterance-" + Guid.NewGuid().ToString("N"));
        try
        {
            return new StreamingViewModel(service, new SettingsViewModel(), new ModelCapabilityCache());
        }
        finally
        {
            ModelStore.CacheRootOverride = hadOverride ? previous : null;
        }
    }

    private static void WaitUntil(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            if (condition())
            {
                return;
            }

            Thread.Sleep(10);
        }

        Assert.Fail("the condition never became true");
    }

    /// <summary>Records the options a Start passed, and runs no audio.</summary>
    private sealed class CapturingStreamingService : ITranscriptionService
    {
        public StreamOptions? LastOptions { get; private set; }

        public Task<TranscriptionResult> TranscribeAsync(
            string audioPath, string modelPath, TranscriptionOptions options,
            IProgress<double>? progress = null, IProgress<TranscriptionPhase>? phase = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public async IAsyncEnumerable<StreamUpdate> StreamTranscribeAsync(
            string modelPath, StreamOptions options,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            LastOptions = options;
            await Task.CompletedTask;
            yield break;
        }

        public Task<List<BatchItemResult>> BatchTranscribeAsync(
            IReadOnlyList<string> audioPaths, string modelPath, TranscriptionOptions options,
            IProgress<int>? progress = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
