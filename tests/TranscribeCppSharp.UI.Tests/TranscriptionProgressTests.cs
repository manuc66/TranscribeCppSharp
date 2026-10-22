using TranscribeCppSharp.Models;
using TranscribeCppSharp.UI.Models;
using TranscribeCppSharp.UI.Services;
using TranscribeCppSharp.UI.ViewModels;
using Xunit;

namespace TranscribeCppSharp.UI.Tests;

/// <summary>
/// The progress card tells the truth about what it measures: a fraction when the
/// service reports one, an indeterminate bar and a reason when it does not, and
/// never a full bar while a second pass is still running.
/// </summary>
[Collection("UiGlobalState")]
public class TranscriptionProgressTests
{
    private static async Task WithRun(
        ITranscriptionService service,
        Func<TranscriptionViewModel, Task> test)
    {
        string? previous = ModelStore.CacheRootOverride;
        ModelStore.CacheRootOverride = Path.Combine(
            Path.GetTempPath(), "tcsharp-progress-" + Guid.NewGuid().ToString("N"));
        using var settings = new UiSettingsScope();
        string audio = Path.Combine(
            Path.GetTempPath(), "tcsharp-progress-" + Guid.NewGuid().ToString("N") + ".wav");
        File.WriteAllText(audio, string.Empty);
        SynchronizationContext? previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new ImmediateSynchronizationContext());
        try
        {
            var viewModel = new TranscriptionViewModel(
                service, new SettingsViewModel(), new ModelCapabilityCache());
            viewModel.AudioPath = audio;
            await test(viewModel);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
            File.Delete(audio);
            ModelStore.CacheRootOverride = previous;
        }
    }

    private sealed class ImmediateSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object? state) => callback(state);
    }

    [Fact]
    public async Task LoadingAndReadingAreIndeterminateAndSayWhy()
    {
        var service = new StagedService();
        await WithRun(service, async viewModel =>
        {
            Task run = viewModel.TranscribeCommand.ExecuteAsync(null);
            await service.Preparing.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

            Assert.True(viewModel.ProgressIsIndeterminate);
            Assert.False(viewModel.ShowProgressPercent);
            // The progress block speaks while the run is in flight; the status
            // line is for the completion or the failure.
            Assert.False(viewModel.ShowStatus);
            Assert.Contains("Loading", viewModel.ProgressStage, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("no progress", viewModel.ProgressNote, StringComparison.OrdinalIgnoreCase);

            service.CouldRead.TrySetResult();
            await service.Reading.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

            Assert.True(viewModel.ProgressIsIndeterminate);
            Assert.Contains("audio", viewModel.ProgressStage, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("no progress", viewModel.ProgressNote, StringComparison.OrdinalIgnoreCase);

            service.CouldTranscribe.TrySetResult();
            await service.FirstStep.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

            Assert.False(viewModel.ProgressIsIndeterminate);
            Assert.True(viewModel.ShowProgressPercent);
            Assert.Equal("Transcribing — window 1 of 3", viewModel.ProgressStage);
            Assert.Equal(string.Empty, viewModel.ProgressNote);

            service.CouldDiarize.TrySetResult();
            service.Release.TrySetResult();
            await run;

            // The elapsed line describes the run, not the tab: it clears when the
            // run ends instead of showing a frozen number.
            Assert.Equal(string.Empty, viewModel.ElapsedText);
            Assert.Equal(string.Empty, viewModel.RemainingText);
            Assert.True(viewModel.ShowStatus);
        });
    }

    [Fact]
    public async Task ADiarizingPassIsIndeterminateRatherThanHoldingAtAStalePercentage()
    {
        var service = new StagedService();
        await WithRun(service, async viewModel =>
        {
            Task run = viewModel.TranscribeCommand.ExecuteAsync(null);
            await service.Preparing.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            service.CouldRead.TrySetResult();
            await service.Reading.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            service.CouldTranscribe.TrySetResult();
            await service.FirstStep.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

            service.CouldDiarize.TrySetResult();
            await service.Diarizing.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

            Assert.True(viewModel.ProgressIsIndeterminate);
            Assert.False(viewModel.ShowProgressPercent);
            Assert.Contains("Diarizing", viewModel.ProgressStage, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("one native call", viewModel.ProgressNote, StringComparison.OrdinalIgnoreCase);

            service.Release.TrySetResult();
            await run;
        });
    }

    [Fact]
    public async Task TheEstimateStartsOnlyAfterASecondMeasuredWindow()
    {
        var service = new StagedService();
        await WithRun(service, async viewModel =>
        {
            Task run = viewModel.TranscribeCommand.ExecuteAsync(null);
            await service.Preparing.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            service.CouldRead.TrySetResult();
            await service.Reading.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            service.CouldTranscribe.TrySetResult();

            await service.FirstStep.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.Equal(string.Empty, viewModel.RemainingText);
            Assert.NotEqual(string.Empty, viewModel.ElapsedText);

            service.ReportWindow(1);
            Assert.Equal(string.Empty, viewModel.RemainingText);

            service.ReportWindow(2);
            Assert.Contains("left (estimate)", viewModel.RemainingText, StringComparison.Ordinal);

            service.CouldDiarize.TrySetResult();
            service.Release.TrySetResult();
            await run;
        });
    }

    [Fact]
    public async Task AFileThatFitsInOneWindowIsIndeterminateRatherThanFrozenAtZero()
    {
        var service = new SingleWindowService();
        await WithRun(service, async viewModel =>
        {
            Task run = viewModel.TranscribeCommand.ExecuteAsync(null);
            await service.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

            Assert.True(viewModel.ProgressIsIndeterminate);
            Assert.False(viewModel.ShowProgressPercent);
            Assert.Equal("Transcribing — window 1 of 1", viewModel.ProgressStage);
            Assert.Contains("one window", viewModel.ProgressNote, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("no progress", viewModel.ProgressNote, StringComparison.OrdinalIgnoreCase);

            service.Release.TrySetResult();
            await run;
        });
    }

    /// <summary>Reports a one-window plan, then finishes.</summary>
    private sealed class SingleWindowService : ITranscriptionService
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<TranscriptionResult> TranscribeAsync(
            string audioPath, string modelPath, TranscriptionOptions options,
            IProgress<double>? progress = null,
            IProgress<string>? liveText = null,
            IProgress<TranscriptionPhase>? phase = null,
            IProgress<TranscriptionStep>? step = null,
            CancellationToken cancellationToken = default)
        {
            phase?.Report(TranscriptionPhase.Transcribing);
            step?.Report(new TranscriptionStep(TranscriptionPhase.Transcribing, 0, 1));
            Started.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            progress?.Report(1.0);
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
            IProgress<int>? progress = null, IProgress<BatchFileStep>? fileStep = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Walks the run through its phases on demand, so the assertions can look at
    /// each state without timing a real model.
    /// </summary>
    private sealed class StagedService : ITranscriptionService
    {
        public TaskCompletionSource Preparing { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource CouldRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Reading { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource CouldTranscribe { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource FirstStep { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource CouldDiarize { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Diarizing { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private IProgress<TranscriptionStep>? _step;

        public void ReportWindow(int completed)
            => _step?.Report(new TranscriptionStep(TranscriptionPhase.Transcribing, completed, 3));

        public async Task<TranscriptionResult> TranscribeAsync(
            string audioPath, string modelPath, TranscriptionOptions options,
            IProgress<double>? progress = null,
            IProgress<string>? liveText = null,
            IProgress<TranscriptionPhase>? phase = null,
            IProgress<TranscriptionStep>? step = null,
            CancellationToken cancellationToken = default)
        {
            _step = step;

            phase?.Report(TranscriptionPhase.Preparing);
            Preparing.TrySetResult();
            await CouldRead.Task.WaitAsync(cancellationToken);

            phase?.Report(TranscriptionPhase.ReadingAudio);
            Reading.TrySetResult();
            await CouldTranscribe.Task.WaitAsync(cancellationToken);

            phase?.Report(TranscriptionPhase.Transcribing);
            step?.Report(new TranscriptionStep(TranscriptionPhase.Transcribing, 0, 3));
            FirstStep.TrySetResult();

            await CouldDiarize.Task.WaitAsync(cancellationToken);
            phase?.Report(TranscriptionPhase.Diarizing);
            step?.Report(new TranscriptionStep(TranscriptionPhase.Diarizing, 0, 1));
            Diarizing.TrySetResult();

            await Release.Task.WaitAsync(cancellationToken);
            progress?.Report(1.0);
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
            IProgress<int>? progress = null, IProgress<BatchFileStep>? fileStep = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
