using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TranscribeCppSharp;
using TranscribeCppSharp.Interop;
using TranscribeCppSharp.Ui.Models;
using TranscribeCppSharp.Ui.Services;

namespace TranscribeCppSharp.Ui.ViewModels;

public partial class StreamingViewModel : ObservableObject
{
    private readonly ITranscriptionService _transcriptionService;
    private readonly IModelDownloadService _modelDownloadService;
    private CancellationTokenSource? _cts;

    [ObservableProperty]
    private string _modelAlias = "moss-transcribe-diarize";

    [ObservableProperty]
    private bool _isStreaming;

    [ObservableProperty]
    private string _committedText = string.Empty;

    [ObservableProperty]
    private string _tentativeText = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private ObservableCollection<string> _availableModels = new();

    public StreamingViewModel(
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
    private async System.Threading.Tasks.Task StartStreamingAsync()
    {
        IsStreaming = true;
        StatusMessage = "Streaming...";
        _cts = new CancellationTokenSource();

        try
        {
            var options = new StreamOptions
            {
                CommitPolicy = StreamCommitPolicy.StreamCommitAuto
            };

            await foreach (var update in _transcriptionService.StreamTranscribeAsync(
                ModelAlias, options, _cts.Token))
            {
                CommittedText = update.CommittedText;
                TentativeText = update.TentativeText;
            }

            StatusMessage = "Streaming complete.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Streaming cancelled.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsStreaming = false;
        }
    }

    [RelayCommand]
    private void StopStreaming()
    {
        _cts?.Cancel();
        StatusMessage = "Stopping...";
    }
}
