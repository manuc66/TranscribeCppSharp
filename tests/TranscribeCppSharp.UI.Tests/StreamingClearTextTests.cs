using System.Runtime.CompilerServices;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using TranscribeCppSharp.Interop;
using TranscribeCppSharp.UI.Models;
using TranscribeCppSharp.UI.Services;
using TranscribeCppSharp.UI.ViewModels;
using Xunit;

// Interop has its own Task and StreamUpdate; the ones this file means are these.
using Task = System.Threading.Tasks.Task;
using StreamUpdate = TranscribeCppSharp.UI.Models.StreamUpdate;

namespace TranscribeCppSharp.UI.Tests;

/// <summary>
/// The streaming tab's Clear empties the two text areas.
/// </summary>
/// <remarks>
/// The native committed prefix is append-only and the stream is not reset, so a
/// clear during a run is a display reset: text already committed stays in the
/// native stream (and its audio context with it), it is just not shown any more.
/// The tests pin both halves of that — empties now, resumes from the click — so
/// the behaviour cannot quietly turn into a native reset or into a no-op.
/// </remarks>
[Collection("UiGlobalState")]
public class StreamingClearTextTests
{
    /// <summary>A cache whose probe never loads weights, whatever is on disk.</summary>
    /// <remarks>
    /// The default alias is MOSS, which some machines have downloaded. Without
    /// this the tab would load it for real; the probe is a stub here, so the
    /// test neither depends on nor disturbs the model cache.
    /// </remarks>
    private static ModelCapabilityCache StreamingCache()
        => new() { Probe = _ => new ModelCapabilitySnapshot(
            Architecture: "moonshine_streaming",
            Variant: "test",
            Roles: TranscribeRole.Asr,
            SupportsTranslate: false,
            SupportsLanguageDetect: false,
            SupportsStreaming: true,
            SupportsSpecDecode: false,
            Features: new HashSet<Feature>(),
            Languages: [],
            TranslateTargetLanguages: []) };

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

    /// <summary>Outside a run, Clear empties both areas.</summary>
    [AvaloniaFact]
    public void ClearEmptiesBothTextAreas()
    {
        using var settings = new UiSettingsScope();
        var viewModel = new StreamingViewModel(
            new ScriptedStreamingService(), new SettingsViewModel(), StreamingCache())
        {
            CommittedText = "committed so far",
            TentativeText = "tentative so far",
        };

        viewModel.ClearTextCommand.Execute(null);

        Assert.Empty(viewModel.CommittedText);
        Assert.Empty(viewModel.TentativeText);
    }

    /// <summary>
    /// During a run, Clear hides what was already committed and what comes next
    /// is shown from the click on — the earlier prefix is not made visible again.
    /// </summary>
    [AvaloniaFact]
    public async Task ClearWhileStreamingHidesTheTextAlreadyShown()
    {
        using var settings = new UiSettingsScope();
        var service = new ScriptedStreamingService();
        var viewModel = new StreamingViewModel(service, new SettingsViewModel(), StreamingCache());

        // A downloaded default model is probed when the tab is built; the stub
        // answers at once, and Start needs that answer to allow a run.
        WaitUntil(() => !viewModel.IsCheckingCapabilities);
        Assert.True(viewModel.SelectedModelStreams);

        Task run = viewModel.StartStreamingCommand.ExecuteAsync(null);
        WaitUntil(() => viewModel.CommittedText == "hello world");
        Assert.Equal("te", viewModel.TentativeText);

        viewModel.ClearTextCommand.Execute(null);
        Assert.Empty(viewModel.CommittedText);
        Assert.Empty(viewModel.TentativeText);

        service.Release();
        WaitUntil(() => viewModel.CommittedText.Length > 0);

        // What was committed before the click is hidden, not merely cleared once.
        Assert.Equal(" again", viewModel.CommittedText);
        Assert.Empty(viewModel.TentativeText);

        await run;
        Assert.False(viewModel.IsStreaming);
        Assert.Equal("Streaming complete.", viewModel.StatusMessage);
    }

    /// <summary>Yields two updates, with a gate between them so a test can act mid-run.</summary>
    private sealed class ScriptedStreamingService : ITranscriptionService
    {
        private readonly TaskCompletionSource _gate =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release() => _gate.TrySetResult();

        public Task<TranscriptionResult> TranscribeAsync(
            string audioPath, string modelPath, TranscriptionOptions options,
            IProgress<double>? progress = null, IProgress<string>? liveText = null, IProgress<TranscriptionPhase>? phase = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public async IAsyncEnumerable<StreamUpdate> StreamTranscribeAsync(
            string modelPath, StreamOptions options,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new StreamUpdate
            {
                FullText = "hello world te",
                CommittedText = "hello world",
                TentativeText = "te",
            };

            await _gate.Task.WaitAsync(cancellationToken);

            yield return new StreamUpdate
            {
                FullText = "hello world again",
                CommittedText = "hello world again",
                TentativeText = string.Empty,
            };
        }

        public Task<MemoryProbeResult> ProbeMemoryAsync(
            string audioPath, string modelPath, TranscriptionOptions options,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<List<BatchItemResult>> BatchTranscribeAsync(
            IReadOnlyList<string> audioPaths, string modelPath, TranscriptionOptions options,
            IProgress<int>? progress = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
