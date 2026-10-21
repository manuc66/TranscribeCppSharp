using TranscribeCppSharp.Models;
using TranscribeCppSharp.UI.Models;
using TranscribeCppSharp.UI.Services;
using TranscribeCppSharp.UI.ViewModels;
using Xunit;

namespace TranscribeCppSharp.UI.Tests;

/// <summary>
/// A run with a separate diarizer is two passes. The status must say when the
/// second one starts, because pass one finishes with the progress bar full and the
/// reader would otherwise think the run is over.
/// </summary>
[Collection("UiGlobalState")]
public class DiarizationPhaseTests
{
    [Fact]
    public async Task TheStatusSaysDiarizingDuringTheSecondPass()
    {
        var service = new TwoPassService();
        string? previous = ModelStore.CacheRootOverride;
        ModelStore.CacheRootOverride = Path.Combine(
            Path.GetTempPath(), "tcsharp-phase-" + Guid.NewGuid().ToString("N"));
        using var settings = new UiSettingsScope();
        string audio = Path.Combine(Path.GetTempPath(), "tcsharp-phase-" + Guid.NewGuid().ToString("N") + ".wav");
        File.WriteAllText(audio, string.Empty);
        SynchronizationContext? previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new ImmediateSynchronizationContext());
        try
        {
            var viewModel = new TranscriptionViewModel(
                service, new SettingsViewModel(), new ModelCapabilityCache());
            viewModel.AudioPath = audio;

            Task run = viewModel.TranscribeCommand.ExecuteAsync(null);
            await service.PhaseReported.Task;

            Assert.Contains("Diarizing", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);

            service.Release.TrySetResult();
            await run;

            Assert.Equal("Transcription complete.", viewModel.StatusMessage);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
            File.Delete(audio);
            ModelStore.CacheRootOverride = previous;
        }
    }

    /// <summary>Runs the callback inline, so Progress<T> updates are ordered in tests.</summary>
    private sealed class ImmediateSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object? state) => callback(state);
    }

    /// <summary>Reports the second pass, waits, then returns a result.</summary>
    private sealed class TwoPassService : ITranscriptionService
    {
        public TaskCompletionSource PhaseReported { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<TranscriptionResult> TranscribeAsync(
            string audioPath, string modelPath, TranscriptionOptions options,
            IProgress<double>? progress = null,
            IProgress<string>? liveText = null,
            IProgress<TranscriptionPhase>? phase = null,
            IProgress<TranscriptionStep>? step = null,
            CancellationToken cancellationToken = default)
        {
            progress?.Report(1.0);
            phase?.Report(TranscriptionPhase.Diarizing);
            PhaseReported.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return new TranscriptionResult { FullText = "text" };
        }

        public async IAsyncEnumerable<StreamUpdate> StreamTranscribeAsync(
            string modelPath, StreamOptions options,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task<MemoryProbeResult> ProbeMemoryAsync(
            string audioPath, string modelPath, TranscriptionOptions options,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<List<BatchItemResult>> BatchTranscribeAsync(
            IReadOnlyList<string> audioPaths, string modelPath, TranscriptionOptions options,
            IProgress<int>? progress = null, IProgress<BatchFileStep>? fileStep = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
