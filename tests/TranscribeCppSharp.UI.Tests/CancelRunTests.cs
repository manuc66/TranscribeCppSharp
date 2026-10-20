using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using TranscribeCppSharp.Models;
using TranscribeCppSharp.UI.Models;
using TranscribeCppSharp.UI.Services;
using TranscribeCppSharp.UI.ViewModels;
using TranscribeCppSharp.UI.Views;
using Xunit;

namespace TranscribeCppSharp.UI.Tests;

/// <summary>
/// Cancelling a run from the Transcription and Batch tabs: the token reaches the
/// service, the status says cancelled rather than failed, and the tab returns to
/// idle.
/// </summary>
[Collection("UiGlobalState")]
public class CancelRunTests
{
    private static async Task WithTranscription(
        ITranscriptionService service,
        Func<TranscriptionViewModel, Task> test)
    {
        string? previous = ModelStore.CacheRootOverride;
        ModelStore.CacheRootOverride = Path.Combine(
            Path.GetTempPath(), "tcsharp-cancel-" + Guid.NewGuid().ToString("N"));
        using var settings = new UiSettingsScope();
        try
        {
            var viewModel = new TranscriptionViewModel(
                service, new SettingsViewModel(), new ModelCapabilityCache());
            await test(viewModel);
        }
        finally
        {
            ModelStore.CacheRootOverride = previous;
        }
    }

    private static async Task WithBatch(
        ITranscriptionService service,
        Func<BatchViewModel, Task> test)
    {
        string? previous = ModelStore.CacheRootOverride;
        ModelStore.CacheRootOverride = Path.Combine(
            Path.GetTempPath(), "tcsharp-cancel-batch-" + Guid.NewGuid().ToString("N"));
        using var settings = new UiSettingsScope();
        try
        {
            var viewModel = new BatchViewModel(service, new SettingsViewModel());
            await test(viewModel);
        }
        finally
        {
            ModelStore.CacheRootOverride = previous;
        }
    }

    private static string TempWav()
    {
        string path = Path.Combine(
            Path.GetTempPath(), "tcsharp-cancel-" + Guid.NewGuid().ToString("N") + ".wav");
        File.WriteAllText(path, string.Empty);
        return path;
    }

    [Fact]
    public async Task CancellingATranscriptionStopsItAndSaysSo()
    {
        var service = new HangingTranscriptionService();
        string audio = TempWav();
        try
        {
            await WithTranscription(service, async viewModel =>
            {
                viewModel.AudioPath = audio;

                Task run = viewModel.TranscribeCommand.ExecuteAsync(null);
                await service.Started.Task;

                Assert.True(viewModel.IsTranscribing);
                Assert.True(viewModel.CanCancelTranscription);

                viewModel.CancelTranscriptionCommand.Execute(null);
                await run;

                Assert.False(viewModel.IsTranscribing);
                Assert.False(viewModel.CanCancelTranscription);
                Assert.Contains("cancel", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("error", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
                Assert.False(viewModel.HasResult);
            });
        }
        finally
        {
            File.Delete(audio);
        }
    }

    [Fact]
    public async Task CancellingABatchKeepsTheItemsThatFinished()
    {
        var service = new PartialBatchService();
        string audio = TempWav();
        try
        {
            await WithBatch(service, async viewModel =>
            {
                viewModel.AudioFiles.Add(audio);

                Task run = viewModel.ProcessBatchCommand.ExecuteAsync(null);
                await service.Started.Task;

                Assert.True(viewModel.IsProcessing);
                Assert.True(viewModel.CanCancelBatch);

                viewModel.CancelBatchCommand.Execute(null);
                await run;

                Assert.False(viewModel.IsProcessing);
                Assert.False(viewModel.CanCancelBatch);
                Assert.Contains("cancel", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
                Assert.Single(viewModel.Results);
                Assert.Equal("first", viewModel.Results[0].FullText);
            });
        }
        finally
        {
            File.Delete(audio);
        }
    }

    [AvaloniaFact]
    public async Task TheCancelButtonsAreBoundToTheirCommands()
    {
        var service = new HangingTranscriptionService();
        await WithTranscription(service, viewModel =>
        {
            var view = new TranscriptionView { DataContext = viewModel };
            var window = new Window { Content = view, Width = 1100, Height = 900 };
            window.Show();

            Button button = view.GetLogicalDescendants()
                .OfType<Button>()
                .First(b => AutomationProperties.GetAutomationId(b) == "transcription-cancel");

            Assert.Same(viewModel.CancelTranscriptionCommand, button.Command);
            Assert.False(button.IsEnabled); // nothing running yet
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task AnAlreadyCancelledBatchDoesNotEvenResolveTheModel()
    {
        // The token is checked before the model is loaded: stopping should not
        // spend a multi-gigabyte download first.
        var service = new TranscriptionService();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.BatchTranscribeAsync(
                ["a.wav"], "no-such-model.gguf", new TranscriptionOptions(),
                cancellationToken: cancellation.Token));
    }

    /// <summary>Starts a transcription and never finishes it on its own.</summary>
    private sealed class HangingTranscriptionService : ITranscriptionService
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<TranscriptionResult> TranscribeAsync(
            string audioPath, string modelPath, TranscriptionOptions options,
            IProgress<double>? progress = null, IProgress<string>? liveText = null, IProgress<TranscriptionPhase>? phase = null, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("unreachable");
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

    /// <summary>
    /// Behaves like the real batch service on cancel: the items finished before
    /// the token fired come back, and the caller's own token is what says the run
    /// was cancelled rather than completed.
    /// </summary>
    private sealed class PartialBatchService : ITranscriptionService
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<TranscriptionResult> TranscribeAsync(
            string audioPath, string modelPath, TranscriptionOptions options,
            IProgress<double>? progress = null, IProgress<string>? liveText = null, IProgress<TranscriptionPhase>? phase = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public async IAsyncEnumerable<StreamUpdate> StreamTranscribeAsync(
            string modelPath, StreamOptions options,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public async Task<List<BatchItemResult>> BatchTranscribeAsync(
            IReadOnlyList<string> audioPaths, string modelPath, TranscriptionOptions options,
            IProgress<int>? progress = null, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // The real service returns what finished before the token fired.
            }

            return [new BatchItemResult { AudioPath = audioPaths[0], FullText = "first", Status = "Ok" }];
        }
    }
}
