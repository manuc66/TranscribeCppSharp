using TranscribeCppSharp.Interop;
using TranscribeCppSharp.Models;
using TranscribeCppSharp.UI.Models;
using TranscribeCppSharp.UI.Services;
using TranscribeCppSharp.UI.ViewModels;
using Xunit;

using Task = System.Threading.Tasks.Task;
using StreamUpdate = TranscribeCppSharp.UI.Models.StreamUpdate;

namespace TranscribeCppSharp.UI.Tests;

/// <summary>
/// What the Transcription tab shows while a run is in flight, and what it leaves
/// behind when one fails.
/// </summary>
[Collection("UiGlobalState")]
public class TranscriptionFeedbackTests
{
    private static async Task WithViewModel(
        ITranscriptionService service,
        Func<TranscriptionViewModel, Task> test)
    {
        string? previous = ModelStore.CacheRootOverride;
        ModelStore.CacheRootOverride = Path.Combine(
            Path.GetTempPath(), "tcsharp-feedback-" + Guid.NewGuid().ToString("N"));
        using var settings = new UiSettingsScope();
        string audio = Path.Combine(
            Path.GetTempPath(), "tcsharp-feedback-" + Guid.NewGuid().ToString("N") + ".wav");
        File.WriteAllText(audio, string.Empty);

        // Progress<T> captures this context. Running the callback inline keeps the
        // test deterministic: in the app the same callbacks are posted to the UI
        // dispatcher, in order.
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
    public async Task TheLivePreviewShowsWhatTheFinishedWindowsProduced()
    {
        var service = new LiveTextService();
        await WithViewModel(service, async viewModel =>
        {
            Task run = viewModel.TranscribeCommand.ExecuteAsync(null);
            await service.PartialReported.Task;

            Assert.Equal("first window", viewModel.LiveText);
            Assert.True(viewModel.ShowLiveText);
            Assert.True(viewModel.IsTranscribing);

            service.Release.TrySetResult();
            await run;

            // The finished result takes over; the preview is not left beside it.
            Assert.Equal("first window and the second", viewModel.FullText);
            Assert.Equal(string.Empty, viewModel.LiveText);
            Assert.False(viewModel.ShowLiveText);
        });
    }

    [Fact]
    public async Task AnOutOfMemoryRunSaysSoAndKeepsADiagnosticToCopy()
    {
        var service = new FailingService(new TranscribeException(Status.ErrOom, "transcribe_run"));
        await WithViewModel(service, async viewModel =>
        {
            await viewModel.TranscribeCommand.ExecuteAsync(null);

            Assert.True(viewModel.CanCopyError);
            Assert.Contains("ErrOom", viewModel.LastError);
            Assert.Contains("transcribe_run", viewModel.LastError);
            Assert.Contains("Out of memory", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("CPU", viewModel.StatusMessage);
            Assert.Contains("logged to", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task ASuccessfulRunClearsThePreviousError()
    {
        var failing = new FailingService(new TranscribeException(Status.ErrOom, "transcribe_run"));
        await WithViewModel(failing, async viewModel =>
        {
            await viewModel.TranscribeCommand.ExecuteAsync(null);
            Assert.True(viewModel.CanCopyError);
        });

        var succeeding = new SucceedingService();
        await WithViewModel(succeeding, async viewModel =>
        {
            await viewModel.TranscribeCommand.ExecuteAsync(null);

            Assert.False(viewModel.CanCopyError);
            Assert.Null(viewModel.LastError);
            Assert.Equal("Transcription complete.", viewModel.StatusMessage);
        });
    }

    /// <summary>Reports one window's text, then waits to be released.</summary>
    private sealed class LiveTextService : ITranscriptionService
    {
        public TaskCompletionSource PartialReported { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<TranscriptionResult> TranscribeAsync(
            string audioPath, string modelPath, TranscriptionOptions options,
            IProgress<double>? progress = null, IProgress<string>? liveText = null,
            IProgress<TranscriptionPhase>? phase = null,
            CancellationToken cancellationToken = default)
        {
            liveText?.Report("first window");
            PartialReported.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return new TranscriptionResult { FullText = "first window and the second" };
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

    private sealed class FailingService(Exception failure) : ITranscriptionService
    {
        public Task<TranscriptionResult> TranscribeAsync(
            string audioPath, string modelPath, TranscriptionOptions options,
            IProgress<double>? progress = null, IProgress<string>? liveText = null,
            IProgress<TranscriptionPhase>? phase = null,
            CancellationToken cancellationToken = default)
            => Task.FromException<TranscriptionResult>(failure);

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

    private sealed class SucceedingService : ITranscriptionService
    {
        public Task<TranscriptionResult> TranscribeAsync(
            string audioPath, string modelPath, TranscriptionOptions options,
            IProgress<double>? progress = null, IProgress<string>? liveText = null,
            IProgress<TranscriptionPhase>? phase = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new TranscriptionResult { FullText = "done" });

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
