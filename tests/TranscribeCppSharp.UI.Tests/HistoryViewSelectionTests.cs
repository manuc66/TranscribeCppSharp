using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TranscribeCppSharp.Audio;
using TranscribeCppSharp.Models;
using TranscribeCppSharp.UI.Models;
using TranscribeCppSharp.UI.Services;
using TranscribeCppSharp.UI.ViewModels;
using TranscribeCppSharp.UI.Views;
using Xunit;

namespace TranscribeCppSharp.UI.Tests;

/// <summary>
/// The grid's multi-selection has to reach the view model: the bulk delete acts
/// on <see cref="HistoryViewModel.SelectedEntries"/>, and a selection the grid
/// keeps to itself would silently delete nothing.
/// </summary>
[Collection("UiGlobalState")]
public class HistoryViewSelectionTests
{
    private static TranscriptionResult Result(string text)
        => new()
        {
            FullText = text,
            DetectedLanguage = "en",
            Segments = [new MergedSegment(TimeSpan.Zero, TimeSpan.FromSeconds(1), text, 0)],
        };

    [AvaloniaFact]
    public void SelectingTwoRowsReachesTheViewModelAndAsksBeforeDeleting()
    {
        string? previousCache = ModelStore.CacheRootOverride;
        ModelStore.CacheRootOverride = Path.Combine(
            Path.GetTempPath(), "tcsharp-history-select-" + Guid.NewGuid().ToString("N"));

        using var history = new HistoryDirectoryScope();
        using var settings = new UiSettingsScope();
        try
        {
            for (int i = 0; i < 3; i++)
            {
                TranscriptionHistory.Save(
                    $"/audio/{i}.wav", "whisper-tiny", "en", "on", 300, Result($"text {i}"),
                    new DateTimeOffset(2026, 10, 20, 10, i, 0, TimeSpan.Zero));
            }

            var viewModel = new HistoryViewModel();
            var view = new HistoryView { DataContext = viewModel };
            var window = new Window { Content = view, Width = 1000, Height = 700 };
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();

                DataGrid grid = window.GetVisualDescendants().OfType<DataGrid>().First();
                Assert.NotNull(grid.SelectedItems);

                grid.SelectedItems.Add(viewModel.Entries[0]);
                grid.SelectedItems.Add(viewModel.Entries[1]);
                Dispatcher.UIThread.RunJobs();

                Assert.Equal(2, viewModel.SelectedEntries.Count);

                viewModel.DeleteSelectedCommand.Execute(null);

                // Two rows is a bulk delete: it must ask, and nothing is gone yet.
                Assert.True(viewModel.IsConfirmingDelete);
                Assert.Equal(3, viewModel.Entries.Count);
            }
            finally
            {
                window.Close();
            }
        }
        finally
        {
            ModelStore.CacheRootOverride = previousCache;
        }
    }
}
