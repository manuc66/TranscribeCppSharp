using CommunityToolkit.Mvvm.ComponentModel;

namespace TranscribeCppSharp.Ui.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty]
    private string _title = "TranscribeCppSharp";

    [ObservableProperty]
    private int _selectedTabIndex;

    public TranscriptionViewModel Transcription { get; }
    public StreamingViewModel Streaming { get; }
    public BatchViewModel Batch { get; }
    public ModelManagerViewModel ModelManager { get; }
    public SettingsViewModel Settings { get; }

    public MainWindowViewModel(
        TranscriptionViewModel transcription,
        StreamingViewModel streaming,
        BatchViewModel batch,
        ModelManagerViewModel modelManager,
        SettingsViewModel settings)
    {
        Transcription = transcription;
        Streaming = streaming;
        Batch = batch;
        ModelManager = modelManager;
        Settings = settings;
    }
}
