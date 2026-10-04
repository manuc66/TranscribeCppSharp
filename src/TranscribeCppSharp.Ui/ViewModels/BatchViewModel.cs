using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TranscribeCppSharp;
using TranscribeCppSharp.Interop;
using TranscribeCppSharp.Ui.Models;
using TranscribeCppSharp.Ui.Services;

namespace TranscribeCppSharp.Ui.ViewModels;

public partial class BatchViewModel : ObservableObject
{
    private readonly ITranscriptionService _transcriptionService;
    private readonly IModelDownloadService _modelDownloadService;

    [ObservableProperty]
    private string _modelAlias = "moss-transcribe-diarize";

    [ObservableProperty]
    private string _language = "en";

    [ObservableProperty]
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
        IModelDownloadService modelDownloadService)
    {
        _transcriptionService = transcriptionService;
        _modelDownloadService = modelDownloadService;
        LoadModels();
    }

    private void LoadModels()
    {
        AvailableModels.Clear();
        foreach (var model in _modelDownloadService.GetAvailableModels())
        {
            AvailableModels.Add(model.Alias);
        }
    }

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
