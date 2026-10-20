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

public class TranscriptionDefaultsTests
{
    [AvaloniaFact]
    public void KvTypeShowsAutoByDefault()
    {
        // Keep the constructor's capability probe from loading a real model:
        // point the cache at an empty directory so nothing is on disk.
        string? previous = ModelStore.CacheRootOverride;
        ModelStore.CacheRootOverride = Path.Combine(
            Path.GetTempPath(), "tcsharp-ui-tests-empty-cache");
        try
        {
            var viewModel = new TranscriptionViewModel(
                new FakeTranscriptionService(), new SettingsViewModel(), new ModelCapabilityCache());
            var view = new TranscriptionView { DataContext = viewModel };
            var window = new Window { Content = view, Width = 1100, Height = 720 };
            window.Show();

            var combo = view.GetLogicalDescendants()
                .OfType<ComboBox>()
                .First(c => AutomationProperties.GetAutomationId(c) == "transcription-kv-type");

            Assert.Equal(3, combo.ItemCount);
            Assert.Equal(Interop.KvType.KvTypeAuto, combo.SelectedItem);
        }
        finally
        {
            ModelStore.CacheRootOverride = previous;
        }
    }

    private sealed class FakeTranscriptionService : ITranscriptionService
    {
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

        public Task<List<BatchItemResult>> BatchTranscribeAsync(
            IReadOnlyList<string> audioPaths, string modelPath, TranscriptionOptions options,
            IProgress<int>? progress = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
