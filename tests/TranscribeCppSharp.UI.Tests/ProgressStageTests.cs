using TranscribeCppSharp.Models;
using TranscribeCppSharp.UI.Models;
using TranscribeCppSharp.UI.Services;
using TranscribeCppSharp.UI.ViewModels;
using Xunit;

namespace TranscribeCppSharp.UI.Tests;

/// <summary>
/// The countable step beside the progress bar: window x/y for one run, and
/// file x/y for a batch. The bar answers "how much"; these answer "of what".
/// </summary>
[Collection("UiGlobalState")]
public class ProgressStageTests
{
    [Fact]
    public async Task TheStageNamesTheWindowBeforeTheFirstOneFinishes()
    {
        var service = new StepService();
        string? previous = ModelStore.CacheRootOverride;
        ModelStore.CacheRootOverride = Path.Combine(
            Path.GetTempPath(), "tcsharp-stage-" + Guid.NewGuid().ToString("N"));
        using var settings = new UiSettingsScope();
        string audio = Path.Combine(
            Path.GetTempPath(), "tcsharp-stage-" + Guid.NewGuid().ToString("N") + ".wav");
        File.WriteAllText(audio, string.Empty);
        SynchronizationContext? previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new ImmediateSynchronizationContext());
        try
        {
            var viewModel = new TranscriptionViewModel(
                service, new SettingsViewModel(), new ModelCapabilityCache());
            viewModel.AudioPath = audio;

            Task run = viewModel.TranscribeCommand.ExecuteAsync(null);
            await service.FirstStep.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

            // Completed=0 is the announcement made before the first window runs,
            // so the reader is told window 1, not window 0.
            Assert.Equal("Transcribing — window 1 of 3", viewModel.ProgressStage);

            service.Release.TrySetResult();
            await run;

            Assert.Equal("Transcribing — window 3 of 3", viewModel.ProgressStage);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
            File.Delete(audio);
            ModelStore.CacheRootOverride = previous;
        }
    }

    [Fact]
    public async Task TheBatchNamesTheFileInFlightAndItsPosition()
    {
        var service = new StepService();
        string? previous = ModelStore.CacheRootOverride;
        ModelStore.CacheRootOverride = Path.Combine(
            Path.GetTempPath(), "tcsharp-batch-stage-" + Guid.NewGuid().ToString("N"));
        using var settings = new UiSettingsScope();
        SynchronizationContext? previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new ImmediateSynchronizationContext());
        try
        {
            var viewModel = new BatchViewModel(service, new SettingsViewModel());
            viewModel.AudioFiles.Add("/audio/one.wav");
            viewModel.AudioFiles.Add("/audio/two.wav");

            Task run = viewModel.ProcessBatchCommand.ExecuteAsync(null);
            await service.FirstStep.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

            Assert.Equal("File 1 of 2", viewModel.CurrentFileStep);
            Assert.Equal("one.wav", viewModel.CurrentFile);
            Assert.Contains("Reading", viewModel.CurrentFileStatus, StringComparison.Ordinal);

            service.Release.TrySetResult();
            await run;

            Assert.Equal("Done.", viewModel.CurrentFileStatus);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
            ModelStore.CacheRootOverride = previous;
        }
    }

    /// <summary>Runs the callback inline, so Progress&lt;T&gt; updates are ordered in tests.</summary>
    private sealed class ImmediateSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object? state) => callback(state);
    }

    /// <summary>Reports the first step of each flow, waits, then finishes.</summary>
    private sealed class StepService : ITranscriptionService
    {
        public TaskCompletionSource FirstStep { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<TranscriptionResult> TranscribeAsync(
            string audioPath,
            string modelPath,
            TranscriptionOptions options,
            IProgress<double>? progress = null,
            IProgress<string>? liveText = null,
            IProgress<TranscriptionPhase>? phase = null,
            IProgress<TranscriptionStep>? step = null,
            CancellationToken cancellationToken = default)
        {
            step?.Report(new TranscriptionStep(TranscriptionPhase.Transcribing, 0, 3));
            FirstStep.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);

            step?.Report(new TranscriptionStep(TranscriptionPhase.Transcribing, 3, 3));
            progress?.Report(1.0);
            return new TranscriptionResult { FullText = "text" };
        }

        public async Task<List<BatchItemResult>> BatchTranscribeAsync(
            IReadOnlyList<string> audioPaths,
            string modelPath,
            TranscriptionOptions options,
            IProgress<int>? progress = null,
            IProgress<BatchFileStep>? fileStep = null,
            CancellationToken cancellationToken = default)
        {
            string first = audioPaths[0];
            fileStep?.Report(new BatchFileStep(1, audioPaths.Count, first, BatchFilePhase.Started));
            FirstStep.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);

            fileStep?.Report(new BatchFileStep(1, audioPaths.Count, first, BatchFilePhase.Completed));
            progress?.Report(1);
            return [new BatchItemResult { AudioPath = first, FullText = "text", Status = "Ok" }];
        }

        public async IAsyncEnumerable<StreamUpdate> StreamTranscribeAsync(
            string modelPath,
            StreamOptions options,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task<MemoryProbeResult> ProbeMemoryAsync(
            string audioPath,
            string modelPath,
            TranscriptionOptions options,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
