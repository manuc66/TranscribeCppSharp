using CommunityToolkit.Mvvm.ComponentModel;
using TranscribeCppSharp.UI.Models;
using TranscribeCppSharp.UI.Services;

namespace TranscribeCppSharp.UI.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    /// <summary>
    /// Index of the Transcription tab, mirroring the order MainWindow.axaml declares.
    /// </summary>
    public const int TranscriptionTabIndex = 0;

    /// <summary>
    /// Index of the Models tab, mirroring the order MainWindow.axaml declares.
    /// </summary>
    /// <remarks>
    /// Stated here rather than looked up, because the tabs are a fixed list. A test
    /// reads the header back at this index, so reordering the XAML without updating
    /// this constant fails the build's tests instead of quietly sending the reader
    /// to the Settings tab.
    /// </remarks>
    public const int ModelTabIndex = 4;

    [ObservableProperty]
    private string _title = "TranscribeCppSharp";

    [ObservableProperty]
    private int _selectedTabIndex;

    public TranscriptionViewModel Transcription { get; }
    public StreamingViewModel Streaming { get; }
    public BatchViewModel Batch { get; }
    public ModelManagerViewModel ModelManager { get; }
    public SettingsViewModel Settings { get; }
    public HistoryViewModel History { get; }

    public MainWindowViewModel(
        TranscriptionViewModel transcription,
        StreamingViewModel streaming,
        BatchViewModel batch,
        ModelManagerViewModel modelManager,
        SettingsViewModel settings,
        HistoryViewModel history)
    {
        Transcription = transcription;
        Streaming = streaming;
        Batch = batch;
        ModelManager = modelManager;
        Settings = settings;
        History = history;

        // The three pickers each offer a way out to the catalogue, and none of them
        // can reach the window — they are constructed before it and hold no reference
        // to it. The event is what carries "take me to the models" upward, so the
        // decision about which tab that is stays in one place.
        Transcription.OpenModelManagerRequested += ShowModelManager;
        Transcription.OpenModelsForRoleRequested += ShowModelsForRole;
        Streaming.OpenModelManagerRequested += ShowModelManager;
        Batch.OpenModelManagerRequested += ShowModelManager;

        // Opening a saved run is the window's job: the History tab does not hold the
        // Transcription tab, and the tab switch is the same decision as the one above.
        History.OpenRequested += OpenHistoryEntry;
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
    /// entry. The History tab is re-read the same way, because a run saves an entry
    /// while that tab is not visible.
    /// </remarks>
    partial void OnSelectedTabIndexChanged(int value)
    {
        Transcription.RefreshModels();
        Streaming.RefreshModels();
        Batch.RefreshModels();
        History.Refresh();
    }

    private void ShowModelManager() => SelectedTabIndex = ModelTabIndex;

    /// <summary>
    /// Opens the catalogue already filtered to a role, which is the question the
    /// Transcription tab's buttons are asking: the goal's role, or diarization for
    /// the dedicated-diarizer step. The filter is the same one the Models tab
    /// exposes, not a second list; the other filters are cleared so a previous
    /// search or "on disk only" cannot hide the answer.
    /// </summary>
    private void ShowModelsForRole(string roleKey)
    {
        ModelManager.ClearFiltersCommand.Execute(null);
        ModelManager.RoleFilter = ModelManager.Roles
            .FirstOrDefault(role => string.Equals(role.Key, roleKey, StringComparison.Ordinal));
        SelectedTabIndex = ModelTabIndex;
    }

    /// <summary>
    /// Loads a saved run and shows it in the Transcription tab. A file that has been
    /// deleted or damaged is reported and dropped from the list instead of opening an
    /// empty tab.
    /// </summary>
    private void OpenHistoryEntry(HistoryEntry entry)
    {
        TranscriptionResult? result = TranscriptionHistory.Load(entry);
        if (result is null)
        {
            History.StatusMessage = "That entry could not be read; it may have been deleted or damaged.";
            History.Refresh();
            return;
        }

        Transcription.Restore(result, entry);
        SelectedTabIndex = TranscriptionTabIndex;
    }
}
