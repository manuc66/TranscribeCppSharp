using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TranscribeCppSharp.Models;
using TranscribeCppSharp.Ui.Models;

namespace TranscribeCppSharp.Ui.ViewModels;

/// <summary>
/// Lists the curated models, downloads them, and deletes what is on disk.
/// </summary>
/// <remarks>
/// The catalogue and the cache belong to <see cref="ModelStore"/> in the
/// wrapper, which is also what the CLI uses: a model downloaded here is
/// available to the CLI and vice versa. This class only turns that into a list
/// a grid can show and buttons a user can press.
/// </remarks>
public partial class ModelManagerViewModel : ObservableObject
{
    /// <summary>
    /// Where the weights live, shown so a user can find or clear them without
    /// the GUI.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "Bound from ModelManagerView.axaml; a static would not be reachable through the DataContext.")]
    public string CacheRoot => ModelStore.CacheRoot();

    public ModelManagerViewModel()
    {
        Reload();
    }

    /// <summary>Every alias in the manifest, filtered by <see cref="Filter"/>.</summary>
    public ObservableCollection<ModelCatalogItem> Models { get; } = new();

    /// <summary>Aliases currently on disk, refreshed with the list.</summary>
    public ObservableCollection<ModelCatalogItem> DownloadedModels { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFilter))]
    private string? _filter;

    [ObservableProperty]
    private ModelCatalogItem? _selectedModel;

    /// <summary>Alias currently downloading or being deleted.</summary>
    [ObservableProperty]
    private string? _busyAlias;

    [ObservableProperty]
    private double _downloadProgress;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>False while a download or delete is in flight.</summary>
    public bool IsBusy => BusyAlias is not null;

    /// <summary>True when the alias currently downloading matches this row.</summary>
    public bool IsBusyAlias(string? alias)
        => alias is not null && string.Equals(alias, BusyAlias, StringComparison.Ordinal);

    public bool IsDownloadingAlias(string? alias) => IsBusyAlias(alias);

    public bool HasFilter => !string.IsNullOrWhiteSpace(Filter);

    /// <summary>Rows the grid binds to: the catalogue narrowed by the filter.</summary>
    public ObservableCollection<ModelCatalogItem> VisibleModels { get; } = new();

    /// <summary>Total size of the downloaded models, as display text.</summary>
    public string CacheSizeText => ModelCatalogItem.HumanSize(DownloadedModels.Sum(m => m.CachedBytes));

    /// <summary>Number of aliases in the manifest that are on disk.</summary>
    public int DownloadedCount => DownloadedModels.Count;

    [RelayCommand]
    private void Reload()
    {
        Models.Clear();
        DownloadedModels.Clear();

        foreach (ModelDescriptor descriptor in ModelStore.Catalog)
        {
            var item = new ModelCatalogItem(descriptor);
            Models.Add(item);
            if (item.IsCached)
            {
                DownloadedModels.Add(item);
            }
        }

        ApplyFilter();
        Raise(nameof(CacheRoot));
        Raise(nameof(CacheSizeText));
        Raise(nameof(DownloadedCount));
        Raise(nameof(SelectedModel));
        OnPropertyChanged(nameof(IsBusy));
    }

    partial void OnFilterChanged(string? value) => ApplyFilter();

    partial void OnBusyAliasChanged(string? value)
    {
        OnPropertyChanged(nameof(IsBusy));
        Raise(nameof(IsBusyAlias));
        Raise(nameof(IsDownloadingAlias));
    }

    /// <summary>
    /// Downloads one alias if it is not already on disk.
    /// </summary>
    /// <remarks>
    /// Runs on a worker thread because ModelStore blocks while it downloads.
    /// The status text carries what ModelStore writes about the source and the
    /// licence, which is why it is collected rather than replaced by a generic
    /// "downloading" line.
    /// </remarks>
    [RelayCommand]
    private async Task DownloadAsync(ModelCatalogItem? item)
    {
        if (item is null || IsBusyAlias(item.Alias))
        {
            return;
        }

        if (item.IsCached)
        {
            StatusMessage = $"{item.Alias} is already on disk at {item.LocalPath}";
            return;
        }

        BusyAlias = item.Alias;
        DownloadProgress = 0;
        var log = new System.IO.StringWriter();
        try
        {
            var progress = new Progress<double>(p => DownloadProgress = p);
            await Task.Run(
                () => ModelStore.Resolve(item.Alias, null, log, progress),
                CancellationToken.None).ConfigureAwait(true);

            item.Refresh();
            if (!DownloadedModels.Contains(item))
            {
                DownloadedModels.Add(item);
            }

            Raise(nameof(CacheSizeText));
            Raise(nameof(DownloadedCount));
            StatusMessage = $"Downloaded {item.Alias} to {item.LocalPath}. {Describe(log)}".TrimEnd();
        }
        catch (Exception ex)
        {
            // ModelStore verifies sha256 before moving the file into place, so
            // anything reaching here means nothing usable was cached.
            item.Refresh();
            StatusMessage = $"Could not download {item.Alias}: {ex.Message}";
        }
        finally
        {
            BusyAlias = null;
        }
    }

    /// <summary>
    /// Deletes one alias from disk.
    /// </summary>
    /// <remarks>
    /// Only the file for that alias is removed, and the directory holding it only
    /// if that leaves it empty. ModelStore.Delete reports false when it removed
    /// nothing, which is the honest answer for a row that was never downloaded.
    /// </remarks>
    [RelayCommand]
    private void Delete(ModelCatalogItem? item)
    {
        if (item is null || IsBusyAlias(item.Alias))
        {
            return;
        }

        if (!item.IsCached)
        {
            StatusMessage = $"{item.Alias} is not on disk.";
            return;
        }

        try
        {
            bool removed = ModelStore.Delete(item.Descriptor);
            item.Refresh();
            DownloadedModels.Remove(item);
            Raise(nameof(CacheSizeText));
            Raise(nameof(DownloadedCount));

            StatusMessage = removed
                ? $"Deleted {item.Alias} ({ModelCatalogItem.HumanSize(item.CachedBytes)} freed)."
                : $"{item.Alias} could not be deleted. It may be in use, or the cache may not be writable.";
        }
        finally
        {
            BusyAlias = null;
        }
    }

    /// <summary>Removes every downloaded alias, one by one.</summary>
    [RelayCommand]
    private void DeleteAll()
    {
        int removed = 0;
        long freed = 0;
        foreach (ModelCatalogItem item in DownloadedModels.ToList())
        {
            freed += item.CachedBytes;
            if (ModelStore.Delete(item.Descriptor))
            {
                removed++;
            }

            item.Refresh();
        }

        DownloadedModels.Clear();
        Raise(nameof(CacheSizeText));
        Raise(nameof(DownloadedCount));
        StatusMessage = removed == 0
            ? "Nothing to delete."
            : $"Deleted {removed} model(s), {ModelCatalogItem.HumanSize(freed)} freed.";
    }

    private void ApplyFilter()
    {
        string? needle = Filter?.Trim();

        VisibleModels.Clear();
        foreach (ModelCatalogItem item in Models)
        {
            if (string.IsNullOrEmpty(needle)
                || item.Alias.Contains(needle, StringComparison.OrdinalIgnoreCase)
                || item.Repo.Contains(needle, StringComparison.OrdinalIgnoreCase)
                || item.License.Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                VisibleModels.Add(item);
            }
        }

        // A row the filter just hid cannot stay selected; otherwise the detail
        // pane keeps showing a model that is no longer in the list.
        if (SelectedModel is not null && !VisibleModels.Contains(SelectedModel))
        {
            SelectedModel = VisibleModels.FirstOrDefault();
        }

        OnPropertyChanged(nameof(HasFilter));
    }

    /// <summary>
    /// Flattens what ModelStore wrote about a download into one status line.
    /// </summary>
    /// <param name="log">The writer ModelStore reported through.</param>
    /// <returns>Its content with the line breaks replaced by spaces.</returns>
    private static string Describe(System.IO.TextWriter log)
        => (log.ToString() ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();

    private void Raise(string propertyName)
        => OnPropertyChanged(propertyName);
}