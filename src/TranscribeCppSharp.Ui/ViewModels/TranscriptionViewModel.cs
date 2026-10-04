using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TranscribeCppSharp;
using TranscribeCppSharp.Interop;
using TranscribeCppSharp.Ui.Models;
using TranscribeCppSharp.Ui.Services;

namespace TranscribeCppSharp.Ui.ViewModels;

public partial class TranscriptionViewModel : ObservableObject
{
    private readonly ITranscriptionService _transcriptionService;
    private readonly IModelDownloadService _modelDownloadService;

    [ObservableProperty]
    private string _audioPath = string.Empty;

    [ObservableProperty]
    private string _modelAlias = "moss-transcribe-diarize";

    [ObservableProperty]
    private string _language = "en";

    [ObservableProperty]
    private bool _isTranscribing;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private TranscriptionResult? _result;

    [ObservableProperty]
    private string _fullText = string.Empty;

    [ObservableProperty]
    private ObservableCollection<TranscribeCppSharp.SegmentResult> _segments = new();

    [ObservableProperty]
    private ObservableCollection<TranscribeCppSharp.WordResult> _words = new();

    [ObservableProperty]
    private ObservableCollection<TranscribeCppSharp.SpeakerSegmentResult> _speakerSegments = new();

    [ObservableProperty]
    private bool _hasResult;

    public ObservableCollection<string> AvailableModels { get; } = new();
    public ObservableCollection<string> AvailableLanguages { get; } = new()
    {
        "en", "fr", "de", "es", "it", "pt", "ru", "zh", "ja", "ko"
    };

    public TranscriptionViewModel(
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
    private async System.Threading.Tasks.Task BrowseAudioAsync()
    {
        var dialog = new Avalonia.Controls.OpenFileDialog
        {
            Title = "Select audio file",
            AllowMultiple = false
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
        if (result?.Length > 0)
        {
            AudioPath = result[0];
        }
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task TranscribeAsync()
    {
        if (string.IsNullOrWhiteSpace(AudioPath) || !File.Exists(AudioPath))
        {
            StatusMessage = "Please select a valid audio file.";
            return;
        }

        IsTranscribing = true;
        Progress = 0;
        StatusMessage = "Transcribing...";
        HasResult = false;

        try
        {
            var options = new TranscriptionOptions
            {
                Language = Language,
                TimestampKind = TimestampKind.TimestampsSegment,
                DiarizeMode = DiarizeMode.DiarizeModeDefault
            };

            var progress = new Progress<double>(p => Progress = p);

            Result = await _transcriptionService.TranscribeAsync(
                AudioPath, ModelAlias, options, progress);

            FullText = Result.FullText;
            Segments = new ObservableCollection<TranscribeCppSharp.SegmentResult>(Result.Segments);
            Words = new ObservableCollection<TranscribeCppSharp.WordResult>(Result.Words);
            SpeakerSegments = new ObservableCollection<TranscribeCppSharp.SpeakerSegmentResult>(Result.SpeakerSegments);
            HasResult = true;
            StatusMessage = "Transcription complete.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsTranscribing = false;
        }
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task ExportAsync()
    {
        if (Result == null) return;

        var dialog = new Avalonia.Controls.SaveFileDialog
        {
            Title = "Export transcript",
            DefaultExtension = "txt",
            Filters =
            {
                new Avalonia.Controls.FileDialogFilter { Name = "Text", Extensions = { "txt" } },
                new Avalonia.Controls.FileDialogFilter { Name = "WebVTT", Extensions = { "vtt" } },
                new Avalonia.Controls.FileDialogFilter { Name = "JSON", Extensions = { "json" } }
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
            ".vtt" => ExportAsVtt(),
            ".json" => ExportAsJson(),
            _ => FullText
        };

        await File.WriteAllTextAsync(path, content);
        StatusMessage = $"Exported to {path}";
    }

    private string ExportAsVtt()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("WEBVTT");
        sb.AppendLine();
        foreach (var segment in Segments)
        {
            sb.AppendLine($"{FormatTime(segment.Start)} --> {FormatTime(segment.End)}");
            sb.AppendLine(segment.Text);
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private string ExportAsJson()
    {
        return System.Text.Json.JsonSerializer.Serialize(Result, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true
        });
    }

    private static string FormatTime(TimeSpan time)
    {
        return $"{time.Hours:D2}:{time.Minutes:D2}:{time.Seconds:D2}.{time.Milliseconds:D3}";
    }
}
