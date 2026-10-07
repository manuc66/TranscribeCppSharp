using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TranscribeCppSharp.Models;
using TranscribeCppSharp.Ui.ViewModels;
using TranscribeCppSharp.Ui.Views;
using Xunit;

namespace TranscribeCppSharp.Ui.Tests;

/// <summary>
/// The model grid scrolls inside itself, so its header and its detail pane stay put.
/// </summary>
/// <remarks>
/// The view lives inside the window's tab <c>ScrollViewer</c>, which measures its
/// child against infinite height. A <c>*</c> row given infinite height resolves to
/// its content, so the grid grows to seventy-odd rows, nothing scrolls internally,
/// and scrolling the page takes the column headers and the detail pane off the
/// screen with it. Both are things a reader needs while looking at a row.
/// </remarks>
public class ModelGridLayoutTests
{
    private static (Window Window, DataGrid Grid, Control Detail) Show()
    {
        string previous = ModelStore.CacheRootOverride ?? string.Empty;
        bool hadOverride = ModelStore.CacheRootOverride is not null;
        ModelStore.CacheRootOverride = Path.Combine(
            Path.GetTempPath(), "tcsharp-ui-tests-grid-layout");

        // The real window, not the view on its own: the tab's ScrollViewer is what
        // measures the view against infinite height, and without it the layout under
        // test is a different layout — which is what the first attempt at this test
        // did, and it passed while proving nothing.
        var window = new MainWindow
        {
            DataContext = new MainWindowViewModel(
                new TranscriptionViewModel(new StubTranscriptionService(), new SettingsViewModel()),
                new StreamingViewModel(new StubTranscriptionService(), new SettingsViewModel()),
                new BatchViewModel(new StubTranscriptionService(), new SettingsViewModel()),
                new ModelManagerViewModel(),
                new SettingsViewModel()),
        };
        window.Show();

        // The TabControl materializes only the selected tab, so the Models view —
        // and its grid — do not exist in the visual tree until we ask for them.
        var viewModel = (MainWindowViewModel)window.DataContext!;
        viewModel.SelectedTabIndex = MainWindowViewModel.ModelTabIndex;

        for (int i = 0; i < 5; i++)
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
        }

        // The pane is collapsed while no row is selected, so measuring it then
        // measures a zero-height control and passes whatever the layout does.
        viewModel.ModelManager.SelectedModel = viewModel.ModelManager.VisibleModels.First();
        for (int i = 0; i < 3; i++)
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
        }

        DataGrid grid = window.GetVisualDescendants().OfType<DataGrid>().First();
        Control detail = window.GetVisualDescendants()
            .OfType<Control>()
            .First(c => AutomationProperties.GetAutomationId(c) == "models-detail");
        Assert.True(detail.Bounds.Height > 0, "the detail pane never expanded");

        return (window, grid, detail);
    }

    /// <summary>
    /// The grid has to be shorter than its content, or it is the page that scrolls.
    /// </summary>
    [AvaloniaFact]
    public void TheGridScrollsWithinTheWindowNotWithIt()
    {
        var (window, grid, _) = Show();

        Assert.True(grid.Bounds.Height > 0, "the grid did not lay out");

        // Taller than the window means the window is what moves, and the header
        // goes with it.
        Assert.False(grid.Bounds.Height > window.Bounds.Height,
            $"the grid is {grid.Bounds.Height:F0}px tall in a {window.Bounds.Height:F0}px window, " +
            "so its header scrolls away with the page");

        // And a floor, because "not taller than the window" is satisfied by a grid
        // one row high. The detail pane shares this space, so a regression that
        // gives it everything shows up here rather than as a blank master list.
        Assert.True(grid.Bounds.Height >= 120,
            $"the grid is only {grid.Bounds.Height:F0}px tall with a row selected — " +
            "too short to choose another row from");
    }

    /// <summary>
    /// The detail pane is visible without scrolling to the bottom of the view.
    /// </summary>
    [AvaloniaFact]
    public void TheDetailPaneIsWithinTheWindow()
    {
        var (window, _, detail) = Show();

        Point? bottom = detail.TranslatePoint(
            new Point(0, detail.Bounds.Height), window);
        Assert.NotNull(bottom);

        Assert.True(bottom.Value.Y <= window.Bounds.Height,
            $"the detail pane ends at y={bottom.Value.Y:F0} in a {window.Bounds.Height:F0}px window, " +
            "so it is below the fold and cannot be read while using the grid");
    }
}