using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TranscribeCppSharp.Interop;
using TranscribeCppSharp.Models;
using TranscribeCppSharp.UI.Models;
using TranscribeCppSharp.UI.Services;

namespace TranscribeCppSharp.UI.ViewModels;

public partial class BatchViewModel : ObservableObject
{
    private readonly ITranscriptionService _transcriptionService;

    private readonly SettingsViewModel _settings;

    /// <summary>The batch's cancellation, while one is in flight.</summary>
    private Action? _cancelBatch;

    [ObservableProperty]
    private string _modelAlias = string.Empty;

    [ObservableProperty]
    private string _language = "en";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanProcessBatch))]
    [NotifyPropertyChangedFor(nameof(CanCancelBatch))]
    private bool _isProcessing;

    [ObservableProperty]
    private double _progress;

    /// <summary>The file in flight, by name, while the batch runs.</summary>
    [ObservableProperty]
    private string _currentFile = string.Empty;

    /// <summary>Its position in the queue, e.g. "File 2 of 5".</summary>
    [ObservableProperty]
    private string _currentFileStep = string.Empty;

    /// <summary>What is happening to it: started, done, or failed.</summary>
    [ObservableProperty]
    private string _currentFileStatus = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private ObservableCollection<string> _audioFiles = new();

    [ObservableProperty]
    private ObservableCollection<BatchItemResult> _results = new();

    [ObservableProperty]
    private ObservableCollection<string> _availableModels = new();

    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static readonly string[] AudioPatterns = ["*.wav", "*.mp3", "*.flac", "*.ogg", "*.m4a"];
    private static readonly string[] JsonPatterns = ["*.json"];
    private static readonly string[] TextPatterns = ["*.txt"];

    public BatchViewModel(
        ITranscriptionService transcriptionService,
        SettingsViewModel settings)
    {
        _transcriptionService = transcriptionService;
        _settings = settings;
        LoadModels();
    }

    /// <summary>Whether a batch can start at all: a model on disk, and no batch running.</summary>
    public bool CanProcessBatch => HasDownloadedModels && !IsProcessing;

    /// <summary>True while a batch is in flight and can still be stopped.</summary>
    public bool CanCancelBatch => IsProcessing;

    /// <summary>Whether any model is on disk, which is what the picker offers.</summary>
    public bool HasDownloadedModels => AvailableModels.Count > 0;

    /// <summary>
    /// What sits under the model picker. Present even when the list is full: the
    /// route to the other models should not appear only once you have run out.
    /// </summary>
    public string ModelPickerHint
        => DownloadedModelCatalog.Hint(AvailableModels.Count, ModelStore.Catalog.Count);

    /// <summary>Raised when the reader asks to go and fetch more models.</summary>
    public event Action? OpenModelManagerRequested;

    /// <summary>The button under the picker. The window decides which tab that means.</summary>
    [RelayCommand]
    private void OpenModelManager() => OpenModelManagerRequested?.Invoke();

    /// <summary>
    /// Rebuilds the picker from what is on disk and repairs the selection.
    /// </summary>
    /// <remarks>
    /// The selection has to be repaired rather than left alone: the picker only
    /// offers downloaded models, so an alias that was deleted would show as no
    /// selection while still running a batch against a file that is not there. The
    /// repaired selection is the model this tab last ran when it is still offered,
    /// so reopening the app lands on the model you were using; see
    /// <see cref="DownloadedModelCatalog.Selection"/>.
    /// </remarks>
    private void LoadModels()
    {
        // See TranscriptionViewModel.LoadModels.
        string currentModelAlias = ModelAlias;

        AvailableModels.Clear();
        foreach (string alias in DownloadedModelCatalog.Aliases())
        {
            AvailableModels.Add(alias);
        }

        ModelAlias = DownloadedModelCatalog.Selection(
            currentModelAlias, AvailableModels, UiSettings.LastModel(UiSettings.BatchTab));

        OnPropertyChanged(nameof(ModelPickerHint));
        OnPropertyChanged(nameof(HasDownloadedModels));
        OnPropertyChanged(nameof(CanProcessBatch));
    }

    /// <summary>
    /// Re-reads the cache. Called when the reader comes back from the Models tab,
    /// which is where a download or a delete happens.
    /// </summary>
    public void RefreshModels() => LoadModels();

    [RelayCommand]
    private async System.Threading.Tasks.Task AddFilesAsync()
    {
        var window = Avalonia.Application.Current?.ApplicationLifetime
            is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;

        if (window == null) return;

        var files = await window.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = "Select audio files",
            SuggestedStartLocation = await PickerFolder.StartAsync(window.StorageProvider),
            AllowMultiple = true,
            FileTypeFilter = new[]
            {
                new Avalonia.Platform.Storage.FilePickerFileType("Audio files")
                {
                    Patterns = AudioPatterns
                }
            }
        });

        string? firstPicked = null;
        foreach (var file in files)
        {
            var path = Avalonia.Platform.Storage.StorageProviderExtensions.TryGetLocalPath(file);
            if (path != null)
            {
                firstPicked ??= path;
                if (!AudioFiles.Contains(path))
                    AudioFiles.Add(path);
            }
        }

        PickerFolder.Remember(firstPicked);
    }

    [RelayCommand]
    private void RemoveFile(string file)
    {
        if (file != null)
            AudioFiles.Remove(file);
    }

    [RelayCommand]
    private void ClearFiles()
    {
        AudioFiles.Clear();
        Results.Clear();
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task ProcessBatchAsync()
    {
        if (AudioFiles.Count == 0)
        {
            StatusMessage = "Please add audio files first.";
            return;
        }

        IsProcessing = true;
        Progress = 0;
        CurrentFile = string.Empty;
        CurrentFileStep = "Loading the model…";
        CurrentFileStatus = string.Empty;
        StatusMessage = "Processing...";
        Results.Clear();
        UiSettings.SetLastModel(UiSettings.BatchTab, ModelAlias);

        // Snapshot the queue: the Add/Clear buttons stay available, and a file
        // added while the run is in flight must not change the denominator of a
        // progress bar that describes the list the service was given.
        List<string> queue = AudioFiles.ToList();

        using var cancellation = new CancellationTokenSource();
        _cancelBatch = cancellation.Cancel;

        try
        {
            var options = new TranscriptionOptions
            {
                // The backend and device chosen in Settings. These were
                // displayed but read by nothing, so the pickers did nothing.
                BackendRequest = _settings.SelectedBackend,
                Device = _settings.SelectedDevice,
                Language = Language,
                // The batch has no capability snapshot for its model, so it asks
                // for the one value the dispatcher never rejects: AUTO resolves
                // to the family's own maximum. SEGMENT would fail on a model whose
                // maximum is coarser (granite-speech-4.1-2b-plus, for one).
                TimestampKind = TimestampKind.TimestampsAuto,
                DiarizeMode = DiarizeMode.DiarizeModeDefault
            };

            var progress = new Progress<int>(p =>
            {
                Progress = (double)p / queue.Count;
                StatusMessage = $"Processing {p}/{queue.Count}...";
            });

            // The per-file step the service can state: there is no window
            // callback inside one file, because a batch runs the whole file in
            // one native call. Started names the file; Completed/Failed is what
            // happened to it. No fraction is invented between them.
            var fileStep = new Progress<BatchFileStep>(s =>
            {
                CurrentFileStep = $"File {s.Index} of {s.Count}";
                CurrentFile = Path.GetFileName(s.AudioPath);
                CurrentFileStatus = s.Phase switch
                {
                    BatchFilePhase.Started => "Reading the audio and transcribing…",
                    BatchFilePhase.Completed => "Done.",
                    BatchFilePhase.Failed => "Failed; the rest of the queue continues.",
                    _ => string.Empty,
                };
            });

            var result = await _transcriptionService.BatchTranscribeAsync(
                queue, ModelAlias, options, progress, fileStep, cancellation.Token);

            Results = new ObservableCollection<BatchItemResult>(result);

            // A cancelled batch is not a failed one: the service returns the items
            // that finished, and the token is what says the rest did not run.
            StatusMessage = cancellation.IsCancellationRequested
                ? $"Batch cancelled — {result.Count} of {queue.Count} finished."
                : "Batch processing complete.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Batch cancelled.";
        }
        catch (Exception ex)
        {
            UiLog.Failure("batch run", ex);
            StatusMessage = $"Error: {ex.Message} (logged to {UiLog.FilePath})";
        }
        finally
        {
            _cancelBatch = null;
            IsProcessing = false;
        }
    }

    /// <summary>Stops the batch in flight, if any; finished items stay in the grid.</summary>
    [RelayCommand]
    private void CancelBatch() => _cancelBatch?.Invoke();

    [RelayCommand]
    private async System.Threading.Tasks.Task ExportResultsAsync()
    {
        if (Results.Count == 0) return;

        var window = Avalonia.Application.Current?.ApplicationLifetime
            is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;

        if (window == null) return;

        try
        {
            var file = await window.StorageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
            {
                Title = "Export batch results",
                SuggestedStartLocation = await PickerFolder.StartAsync(window.StorageProvider),
                DefaultExtension = "json",
                FileTypeChoices = new[]
                {
                    new Avalonia.Platform.Storage.FilePickerFileType("JSON") { Patterns = JsonPatterns },
                    new Avalonia.Platform.Storage.FilePickerFileType("Text") { Patterns = TextPatterns }
                }
            });

            if (file == null) return;

            var path = Avalonia.Platform.Storage.StorageProviderExtensions.TryGetLocalPath(file);
            if (string.IsNullOrWhiteSpace(path))
            {
                StatusMessage = "Could not export: the chosen location has no local path.";
                return;
            }

            PickerFolder.Remember(path);
            await ExportResultsToAsync(path);
        }
        catch (Exception ex)
        {
            UiLog.Failure("batch export", ex);
            StatusMessage = $"Could not export: {ex.Message} (logged to {UiLog.FilePath})";
        }
    }

    /// <summary>
    /// Writes the results to <paramref name="path"/>, JSON for a .json name and
    /// plain text otherwise. Internal so a disk failure is testable without a
    /// file picker; it reports the failure rather than throwing.
    /// </summary>
    internal async System.Threading.Tasks.Task ExportResultsToAsync(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        try
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            var content = ext switch
            {
                ".json" => System.Text.Json.JsonSerializer.Serialize(Results, JsonOptions),
                _ => string.Join("\n\n", Results.Select(r => $"{Path.GetFileName(r.AudioPath)}:\n{r.FullText}"))
            };

            await File.WriteAllTextAsync(path, content);
            StatusMessage = $"Exported to {path}";
        }
        catch (Exception ex)
        {
            UiLog.Failure("batch export", ex);
            StatusMessage = $"Could not export: {ex.Message} (logged to {UiLog.FilePath})";
        }
    }
}
