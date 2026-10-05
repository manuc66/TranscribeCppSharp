using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using TranscribeCppSharp.Models;
using TranscribeCppSharp.Ui.Models;
using TranscribeCppSharp.Ui.Services;
using TranscribeCppSharp.Ui.ViewModels;
using TranscribeCppSharp.Ui.Views;
using Xunit;

namespace TranscribeCppSharp.Ui.Tests;

public class TranscriptionDefaultsTests
{
    [AvaloniaFact]
    public void KvTypeShowsAutoByDefault()
    {
        // Keep the constructor's capability probe from loading a real model:
        // point the cache at an empty directory so nothing is on disk.
        string? previous = ModelStore.CacheRootOverride;
        ModelStore.CacheRootOverride = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "tcsharp-ui-tests-empty-cache");
        try
        {
            var viewModel = new TranscriptionViewModel(new FakeTranscriptionService(), new SettingsViewModel());
            var view = new TranscriptionView { DataContext = viewModel };
            var window = new Window { Content = view, Width = 1100, Height = 720 };
            window.Show();

            var combo = view.GetLogicalDescendants()
                .OfType<ComboBox>()
                .First(c => AutomationProperties.GetAutomationId(c) == "transcription-kv-type");

            Assert.Equal(3, combo.ItemCount);
            Assert.Equal(TranscribeCppSharp.Interop.KvType.KvTypeAuto, combo.SelectedItem);
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
            IProgress<double>? progress = null, CancellationToken cancellationToken = default)
            => throw new System.NotSupportedException();

        public async IAsyncEnumerable<TranscribeCppSharp.Ui.Models.StreamUpdate> StreamTranscribeAsync(
            string modelPath, TranscribeCppSharp.Ui.Models.StreamOptions options,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task<List<BatchItemResult>> BatchTranscribeAsync(
            IReadOnlyList<string> audioPaths, string modelPath, TranscriptionOptions options,
            IProgress<int>? progress = null, CancellationToken cancellationToken = default)
            => throw new System.NotSupportedException();
    }
}
