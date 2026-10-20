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
            File.Delete(audio);
            ModelStore.CacheRootOverride = previous;
        }
    }

    /// <summary>Reports the second pass, waits, then returns a result.</summary>
    private sealed class TwoPassService : ITranscriptionService
    {
        public TaskCompletionSource PhaseReported { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<TranscriptionResult> TranscribeAsync(
            string audioPath, string modelPath, TranscriptionOptions options,
            IProgress<double>? progress = null,
            IProgress<TranscriptionPhase>? phase = null,
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

        public Task<List<BatchItemResult>> BatchTranscribeAsync(
            IReadOnlyList<string> audioPaths, string modelPath, TranscriptionOptions options,
            IProgress<int>? progress = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
