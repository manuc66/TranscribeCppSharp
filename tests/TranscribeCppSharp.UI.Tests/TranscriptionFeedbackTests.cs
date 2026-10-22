using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using TranscribeCppSharp.Interop;
using TranscribeCppSharp.Models;
using TranscribeCppSharp.UI.Models;
using TranscribeCppSharp.UI.Services;
using TranscribeCppSharp.UI.ViewModels;
using TranscribeCppSharp.UI.Views;
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
        var oom = new TranscribeException(Status.ErrOom, "transcribe_run");
        oom.Data["memory"] = new MemoryObservation(
            "Quadro P2000", 4_294_967_296, 300_000_000,
            new DateTimeOffset(2026, 10, 21, 8, 5, 0, TimeSpan.Zero));
        oom.Data["session limits"] = new Session.SessionLimitsInfo(32768, 0, 1_000_000_000);
        oom.Data["window"] = "7 of 120";
        var service = new FailingService(oom);
        await WithViewModel(service, async viewModel =>
        {
            viewModel.ModelAlias = "whisper-tiny";
            // The report must name the window the run used, whatever it is.
            viewModel.WindowSeconds = 300;
            await viewModel.TranscribeCommand.ExecuteAsync(null);

            Assert.True(viewModel.CanCopyError);
            Assert.Contains("ErrOom", viewModel.LastError);
            Assert.Contains("transcribe_run", viewModel.LastError);
            Assert.Contains("model: whisper-tiny", viewModel.LastError);
            Assert.Contains("window: 300 s", viewModel.LastError);
            Assert.Contains("target device:", viewModel.LastError);
            Assert.Contains("device at failure: Quadro P2000", viewModel.LastError);
            Assert.Contains("session limits: n_ctx 32768", viewModel.LastError);
            Assert.Contains("failed at window: 7 of 120", viewModel.LastError);
            Assert.Contains("Out of memory", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("whisper-tiny", viewModel.StatusMessage);
            Assert.Contains("Window", viewModel.StatusMessage);
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

    [Fact]
    public async Task TheErrorReportDescribesTheRunThatStartedNotThePickersNow()
    {
        // A run works from the values at its start. Editing during it must not
        // rewrite what the report says happened.
        var service = new BlockingFailureService(new TranscribeException(Status.ErrOom, "Run"));
        await WithViewModel(service, async viewModel =>
        {
            viewModel.ModelAlias = "parakeet-tdt-0.6b-v3";
            viewModel.WindowSeconds = 5;

            Task run = viewModel.TranscribeCommand.ExecuteAsync(null);
            await service.Started.Task;

            viewModel.ModelAlias = "whisper-tiny";
            viewModel.WindowSeconds = 300;
            viewModel.ContextSize = 32768;

            service.Release.TrySetResult();
            await run;

            Assert.Contains("model: parakeet-tdt-0.6b-v3", viewModel.LastError);
            Assert.Contains("window: 5 s", viewModel.LastError);
            Assert.Contains("context size: model default", viewModel.LastError);
            Assert.DoesNotContain("window: 300 s", viewModel.LastError);
        });
    }

    [Fact]
    public async Task TheSettingsAreLockedWhileARunOrAProbeIsActive()
    {
        await WithViewModel(new SucceedingService(), viewModel =>
        {
            Assert.True(viewModel.CanEditSettings);

            viewModel.IsTranscribing = true;
            Assert.False(viewModel.CanEditSettings);

            viewModel.IsTranscribing = false;
            viewModel.IsProbingMemory = true;
            Assert.False(viewModel.CanEditSettings);

            viewModel.IsProbingMemory = false;
            Assert.True(viewModel.CanEditSettings);
            return Task.CompletedTask;
        });
    }

    [AvaloniaFact]
    public async Task TheSettingsControlsAreDisabledWhileARunIsActive()
    {
        await WithViewModel(new SucceedingService(), viewModel =>
        {
            var view = new TranscriptionView { DataContext = viewModel };
            var window = new Window { Content = view, Width = 1100, Height = 900 };
            window.Show();

            NumericUpDown windowSeconds = view.GetLogicalDescendants()
                .OfType<NumericUpDown>()
                .First(c => AutomationProperties.GetAutomationId(c) == "transcription-window-seconds");

            Assert.True(windowSeconds.IsEffectivelyEnabled);

            viewModel.IsTranscribing = true;
            Assert.False(windowSeconds.IsEffectivelyEnabled);

            viewModel.IsTranscribing = false;
            Assert.True(windowSeconds.IsEffectivelyEnabled);
            return Task.CompletedTask;
        });
    }

    /// <summary>Signals that the run started, waits, then throws the given failure.</summary>
    private sealed class BlockingFailureService(Exception failure) : ITranscriptionService
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<TranscriptionResult> TranscribeAsync(
            string audioPath, string modelPath, TranscriptionOptions options,
            IProgress<double>? progress = null, IProgress<string>? liveText = null,
            IProgress<TranscriptionPhase>? phase = null,
            IProgress<TranscriptionStep>? step = null,
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            throw failure;
        }

        public Task<MemoryProbeResult> ProbeMemoryAsync(
            string audioPath, string modelPath, TranscriptionOptions options,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public async IAsyncEnumerable<StreamUpdate> StreamTranscribeAsync(
            string modelPath, StreamOptions options,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task<List<BatchItemResult>> BatchTranscribeAsync(
            IReadOnlyList<string> audioPaths, string modelPath, TranscriptionOptions options,
            IProgress<int>? progress = null, IProgress<BatchFileStep>? fileStep = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
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
            IProgress<TranscriptionStep>? step = null,
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

        public Task<MemoryProbeResult> ProbeMemoryAsync(
            string audioPath, string modelPath, TranscriptionOptions options,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<List<BatchItemResult>> BatchTranscribeAsync(
            IReadOnlyList<string> audioPaths, string modelPath, TranscriptionOptions options,
            IProgress<int>? progress = null, IProgress<BatchFileStep>? fileStep = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class FailingService(Exception failure) : ITranscriptionService
    {
        public Task<TranscriptionResult> TranscribeAsync(
            string audioPath, string modelPath, TranscriptionOptions options,
            IProgress<double>? progress = null, IProgress<string>? liveText = null,
            IProgress<TranscriptionPhase>? phase = null,
            IProgress<TranscriptionStep>? step = null,
            CancellationToken cancellationToken = default)
            => Task.FromException<TranscriptionResult>(failure);

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

    private sealed class SucceedingService : ITranscriptionService
    {
        public Task<TranscriptionResult> TranscribeAsync(
            string audioPath, string modelPath, TranscriptionOptions options,
            IProgress<double>? progress = null, IProgress<string>? liveText = null,
            IProgress<TranscriptionPhase>? phase = null,
            IProgress<TranscriptionStep>? step = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new TranscriptionResult { FullText = "done" });

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
