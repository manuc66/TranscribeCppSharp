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
        var dialog = new Avalonia.Controls.OpenFileDialog
        {
            Title = "Select audio files",
            AllowMultiple = true
        };
        dialog.Filters.Add(new Avalonia.Controls.FileDialogFilter
        {
            Name = "Audio files",
            Extensions = { "wav", "mp3", "flac", "ogg", "m4a" }
        });

        var window = Avalonia.Application.Current?.ApplicationLifetime
            is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;

        if (window == null) return;

        var result = await dialog.ShowAsync(window);
        if (result != null)
        {
            foreach (var file in result)
            {
                if (!AudioFiles.Contains(file))
                    AudioFiles.Add(file);
            }
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

        var dialog = new Avalonia.Controls.SaveFileDialog
        {
            Title = "Export batch results",
            DefaultExtension = "json",
            Filters =
            {
                new Avalonia.Controls.FileDialogFilter { Name = "JSON", Extensions = { "json" } },
                new Avalonia.Controls.FileDialogFilter { Name = "Text", Extensions = { "txt" } }
            }
        };

        var window = Avalonia.Application.Current?.ApplicationLifetime
            is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;

        if (window == null) return;

        var path = await dialog.ShowAsync(window);
        if (string.IsNullOrWhiteSpace(path)) return;

        var ext = Path.GetExtension(path).ToLowerInvariant();
        var content = ext switch
        {
            ".json" => System.Text.Json.JsonSerializer.Serialize(Results, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            }),
            _ => string.Join("\n\n", Results.Select(r => $"{Path.GetFileName(r.AudioPath)}:\n{r.FullText}"))
        };

        await File.WriteAllTextAsync(path, content);
        StatusMessage = $"Exported to {path}";
    }
}
