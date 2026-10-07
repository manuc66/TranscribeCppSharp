using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TranscribeCppSharp.Models;
using TranscribeCppSharp.Performance;
using TranscribeCppSharp.Shared;
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

    /// <summary>
    /// How the list is ordered.
    /// </summary>
    public enum ModelSort
    {
        /// <summary>Alphabetical by alias.</summary>
        Alias = 0,

        /// <summary>Smallest download first.</summary>
        Size = 1,

        /// <summary>Fastest first, by measured real-time factor.</summary>
        Speed = 2,

        /// <summary>Family, as upstream records it.</summary>
        Family = 3,

        /// <summary>Whether the weights are on disk.</summary>
        OnDisk = 4,

        /// <summary>Licence identifier.</summary>
        Licence = 5,

        /// <summary>The languages the model card declares, as the column shows them.</summary>
        Languages = 6,

        /// <summary>What diarization the column currently reads.</summary>
        Diarization = 7,
    }

    /// <summary>
    /// Order applied to <see cref="VisibleModels"/>.
    /// </summary>
    /// <remarks>
    /// Ordered by rebuilding the collection rather than by a DataGrid sort
    /// description: the grid's collection is rebuilt on every filter change, and
    /// sort descriptions do not survive that. Header clicks reach this through
    /// <see cref="SortByColumn"/>, which sets the same two properties the sort
    /// buttons do, so there is one source of order regardless of which asked.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SortDescription))]
    private ModelSort _sort = ModelSort.Alias;

    /// <summary>True when the list is ordered ascending, whatever is ordered.</summary>
    /// <remarks>
    /// Renamed from SortFastestFirst when column headers started driving it: the
    /// name described one sort key while the flag now governs all three. True by
    /// default because ascending is what each sort button already did — the speed
    /// button set it true on first click, so no order changes.
    /// </remarks>
    [ObservableProperty]
    private bool _sortAscending = true;

    /// <summary>How the current order reads out, for the header.</summary>
    public string SortDescription => Sort switch
    {
        ModelSort.Size => "by size, smallest first",
        ModelSort.Speed => SortAscending ? "by measured speed, fastest first" : "by measured speed, slowest first",
        _ => "by name",
    };

    /// <summary>Label of the name-sort button, marked when it is the active order.</summary>
    public string NameSortText => Sort == ModelSort.Alias ? "• By name" : "By name";

    /// <summary>Label of the size-sort button, marked when it is the active order.</summary>
    public string SizeSortText => Sort == ModelSort.Size ? "• By size" : "By size";

    /// <summary>
    /// Label of the speed-sort button, marked when active, with the direction.
    /// </summary>
    public string SpeedSortText => Sort != ModelSort.Speed
        ? "By speed"
        : SortAscending ? "• Speed ↑" : "• Speed ↓";

    /// <summary>Every alias in the manifest, filtered by <see cref="Filter"/>.</summary>
    public ObservableCollection<ModelCatalogItem> Models { get; } = new();

    /// <summary>Aliases currently on disk, refreshed with the list.</summary>
    public ObservableCollection<ModelCatalogItem> DownloadedModels { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFilter))]
    [NotifyPropertyChangedFor(nameof(HasAnyQuickFilter))]
    [NotifyPropertyChangedFor(nameof(ActiveFilterSummary))]
    private string? _filter;

    /// <summary>
    /// Show only models already on disk.
    /// </summary>
    /// <remarks>
    /// The one toggle worth having by default: "which models can I use right
    /// now, without a download" is the question a returning user actually has.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAnyQuickFilter))]
    [NotifyPropertyChangedFor(nameof(ActiveFilterSummary))]
    private bool _downloadedOnly;

    /// <summary>
    /// Show only models whose licence forbids commercial use.
    /// </summary>
    /// <remarks>
    /// Inverted on purpose. Most entries are permissive, so the useful filter is
    /// "hide the ones I may not use commercially", not "show the two I may not".
    /// The flag is a text test on the SPDX id in the manifest, so the row still
    /// shows the licence and its URL.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAnyQuickFilter))]
    [NotifyPropertyChangedFor(nameof(ActiveFilterSummary))]
    private bool _hideNonCommercial;

    /// <summary>Size bucket, or null for every size.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAnyQuickFilter))]
    [NotifyPropertyChangedFor(nameof(ActiveFilterSummary))]
    private ModelSizeFilter _sizeFilter = ModelSizeFilter.Any;

    /// <summary>Licence to show, or null for every licence.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAnyQuickFilter))]
    [NotifyPropertyChangedFor(nameof(ActiveFilterSummary))]
    private string? _licenseFilter;

    /// <summary>
    /// Capability to show, or null for all of them.
    /// </summary>
    /// <remarks>
    /// Strings, like the licence and language filters: the items are what upstream
    /// calls the capability and the value is the same string, so there is no
    /// record/enum mismatch for the binding to silently fail on — which is what
    /// made the size picker inert until it was found.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAnyQuickFilter))]
    [NotifyPropertyChangedFor(nameof(ActiveFilterSummary))]
    private string? _featureFilter;

    /// <summary>Language to show, or null for every language.</summary>
    /// <remarks>
    /// Codes, not names: the manifest stores what the model card states, which is
    /// codes, and inventing a name table beside it would be a second source to keep
    /// in step. The free-text search matches codes too, so a reader who knows the
    /// name can still search by the code it corresponds to.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAnyQuickFilter))]
    [NotifyPropertyChangedFor(nameof(ActiveFilterSummary))]
    private string? _languageFilter;

    [ObservableProperty]
    private ModelCatalogItem? _selectedModel;

    /// <summary>
    /// Timings taken this session, by alias.
    /// </summary>
    /// <remarks>
    /// In memory only. Nothing is written to disk, so a result does not outlive
    /// the session, and there is nothing to invalidate when the machine changes:
    /// <see cref="MachineKey"/> is carried on each result so a later on-disk
    /// version could refuse to show a number measured elsewhere.
    /// </remarks>
    public Dictionary<string, ModelBenchmark> Benchmarks { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Fingerprint of this machine, used to keep results measured elsewhere out
    /// of a speed comparison.
    /// </summary>
    public string MachineKey { get; } = ModelBenchmarkService.MachineKey();

    /// <summary>
    /// Diarization answers from this session, by alias.
    /// </summary>
    /// <remarks>
    /// Kept across reloads: re-filtering the list should not reload a model just
    /// to re-ask a question whose answer cannot change while the app is running.
    /// </remarks>
    private readonly Dictionary<string, ModelCatalogItem.DiarizationSupport> _diarizationResults = new(StringComparer.Ordinal);

    /// <summary>True while <see cref="CheckDiarizationAsync"/> is walking the disk.</summary>
    private bool _checkingDiarization;

    /// <summary>The audio the timing runs are taken over.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBenchmarkAudio))]
    [NotifyPropertyChangedFor(nameof(BenchmarkAudioName))]
    private string? _benchmarkAudioPath;

    /// <summary>True once an audio file has been chosen.</summary>
    public bool HasBenchmarkAudio => !string.IsNullOrWhiteSpace(BenchmarkAudioPath);

    /// <summary>Name of the chosen file, for the toolbar.</summary>
    public string BenchmarkAudioName => HasBenchmarkAudio
        ? Path.GetFileName(BenchmarkAudioPath!)
        : "no audio chosen";

    /// <summary>How much of the audio each timing run uses, in seconds.</summary>
    [ObservableProperty]
    private int _benchmarkExcerptSeconds = ModelBenchmarkService.DefaultExcerptSeconds;

    /// <summary>Session threads for the timing runs, or 0 for the default.</summary>
    [ObservableProperty]
    private int _benchmarkThreads;

    /// <summary>Compute backend for the timing runs.</summary>
    [ObservableProperty]
    private Interop.BackendRequest _selectedBenchmarkBackend
        = Interop.BackendRequest.BackendAuto;

    /// <summary>Backends offered for the timing runs.</summary>
    public IReadOnlyList<Interop.BackendRequest> AvailableBenchmarkBackends { get; } =
        new[]
        {
            Interop.BackendRequest.BackendAuto,
            Interop.BackendRequest.BackendCpu,
            Interop.BackendRequest.BackendVulkan,
            Interop.BackendRequest.BackendMetal,
            Interop.BackendRequest.BackendCuda,
        };

    /// <summary>Progress through the current model's passes, 0 to 1.</summary>
    [ObservableProperty]
    private double _benchmarkProgress;

    /// <summary>Alias currently downloading or being deleted.</summary>
    [ObservableProperty]
    private string? _busyAlias;

    [ObservableProperty]
    private double _downloadProgress;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>Size buckets offered as a quick filter.</summary>
    /// <remarks>
    /// Built from the manifest rather than picked by feel: the sizes there run
    /// from 33 MB to 16 GB, so one linear range slider would be useless.
    /// </remarks>
    public IReadOnlyList<ModelSizeFilterOption> SizeFilters { get; } = new[]
    {
        new ModelSizeFilterOption(ModelSizeFilter.Any, "Any size"),
        new ModelSizeFilterOption(ModelSizeFilter.Small, "Under 500 MB"),
        new ModelSizeFilterOption(ModelSizeFilter.Medium, "500 MB - 1.5 GB"),
        new ModelSizeFilterOption(ModelSizeFilter.Large, "Over 1.5 GB"),
    };

    /// <summary>
    /// Capabilities any model in the catalog records, for the feature filter.
    /// </summary>
    /// <remarks>
    /// Taken from upstream's records rather than written here: every one of them is
    /// declared by at least one model, so no choice can return an empty grid for
    /// want of a model that ever had it.
    /// </remarks>
    public IReadOnlyList<string> Features { get; } = UpstreamCatalog.FeatureKeys;

    /// <summary>Licences present in the manifest, for the licence filter.</summary>
    public IReadOnlyList<string> Licenses { get; } =
        ModelStore.Catalog
            .Select(m => m.License)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(l => l, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// Every language any entry of the catalogue declares, in code order.
    /// </summary>
    /// <remarks>
    /// Built from the manifest rather than from a list somewhere else, so a model
    /// cannot be selectable by a language no row claims, and a language nobody
    /// claims cannot appear as a filter that returns nothing.
    /// </remarks>
    public IReadOnlyList<string> Languages { get; } =
        ModelStore.Catalog
            .Where(m => m.Languages is not null)
            .SelectMany(m => m.Languages!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(l => l, StringComparer.Ordinal)
            .ToList();

    /// <summary>True when any quick filter is narrowing the list.</summary>
    /// <remarks>
    /// The licence and language filters belong here too: the Clear button is bound to
    /// this, so with only a licence filter active it used to read as "nothing to
    /// clear" and leave a narrowed list with no way back.
    /// </remarks>
    public bool HasAnyQuickFilter
        => DownloadedOnly
        || HideNonCommercial
        || SizeFilter != ModelSizeFilter.Any
        || !string.IsNullOrEmpty(LicenseFilter)
        || !string.IsNullOrEmpty(LanguageFilter)
        || !string.IsNullOrEmpty(FeatureFilter);

    /// <summary>
    /// The quick filters in force, as one line, or empty when none are.
    /// </summary>
    /// <remarks>
    /// Said out loud because a filter that silently hides rows is how a user
    /// ends up convinced a model is missing from the catalogue.
    /// </remarks>
    public string ActiveFilterSummary
    {
        get
        {
            var parts = new List<string>(3);
            if (DownloadedOnly)
            {
                parts.Add("downloaded only");
            }

            if (HideNonCommercial)
            {
                parts.Add("commercial licences only");
            }

            if (SizeFilter != ModelSizeFilter.Any)
            {
                parts.Add(SizeFilters.First(o => o.Value == SizeFilter).Label.ToLowerInvariant());
            }

            if (!string.IsNullOrEmpty(LicenseFilter))
            {
                parts.Add($"licence {LicenseFilter}");
            }

            if (!string.IsNullOrEmpty(LanguageFilter))
            {
                parts.Add($"language {LanguageFilter}");
            }

            if (!string.IsNullOrEmpty(FeatureFilter))
            {
                parts.Add($"capability {FeatureFilter}");
            }

            if (!string.IsNullOrWhiteSpace(Filter))
            {
                parts.Add($"matching \"{Filter.Trim()}\"");
            }

            return parts.Count == 0
                ? string.Empty
                : $"Filtered by: {string.Join(", ", parts)}. {VisibleModels.Count} shown.";
        }
    }

    /// <summary>False while a download or delete is in flight.</summary>
    public bool IsBusy => BusyAlias is not null;

    /// <summary>What the busy alias is doing, for the status line.</summary>
    [ObservableProperty]
    private string? _busyAction;

    /// <summary>True while a model is being fetched rather than timed.</summary>
    public bool IsDownloading => string.Equals(BusyAction, "download", StringComparison.Ordinal);

    /// <summary>True when the alias currently downloading matches this row.</summary>
    public bool IsBusyAlias(string? alias)
        => alias is not null && string.Equals(alias, BusyAlias, StringComparison.Ordinal);

    public bool IsDownloadingAlias(string? alias) => IsBusyAlias(alias);

    public bool HasFilter => !string.IsNullOrWhiteSpace(Filter);

    /// <summary>Rows the grid binds to: the catalogue narrowed by the filter.</summary>
    public ObservableCollection<ModelCatalogItem> VisibleModels { get; } = new();

    /// <summary>Total size of the downloaded models, as display text.</summary>
    public string CacheSizeText => ModelSizeFormat.Format(DownloadedModels.Sum(m => m.CachedBytes)).ToString();

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
            if (_diarizationResults.TryGetValue(item.Alias, out ModelCatalogItem.DiarizationSupport diarization))
            {
                item.SetDiarization(diarization);
            }

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

    partial void OnDownloadedOnlyChanged(bool value) => ApplyFilter();

    partial void OnHideNonCommercialChanged(bool value) => ApplyFilter();

    partial void OnSizeFilterChanged(ModelSizeFilter value) => ApplyFilter();

    partial void OnLicenseFilterChanged(string? value) => ApplyFilter();

    partial void OnLanguageFilterChanged(string? value) => ApplyFilter();

    partial void OnFeatureFilterChanged(string? value) => ApplyFilter();

    partial void OnBusyAliasChanged(string? value)
    {
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(IsDownloading));
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
        if (item is null || IsBusyAlias(item.Alias) || _checkingDiarization)
        {
            return;
        }

        if (item.IsCached)
        {
            StatusMessage = $"{item.Alias} is already on disk at {item.LocalPath}";
            return;
        }

        BusyAlias = item.Alias;
        BusyAction = "download";
        DownloadProgress = 0;
        var log = new StringWriter();
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
            BusyAction = null;
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
        if (item is null || IsBusyAlias(item.Alias) || _checkingDiarization)
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
                ? $"Deleted {item.Alias} ({ModelSizeFormat.Format(item.CachedBytes)} freed)."
                : $"{item.Alias} could not be deleted. It may be in use, or the cache may not be writable.";
        }
        finally
        {
            BusyAlias = null;
            BusyAction = null;
        }
    }

    /// <summary>Removes every downloaded alias, one by one.</summary>
    [RelayCommand]
    private void DeleteAll()
    {
        if (_checkingDiarization)
        {
            StatusMessage = "Wait for the diarization check to finish first.";
            return;
        }

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
            : $"Deleted {removed} model(s), {ModelSizeFormat.Format(freed)} freed.";
    }

    /// <summary>
    /// Loads each downloaded model once and asks it whether it attributes
    /// speakers, filling the Diarization column.
    /// </summary>
    /// <remarks>
    /// A button rather than automatic on load: the only way to answer is
    /// <c>Model.Supports(FeatureDiarization)</c>, which needs the weights loaded,
    /// and loading tens of gigabytes unprompted when the window opens would be a
    /// surprise. Rows that are not on disk, or not checked, stay "?" — the
    /// manifest declares no capability and it is never guessed from the alias.
    /// The load is on the CPU so it does not take the GPU from a run.
    /// </remarks>
    [RelayCommand]
    private async Task CheckDiarizationAsync()
    {
        if (_checkingDiarization)
        {
            return;
        }

        if (IsBusy)
        {
            StatusMessage = "Wait for the current download or measurement to finish first.";
            return;
        }

        List<ModelCatalogItem> targets = DownloadedModels.ToList();
        if (targets.Count == 0)
        {
            StatusMessage = "No downloaded models to check. Download one first.";
            return;
        }

        _checkingDiarization = true;
        int done = 0;
        int failed = 0;
        try
        {
            for (int i = 0; i < targets.Count; i++)
            {
                ModelCatalogItem item = targets[i];
                item.SetDiarization(ModelCatalogItem.DiarizationSupport.Checking);
                StatusMessage = $"Checking diarization for {item.Alias} ({i + 1}/{targets.Count})...";

                try
                {
                    string path = item.LocalPath;
                    bool supported = await Task.Run(() =>
                    {
                        using var model = Model.Load(path, p => p.WithBackend(Interop.BackendRequest.BackendCpu));
                        return model.Supports(Interop.Feature.FeatureDiarization);
                    }).ConfigureAwait(true);

                    ModelCatalogItem.DiarizationSupport state = supported
                        ? ModelCatalogItem.DiarizationSupport.Supported
                        : ModelCatalogItem.DiarizationSupport.Unsupported;
                    _diarizationResults[item.Alias] = state;
                    item.SetDiarization(state);
                }
                catch (Exception ex)
                {
                    // A model that will not load cannot be classified: the cell
                    // shows "?" and the status names the reason, rather than
                    // answering "no" for a question that was never put.
                    item.SetDiarization(ModelCatalogItem.DiarizationSupport.LoadFailed);
                    failed++;
                    StatusMessage = $"Could not check {item.Alias}: {ex.Message}";
                }

                done++;
            }

            StatusMessage = failed == 0
                ? $"Checked diarization for {done} downloaded model(s)."
                : $"Checked diarization for {done - failed} of {done} downloaded model(s); {failed} could not be loaded.";
        }
        finally
        {
            _checkingDiarization = false;
        }
    }

    private void ApplyFilter()
    {
        string? needle = Filter?.Trim();
        ModelSizeFilterOption band = SizeFilters.First(o => o.Value == SizeFilter);

        VisibleModels.Clear();
        foreach (ModelCatalogItem item in Models)
        {
            if (DownloadedOnly && !item.IsCached)
            {
                continue;
            }

            if (HideNonCommercial && item.IsNonCommercialLicense)
            {
                continue;
            }

            if (!band.Contains(item.Descriptor.Size))
            {
                continue;
            }

            if (!string.IsNullOrEmpty(LicenseFilter)
                && !string.Equals(item.License, LicenseFilter, StringComparison.Ordinal))
            {
                continue;
            }

            // A row that declares no languages cannot be shown to have them: with a
            // language selected, "unknown" is hidden rather than assumed to match.
            if (!string.IsNullOrEmpty(LanguageFilter)
                && (item.Descriptor.Languages is null
                    || !item.Descriptor.Languages.Contains(LanguageFilter, StringComparer.Ordinal)))
            {
                continue;
            }

            // A model with no record is not a model that has the feature: unknown
            // is not yes, which is the same rule the language filter applies.
            if (!string.IsNullOrEmpty(FeatureFilter) && !item.Supports(FeatureFilter))
            {
                continue;
            }

            if (!string.IsNullOrEmpty(needle)
                && !item.Alias.Contains(needle, StringComparison.OrdinalIgnoreCase)
                && !item.Repo.Contains(needle, StringComparison.OrdinalIgnoreCase)
                && !item.Family.Contains(needle, StringComparison.OrdinalIgnoreCase)
                && !item.License.Contains(needle, StringComparison.OrdinalIgnoreCase)
                && item.Descriptor.Languages is not null
                    && !item.Descriptor.Languages.Any(l => l.Contains(needle, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            VisibleModels.Add(item);
        }

        ApplySort();

        // A row the filter just hid cannot stay selected; otherwise the detail
        // pane keeps showing a model that is no longer in the list.
        if (SelectedModel is not null && !VisibleModels.Contains(SelectedModel))
        {
            SelectedModel = VisibleModels.FirstOrDefault();
        }

        OnPropertyChanged(nameof(HasFilter));
        OnPropertyChanged(nameof(HasAnyQuickFilter));
        OnPropertyChanged(nameof(ActiveFilterSummary));
    }

    /// <summary>Turns every quick filter off.</summary>
    [RelayCommand]
    private void ClearFilters()
    {
        Filter = null;
        DownloadedOnly = false;
        HideNonCommercial = false;
        SizeFilter = ModelSizeFilter.Any;
        LicenseFilter = null;
        LanguageFilter = null;
        FeatureFilter = null;
        ApplyFilter();
    }

    /// <summary>Orders the list alphabetically.</summary>
    [RelayCommand]
    private void SortByName()
    {
        Sort = ModelSort.Alias;
        ApplyFilter();
    }

    /// <summary>
    /// Orders by a column, as a header click asks.
    /// </summary>
    /// <param name="column">Which column: "size", "speed", or anything else for the alias.</param>
    /// <param name="ascending">The direction the click implies.</param>
    /// <remarks>
    /// Reached from the view's Sorting handler, which has already cancelled the
    /// DataGrid's own attempt to sort: the grid's collection is rebuilt here, so
    /// a sort description written on it would be lost on the next filter change.
    /// Columns with no order behind them pass through as the alias, which is the
    /// default order rather than a sort they cannot have.
    /// </remarks>
    public void SortByColumn(string? column, bool ascending)
    {
        Sort = FromColumn(column);
        SortAscending = ascending;
        ApplyFilter();
    }

    /// <summary>
    /// Maps a column's Tag to the order behind it.
    /// </summary>
    /// <remarks>
    /// Anything unknown maps to the alias, which is the default order rather than
    /// an order invented for a column that has none — a header click on a column
    /// with no Tag never reaches here, because the view refuses it first.
    /// </remarks>
    internal static ModelSort FromColumn(string? column) => column switch
    {
        "size" => ModelSort.Size,
        "speed" => ModelSort.Speed,
        "family" => ModelSort.Family,
        "ondisk" => ModelSort.OnDisk,
        "licence" => ModelSort.Licence,
        "languages" => ModelSort.Languages,
        "diarization" => ModelSort.Diarization,
        _ => ModelSort.Alias,
    };

    /// <summary>Orders the list with the smallest download first.</summary>
    [RelayCommand]
    private void SortBySize()
    {
        Sort = ModelSort.Size;
        ApplyFilter();
    }

    /// <summary>
    /// Orders the list by measured speed, fastest first, or back to slowest.
    /// </summary>
    /// <remarks>
    /// Only models with a measurement sort into a speed order; the rest keep
    /// alphabetical order below them. Sorting by an absent number would either
    /// bury the models that were measured under the ones that were not, or invent
    /// a position for them, and both mislead.
    /// </remarks>
    [RelayCommand]
    private void SortBySpeed()
    {
        if (Sort != ModelSort.Speed)
        {
            Sort = ModelSort.Speed;
            SortAscending = true;
        }
        else
        {
            SortAscending = !SortAscending;
        }

        ApplyFilter();
    }

    /// <summary>
    /// Picks the audio the timing runs are taken over.
    /// </summary>
    /// <remarks>
    /// The user's own file, because the number is only meaningful for the audio
    /// it was measured on. No clip is bundled: a synthetic one would not exercise
    /// the decoder realistically, and shipping a recording of speech in the
    /// repository is a licensing question this project should not answer by
    /// picking something at random.
    /// </remarks>
    [RelayCommand]
    private async Task PickBenchmarkAudioAsync()
    {
        var window = Avalonia.Application.Current?.ApplicationLifetime
            is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null;
        if (window?.StorageProvider is null)
        {
            StatusMessage = "No window is available to pick a file from.";
            return;
        }

        var files = await window.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = "Pick audio to measure models on",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new Avalonia.Platform.Storage.FilePickerFileType("Audio files")
                {
                    Patterns = ["*.wav", "*.mp3", "*.flac", "*.ogg", "*.m4a"]
                }
            }
        });

        if (files.Count > 0)
        {
            BenchmarkAudioPath = Avalonia.Platform.Storage.StorageProviderExtensions.TryGetLocalPath(files[0])
                ?? string.Empty;
        }
    }

    /// <summary>Times the models already measured, most recent last.</summary>
    [RelayCommand]
    private async Task BenchmarkAllAsync()
    {
        if (string.IsNullOrWhiteSpace(BenchmarkAudioPath))
        {
            StatusMessage = "Pick an audio file first: a timing is only meaningful for the audio it was taken on.";
            return;
        }

        foreach (ModelCatalogItem item in DownloadedModels.ToList())
        {
            await BenchmarkOneAsync(item).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Times one model on this machine and records the result.
    /// </summary>
    /// <remarks>
    /// Runs off the UI thread and reports progress, because loading a large model
    /// takes long enough to freeze the window otherwise. Results live in memory
    /// for this session only: there is no on-disk store, so closing the app
    /// discards them and a re-run starts from nothing.
    /// </remarks>
    [RelayCommand]
    private async Task BenchmarkAsync(ModelCatalogItem? item)
        => await BenchmarkOneAsync(item).ConfigureAwait(true);

    private async Task BenchmarkOneAsync(ModelCatalogItem? item)
    {
        if (item is null || IsBusy || _checkingDiarization)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(BenchmarkAudioPath))
        {
            StatusMessage = "Pick an audio file first: a timing is only meaningful for the audio it was taken on.";
            return;
        }

        if (!item.IsCached)
        {
            StatusMessage = $"{item.Alias} is not on disk; download it before measuring it.";
            return;
        }

        BusyAlias = item.Alias;
        BusyAction = "benchmark";
        BenchmarkProgress = 0;
        StatusMessage = $"Measuring {item.Alias}...";

        try
        {
            var progress = new Progress<double>(p => BenchmarkProgress = p);
            string audioPath = BenchmarkAudioPath!;
            var backend = SelectedBenchmarkBackend;
            int? threads = BenchmarkThreads > 0 ? BenchmarkThreads : null;

            ModelBenchmark result = await Task.Run(
                () => ModelBenchmarkService.Measure(
                    item.Descriptor,
                    audioPath,
                    BenchmarkExcerptSeconds,
                    backend,
                    null,
                    threads,
                    progress),
                CancellationToken.None).ConfigureAwait(true);

            Benchmarks[item.Alias] = result;
            item.Benchmark = result;
            item.Refresh();
            Sort = ModelSort.Speed;
            SortAscending = true;
            ApplyFilter();

            StatusMessage = $"{item.Alias}: {result.ConditionsText}";
        }
        catch (Exception ex)
        {
            // A model that will not fit in memory, or that the backend refuses,
            // has to be reported rather than swallowed: it is the answer for that
            // model on this machine.
            StatusMessage = $"{item.Alias} could not be measured: {ex.Message}";
        }
        finally
        {
            BusyAlias = null;
            BusyAction = null;
        }
    }

    /// <summary>
    /// Orders <see cref="VisibleModels"/> in place, per <see cref="Sort"/>.
    /// </summary>
    /// <remarks>
    /// The rules live in <see cref="ModelBenchmarkOrder"/> in the wrapper so they
    /// can be tested directly; this only rebuilds the bound collection.
    /// </remarks>
    private void ApplySort()
    {
        // Columns ordered by the value they show, one rule for all of them: a
        // reader who clicks a column expects the order to match what is printed in
        // that column, not an order they then have to translate.
        Func<ModelCatalogItem, string>? shownValue = Sort switch
        {
            ModelSort.Family => m => m.Family,
            ModelSort.OnDisk => m => m.OnDiskText,
            ModelSort.Licence => m => m.License,
            ModelSort.Languages => m => m.LanguagesText,
            ModelSort.Diarization => m => m.DiarizationText,
            _ => null,
        };

        List<ModelCatalogItem> ordered;
        if (shownValue is not null)
        {
            ordered = ModelBenchmarkOrder.ByKey(
                VisibleModels, shownValue, StringComparer.Ordinal, SortAscending);
        }
        else
        {
            ordered = Sort switch
            {
                ModelSort.Size => ModelBenchmarkOrder.BySize(
                    VisibleModels, m => m.Alias, m => m.Descriptor.Size, SortAscending),
                ModelSort.Speed => ModelBenchmarkOrder.BySpeed(
                    VisibleModels, m => m.Alias, Benchmarks, MachineKey, SortAscending),
                _ => ModelBenchmarkOrder.ByName(VisibleModels, m => m.Alias, SortAscending),
            };
        }

        VisibleModels.Clear();
        foreach (ModelCatalogItem item in ordered)
        {
            VisibleModels.Add(item);
        }
    }

    /// <summary>
    /// Flattens what ModelStore wrote about a download into one status line.
    /// </summary>
    /// <param name="log">The writer ModelStore reported through.</param>
    /// <returns>Its content with the line breaks replaced by spaces.</returns>
    private static string Describe(TextWriter log)
        => (log.ToString() ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();

    private void Raise(string propertyName)
        => OnPropertyChanged(propertyName);
}