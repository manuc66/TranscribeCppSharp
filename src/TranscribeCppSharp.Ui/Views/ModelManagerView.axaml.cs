using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using TranscribeCppSharp.Ui.ViewModels;

namespace TranscribeCppSharp.Ui.Views;

/// <summary>
/// Sorts the grid by rebuilding its collection, and keeps the header arrows in
/// step with the sort buttons.
/// </summary>
/// <remarks>
/// The DataGrid's own sort is cancelled in <see cref="OnSorting"/>: it writes sort
/// descriptions onto the bound collection, and that collection is cleared and
/// refilled on every filter change, so the order would vanish the next time a
/// filter was set. The view model orders the list instead, through the same two
/// properties the sort buttons already use — one source of order, whichever
/// control asked for it.
/// <para>
/// The arrows are read back from the view model rather than left where the
/// DataGrid put them, so clicking "By size" and clicking the Size header show the
/// same arrow. Two controls tracking their own state is how a grid ends up
/// sorted one way and pointing another.
/// </para>
/// </remarks>
public partial class ModelManagerView : UserControl
{
    private ModelManagerViewModel? _viewModel;

    /// <summary>
    /// Header text before any arrow was added, keyed by column.
    /// </summary>
    /// <remarks>
    /// Kept because Avalonia's DataGridColumn exposes no public sort direction —
    /// only CanUserSort, SortMemberPath and CustomSortComparer — so the arrow is
    /// carried by the header text itself. Reading it back from a remembered base
    /// is what stops "Size ↑" from becoming "Size ↑ ↓" the second time round.
    /// </remarks>
    private readonly Dictionary<DataGridColumn, string> _baseHeaders = new();

    public ModelManagerView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void Attach()
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = DataContext as ModelManagerViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            SyncSortGlyphs();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ModelManagerViewModel.Sort)
            or nameof(ModelManagerViewModel.SortAscending))
        {
            SyncSortGlyphs();
        }
    }

    /// <summary>
    /// A header was clicked: take the sort, or reverse it if it is already set.
    /// </summary>
    private void OnSorting(object? sender, DataGridColumnEventArgs e)
    {
        // Cancel whatever the DataGrid would do on its own — see the remarks above.
        e.Handled = true;

        ModelManagerViewModel? viewModel = _viewModel;
        if (viewModel is null)
        {
            return;
        }

        string key = e.Column.Tag as string ?? string.Empty;
        ModelManagerViewModel.ModelSort requested = key switch
        {
            "size" => ModelManagerViewModel.ModelSort.Size,
            "speed" => ModelManagerViewModel.ModelSort.Speed,
            _ => ModelManagerViewModel.ModelSort.Alias,
        };

        // Ascending unless this column is already the one being ordered that way,
        // which makes the first click A to Z and the next Z to A. Read from the
        // view model, not from the column: the column exposes no direction.
        bool ascending = !(viewModel.Sort == requested && viewModel.SortAscending);
        viewModel.SortByColumn(key, ascending);

        // The view model now holds the order; SyncSortGlyphs runs from its
        // PropertyChanged, which the assignment above raises.
    }

    /// <summary>
    /// Puts one arrow on the column being ordered and none on the rest.
    /// </summary>
    private void SyncSortGlyphs()
    {
        ModelManagerViewModel? viewModel = _viewModel;
        DataGrid? grid = this.FindControl<DataGrid>("ModelsGrid");
        if (viewModel is null || grid is null)
        {
            return;
        }

        string orderedBy = viewModel.Sort switch
        {
            ModelManagerViewModel.ModelSort.Size => "size",
            ModelManagerViewModel.ModelSort.Speed => "speed",
            _ => "alias",
        };

        string arrow = viewModel.SortAscending ? " \u2191" : " \u2193";

        foreach (DataGridColumn column in grid.Columns)
        {
            if (!_baseHeaders.TryGetValue(column, out string? baseHeader))
            {
                baseHeader = column.Header?.ToString() ?? string.Empty;
                _baseHeaders[column] = baseHeader;
            }

            // A column with no Tag has no order behind it, so it carries no arrow
            // whatever the view model happens to be doing.
            bool ordered = column.Tag is string tag && tag == orderedBy;
            column.Header = ordered ? baseHeader + arrow : baseHeader;
        }
    }
}