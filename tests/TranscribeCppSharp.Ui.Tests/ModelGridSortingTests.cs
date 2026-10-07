using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TranscribeCppSharp.Models;
using TranscribeCppSharp.Ui.ViewModels;
using TranscribeCppSharp.Ui.Views;
using Xunit;

namespace TranscribeCppSharp.Ui.Tests;

/// <summary>
/// A column header click orders the grid, and says so.
/// </summary>
/// <remarks>
/// The DataGrid's own sorting is cancelled: it writes sort descriptions onto the
/// bound collection, and that collection is cleared and refilled on every filter
/// change, so the order would be lost on the next filter. The view model orders
/// instead, through the same properties the sort buttons use — which is also why
/// direction is read back from it rather than from the column: Avalonia's column
/// exposes no public sort direction, so the arrow lives in the header text.
/// </remarks>
public class ModelGridSortingTests
{
    /// <summary>The columns with an order behind them, and the two without.</summary>
    private static readonly string[] SortableColumns =
        ["Alias", "Family", "Size", "On disk", "Licence", "Languages", "x realtime", "Diarization"];

    private static readonly string[] RefusedColumns = ["WER", "Action"];

    private static (Window Window, ModelManagerViewModel ViewModel, DataGrid Grid) Show()
    {
        ModelStore.CacheRootOverride = Path.Combine(
            Path.GetTempPath(), "tcsharp-ui-tests-grid-sort");

        var root = new MainWindowViewModel(
            new TranscriptionViewModel(new StubTranscriptionService(), new SettingsViewModel()),
            new StreamingViewModel(new StubTranscriptionService(), new SettingsViewModel()),
            new BatchViewModel(new StubTranscriptionService(), new SettingsViewModel()),
            new ModelManagerViewModel(),
            new SettingsViewModel());

        var window = new MainWindow { DataContext = root };
        window.Show();
        root.SelectedTabIndex = MainWindowViewModel.ModelTabIndex;
        Settle(window);

        DataGrid grid = window.GetVisualDescendants().OfType<DataGrid>().First();
        return (window, root.ModelManager, grid);
    }

    private static void Settle(Window window)
    {
        for (int i = 0; i < 5; i++)
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
        }
    }

    /// <summary>
    /// The header a reader clicks, located by its caption.
    /// </summary>
    /// <remarks>
    /// By content, not by DataContext: a DataGridColumnHeader's DataContext here is
    /// the view model, not the column — a diagnostic dump of the header-shaped
    /// controls is what established that, and it is also where the arrow showed up,
    /// as "Alias ↑". Two captions exist because some headers are plain strings and
    /// some are TextBlocks carrying a tooltip, and Header.ToString() on the second
    /// kind is the type name.
    /// </remarks>
    private static ContentControl Header(DataGrid grid, string prefix)
        => grid.GetVisualDescendants()
            .OfType<ContentControl>()
            .First(c => Caption(c)?.StartsWith(prefix, StringComparison.Ordinal) == true);

    private static string? Caption(ContentControl header)
        => header.Content switch
        {
            string text => text,
            TextBlock block => block.Text,
            _ => header.Content?.ToString(),
        };

    private static string CaptionOf(DataGrid grid, string prefix)
        => Caption(Header(grid, prefix))!;

    /// <summary>
    /// Every data column either has an order behind it or says why it does not.
    /// </summary>
    /// <remarks>
    /// The count is the point: a grid where most columns decline a click is a grid
    /// where sorting is a feature nobody can find. Action is not data — it holds
    /// buttons — and WER is the one data column without an order, because its 72
    /// figures come from three metrics, two datasets and eight languages.
    /// </remarks>
    [AvaloniaFact]
    public void AlmostEveryDataColumnCanBeOrdered()
    {
        var (_, _, grid) = Show();

        List<string> sortable = grid.Columns
            .Where(c => c.CanUserSort && c.Tag is not null)
            .Select(c => c.Header switch { string t => t, TextBlock b => b.Text ?? "", _ => "" })
            .Select(h => h.Replace("\u2191", string.Empty).Replace("\u2193", string.Empty).Trim())
            .Where(h => h.Length > 0)
            .ToList();

        Assert.Equal(SortableColumns, sortable);

        // The two that decline, and only those two.
        List<string> refused = grid.Columns
            .Where(c => !c.CanUserSort || c.Tag is null)
            .Select(c => c.Header switch { string t => t, TextBlock b => b.Text ?? "", _ => "" })
            .Where(h => h.Length > 0)
            .ToList();
        Assert.Equal(RefusedColumns, refused);
    }

    [AvaloniaFact]
    public void ClickingTheLicenceHeaderOrdersByLicence()
    {
        var (window, viewModel, grid) = Show();

        Click(window, Header(grid, "Licence"));

        Assert.Equal(ModelManagerViewModel.ModelSort.Licence, viewModel.Sort);
        List<string> licences = viewModel.VisibleModels.Select(m => m.License).ToList();
        Assert.Equal(licences.OrderBy(l => l, StringComparer.Ordinal).ToList(), licences);
        Assert.Contains("\u2191", CaptionOf(grid, "Licence"), StringComparison.Ordinal);
    }

    /// <summary>The column that has no comparable order refuses, and says why.</summary>
    [AvaloniaFact]
    public void ClickingTheWerHeaderDoesNothing()
    {
        var (window, viewModel, grid) = Show();

        Click(window, Header(grid, "WER"));

        // Still the default order: no order was invented for numbers that do not
        // measure the same thing.
        Assert.Equal(ModelManagerViewModel.ModelSort.Alias, viewModel.Sort);
        Assert.True(viewModel.SortAscending);

        // The refusal is explained in the tooltip rather than shouted in the
        // caption: the column holds a number, and the reader needs to know before
        // they trust an order that the numbers do not measure the same thing.
        // The tooltip sits on the TextBlock inside the header, so that is the
        // control to ask — the header itself carries none.
        Control header = Header(grid, "WER").Content is TextBlock caption
            ? caption
            : Header(grid, "WER");
        string? tip = ToolTip.GetTip(header)?.ToString();

        Assert.NotNull(tip);
        Assert.Contains("not sortable", tip!, StringComparison.Ordinal);
        Assert.Contains("65 WER, 6 CER, 1 DER", tip!, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void TheDefaultOrderIsAscendingAndTheHeaderSaysSo()
    {
        var (_, viewModel, grid) = Show();

        Assert.Equal(ModelManagerViewModel.ModelSort.Alias, viewModel.Sort);
        Assert.True(viewModel.SortAscending);
        Assert.Contains("\u2191", CaptionOf(grid, "Alias"), StringComparison.Ordinal);

        List<string> aliases = viewModel.VisibleModels.Select(m => m.Alias).ToList();
        Assert.Equal(aliases.OrderBy(a => a, StringComparer.Ordinal).ToList(), aliases);
    }

    [AvaloniaFact]
    public void ClickingTheSizeHeaderOrdersSmallestFirst()
    {
        var (window, viewModel, grid) = Show();

        Click(window, Header(grid, "Size"));

        Assert.Equal(ModelManagerViewModel.ModelSort.Size, viewModel.Sort);
        Assert.True(viewModel.SortAscending);
        Assert.Contains("\u2191", CaptionOf(grid, "Size"), StringComparison.Ordinal);

        List<long> sizes = viewModel.VisibleModels.Select(m => m.Descriptor.Size).ToList();
        Assert.Equal(sizes.OrderBy(s => s).ToList(), sizes);
    }

    [AvaloniaFact]
    public void ASecondClickOnTheSameHeaderReverses()
    {
        var (window, viewModel, grid) = Show();

        Click(window, Header(grid, "Size"));
        List<long> ascending = viewModel.VisibleModels.Select(m => m.Descriptor.Size).ToList();

        Click(window, Header(grid, "Size"));

        Assert.False(viewModel.SortAscending);
        Assert.Contains("\u2193", CaptionOf(grid, "Size"), StringComparison.Ordinal);

        List<long> descending = viewModel.VisibleModels.Select(m => m.Descriptor.Size).ToList();
        Assert.Equal(ascending.AsEnumerable().Reverse(), descending);
    }

    /// <summary>
    /// Only one arrow, and it follows whichever control set the order.
    /// </summary>
    [AvaloniaFact]
    public void TheSortButtonAndTheHeaderAgreeOnTheArrow()
    {
        var (window, viewModel, grid) = Show();

        // Header route: ordering by size takes the arrow off the alias column.
        Click(window, Header(grid, "Size"));
        Assert.DoesNotContain("\u2191", CaptionOf(grid, "Alias"), StringComparison.Ordinal);

        // Button route: the same state reached from the other control, so the
        // arrows move too. Two controls keeping their own would leave the grid
        // sorted one way and pointing another.
        viewModel.SortBySpeedCommand.Execute(null);

        Assert.Equal(ModelManagerViewModel.ModelSort.Speed, viewModel.Sort);
        Assert.DoesNotContain("\u2191", CaptionOf(grid, "Alias"), StringComparison.Ordinal);
        Assert.Contains("\u2191", CaptionOf(grid, "x realtime"), StringComparison.Ordinal);
    }

    /// <summary>Press and release on a control's centre, as a reader would.</summary>
    private static void Click(Window window, Control control)
    {
        Settle(window);

        Point at = control.TranslatePoint(
                new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)
            ?? throw new InvalidOperationException(
                "the header has no position in the window, so it cannot be clicked");

        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Settle(window);
    }
}