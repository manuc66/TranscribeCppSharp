using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TranscribeCppSharp.Ui.Models;
using TranscribeCppSharp.Ui.Services;

namespace TranscribeCppSharp.Ui.ViewModels;

public partial class ModelManagerViewModel : ObservableObject
{
    private readonly IModelDownloadService _modelDownloadService;

    [ObservableProperty]
    private ObservableCollection<ModelInfo> _models = new();

    [ObservableProperty]
    private ModelInfo? _selectedModel;

    [ObservableProperty]
    private bool _isDownloading;

    [ObservableProperty]
    private double _downloadProgress;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public ModelManagerViewModel(IModelDownloadService modelDownloadService)
    {
        _modelDownloadService = modelDownloadService;
        LoadModels();
    }

    private void LoadModels()
    {
        Models.Clear();
        foreach (var model in _modelDownloadService.GetAvailableModels())
        {
            Models.Add(model);
        }
    }

    [RelayCommand]
    private async Task DownloadAsync(ModelInfo? model)
    {
        if (model == null) return;

        IsDownloading = true;
        DownloadProgress = 0;
        StatusMessage = $"Downloading {model.Alias}...";

        try
        {
            var progress = new Progress<double>(p => DownloadProgress = p);
            await _modelDownloadService.DownloadModelAsync(model.Alias, progress);
            StatusMessage = $"Downloaded {model.Alias}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsDownloading = false;
        }
    }

    [RelayCommand]
    private void Refresh()
    {
        LoadModels();
        StatusMessage = "Model list refreshed.";
    }
}
