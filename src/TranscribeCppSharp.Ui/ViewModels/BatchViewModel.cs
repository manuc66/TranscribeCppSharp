using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TranscribeCppSharp.Interop;
using TranscribeCppSharp.Models;
using TranscribeCppSharp.Ui.Models;
using TranscribeCppSharp.Ui.Services;

namespace TranscribeCppSharp.Ui.ViewModels;

public partial class BatchViewModel : ObservableObject
{
    private readonly ITranscriptionService _transcriptionService;

    private readonly SettingsViewModel _settings;

    [ObservableProperty]
    private string _modelAlias = "moss-transcribe-diarize";

    [ObservableProperty]
    private string _language = "en";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanProcessBatch))]
    private bool _isProcessing;

    [ObservableProperty]
    private double _progress;

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
    /// selection while still running a batch against a file that is not there.
    /// </remarks>
    private void LoadModels()
    {
        AvailableModels.Clear();
        foreach (string alias in DownloadedModelCatalog.Aliases())
        {
            AvailableModels.Add(alias);
        }

        if (AvailableModels.Count == 0)
        {
            ModelAlias = string.Empty;
        }
        else if (!AvailableModels.Contains(ModelAlias))
        {
            ModelAlias = AvailableModels[0];
        }

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
            AllowMultiple = true,
            FileTypeFilter = new[]
            {
                new Avalonia.Platform.Storage.FilePickerFileType("Audio files")
                {
                    Patterns = AudioPatterns
                }
            }
        });

        foreach (var file in files)
        {
            var path = Avalonia.Platform.Storage.StorageProviderExtensions.TryGetLocalPath(file);
            if (path != null && !AudioFiles.Contains(path))
                AudioFiles.Add(path);
        }
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
        StatusMessage = "Processing...";
        Results.Clear();

        try
        {
            var options = new TranscriptionOptions
            {
                // The backend and device chosen in Settings. These were
                // displayed but read by nothing, so the pickers did nothing.
                BackendRequest = _settings.SelectedBackend,
                Device = _settings.SelectedDevice,
                Language = Language,
                TimestampKind = TimestampKind.TimestampsSegment,
                DiarizeMode = DiarizeMode.DiarizeModeDefault
            };

            var progress = new Progress<int>(p =>
            {
                Progress = (double)p / AudioFiles.Count;
                StatusMessage = $"Processing {p}/{AudioFiles.Count}...";
            });

            var result = await _transcriptionService.BatchTranscribeAsync(
                AudioFiles.ToList(), ModelAlias, options, progress);

            Results = new ObservableCollection<BatchItemResult>(result);
            StatusMessage = "Batch processing complete.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsProcessing = false;
        }
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task ExportResultsAsync()
    {
        if (Results.Count == 0) return;

        var window = Avalonia.Application.Current?.ApplicationLifetime
            is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;

        if (window == null) return;

        var file = await window.StorageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
        {
            Title = "Export batch results",
            DefaultExtension = "json",
            FileTypeChoices = new[]
            {
                new Avalonia.Platform.Storage.FilePickerFileType("JSON") { Patterns = JsonPatterns },
                new Avalonia.Platform.Storage.FilePickerFileType("Text") { Patterns = TextPatterns }
            }
        });

        if (file == null) return;

        var path = Avalonia.Platform.Storage.StorageProviderExtensions.TryGetLocalPath(file);
        if (string.IsNullOrWhiteSpace(path)) return;

        var ext = Path.GetExtension(path).ToLowerInvariant();
        var content = ext switch
        {
            ".json" => System.Text.Json.JsonSerializer.Serialize(Results, JsonOptions),
            _ => string.Join("\n\n", Results.Select(r => $"{Path.GetFileName(r.AudioPath)}:\n{r.FullText}"))
        };

        await File.WriteAllTextAsync(path, content);
        StatusMessage = $"Exported to {path}";
    }
}
