using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TranscribeCppSharp;
using TranscribeCppSharp.Interop;
using TranscribeCppSharp.Ui.Models;
using TranscribeCppSharp.Ui.Services;

namespace TranscribeCppSharp.Ui.ViewModels;

public partial class StreamingViewModel : ObservableObject, IDisposable
{
    private readonly ITranscriptionService _transcriptionService;
    private readonly IModelDownloadService _modelDownloadService;
    private CancellationTokenSource? _cts;
    private bool _disposed;

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

    [ObservableProperty]
    private StreamCommitPolicy _selectedCommitPolicy = StreamCommitPolicy.StreamCommitAuto;

    [ObservableProperty]
    private int? _stablePrefixAgreement;

    [ObservableProperty]
    private int? _moonshineMinDecodeIntervalMs;

    [ObservableProperty]
    private int? _parakeetAttContextRight;

    [ObservableProperty]
    private int? _parakeetLeftMs;

    [ObservableProperty]
    private int? _parakeetChunkMs;

    [ObservableProperty]
    private int? _parakeetRightMs;

    [ObservableProperty]
    private SortformerPreset? _selectedSortformerPreset;

    [ObservableProperty]
    private int? _voxtralNumDelayTokens;

    [ObservableProperty]
    private int? _voxtralMinDecodeIntervalMs;

    public ObservableCollection<StreamCommitPolicy> AvailableCommitPolicies { get; } = new()
    {
        StreamCommitPolicy.StreamCommitAuto,
        StreamCommitPolicy.StreamCommitOnFinalize,
        StreamCommitPolicy.StreamCommitStablePrefix,
    };

    public ObservableCollection<SortformerPreset> AvailableSortformerPresets { get; } = new()
    {
        SortformerPreset.SortformerPresetDefault,
        SortformerPreset.SortformerPresetVeryHighLatency,
        SortformerPreset.SortformerPresetHighLatency,
        SortformerPreset.SortformerPresetLowLatency,
    };

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
        if (IsStreaming)
        {
            return;
        }

        IsStreaming = true;
        StatusMessage = "Streaming...";
        CommittedText = string.Empty;
        TentativeText = string.Empty;
        _cts = new CancellationTokenSource();

        try
        {
            var options = new StreamOptions
            {
                CommitPolicy = SelectedCommitPolicy,
                StablePrefixAgreement = (uint?)StablePrefixAgreement,
                MoonshineMinDecodeIntervalMs = MoonshineMinDecodeIntervalMs,
                ParakeetAttContextRight = ParakeetAttContextRight,
                ParakeetLeftMs = ParakeetLeftMs,
                ParakeetChunkMs = ParakeetChunkMs,
                ParakeetRightMs = ParakeetRightMs,
                SortformerPreset = SelectedSortformerPreset,
                VoxtralNumDelayTokens = VoxtralNumDelayTokens,
                VoxtralMinDecodeIntervalMs = VoxtralMinDecodeIntervalMs,
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

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cts?.Cancel();
        _cts?.Dispose();
        GC.SuppressFinalize(this);
    }
}
