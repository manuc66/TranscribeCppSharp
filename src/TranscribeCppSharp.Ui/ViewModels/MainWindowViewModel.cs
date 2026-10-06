using CommunityToolkit.Mvvm.ComponentModel;

namespace TranscribeCppSharp.Ui.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    /// <summary>
    /// Index of the Models tab, mirroring the order MainWindow.axaml declares.
    /// </summary>
    /// <remarks>
    /// Stated here rather than looked up, because the tabs are a fixed list. A test
    /// reads the header back at this index, so reordering the XAML without updating
    /// this constant fails the build's tests instead of quietly sending the reader
    /// to the Settings tab.
    /// </remarks>
    public const int ModelTabIndex = 3;

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

        // The three pickers each offer a way out to the catalogue, and none of them
        // can reach the window — they are constructed before it and hold no reference
        // to it. The event is what carries "take me to the models" upward, so the
        // decision about which tab that is stays in one place.
        Transcription.OpenModelManagerRequested += ShowModelManager;
        Streaming.OpenModelManagerRequested += ShowModelManager;
        Batch.OpenModelManagerRequested += ShowModelManager;
    }

    /// <summary>
    /// Re-reads what is on disk every time the reader moves between tabs.
    /// </summary>
    /// <remarks>
    /// Downloads and deletes happen in the Models tab, while the three pickers are
    /// built once when the window opens. Without this, the guidance under each picker
    /// would point at a download that never appears — the loop it opens would not
    /// close. It runs on every switch rather than only entering Models, so coming
    /// back from it is covered too, and it costs one <c>File.Exists</c> per catalogue
    /// entry.
    /// </remarks>
    partial void OnSelectedTabIndexChanged(int value)
    {
        Transcription.RefreshModels();
        Streaming.RefreshModels();
        Batch.RefreshModels();
    }

    private void ShowModelManager() => SelectedTabIndex = ModelTabIndex;
}
