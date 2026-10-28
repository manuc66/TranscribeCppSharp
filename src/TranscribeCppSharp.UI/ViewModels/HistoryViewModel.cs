using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TranscribeCppSharp.Models;
using TranscribeCppSharp.UI.Models;
using TranscribeCppSharp.UI.Services;

namespace TranscribeCppSharp.UI.ViewModels;

/// <summary>
/// The History tab: what finished runs were saved locally, with open and delete.
/// </summary>
/// <remarks>
/// <para>
/// The list is rebuilt from disk rather than kept in memory, so the tab reflects
/// what is actually there after a delete made outside the app. A saved entry is
/// self-contained: opening one does not need the original audio file.
/// </para>
/// <para>
/// Deleting one row deletes it at once, like the Models tab. Deleting several —
/// a selection or the whole history — asks first: a transcript cannot be
/// downloaded again, so the only way back is re-transcribing audio that may have
/// been moved. The confirmation is inline (see <see cref="IsConfirmingDelete"/>)
/// rather than a modal, because the app has no dialog infrastructure and this
/// keeps the state testable.
/// </para>
/// </remarks>
public partial class HistoryViewModel : ObservableObject
{
    private Action? pendingDelete;

    [ObservableProperty]
    private HistoryEntry? _selectedEntry;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>Whether the inline confirmation is showing.</summary>
    [ObservableProperty]
    private bool _isConfirmingDelete;

    /// <summary>What the inline confirmation asks, in one sentence.</summary>
    [ObservableProperty]
    private string _confirmationMessage = string.Empty;

    /// <summary>The saved runs, newest first.</summary>
    public ObservableCollection<HistoryEntry> Entries { get; } = new();

    /// <summary>
    /// The rows selected in the grid, for a bulk delete. Bound to the grid's own
    /// selection; <see cref="SelectedEntry"/> stays the one Open acts on.
    /// </summary>
    public ObservableCollection<HistoryEntry> SelectedEntries { get; } = new();

    /// <summary>Where the entries are written, shown in the tab.</summary>
    public string HistoryPath { get; } = TranscriptionHistory.DirectoryPath;

    /// <summary>Raised when the reader asks to open the selected run.</summary>
    public event Action<HistoryEntry>? OpenRequested;

    public HistoryViewModel() => Refresh();

    /// <summary>Re-reads the history directory.</summary>
    [RelayCommand]
    internal void Refresh()
    {
        // Keep the reader's place: rebuilding the list from disk must not move the
        // selection to the first entry every time the tabs change. The same
        // TwoWay-binding caveat as the model pickers applies to Entries, so the
        // current selection is captured before the collection is cleared.
        HistoryEntry? current = SelectedEntry;
        string[] selectedPaths = SelectedEntries.Select(e => e.FilePath).ToArray();

        Entries.Clear();
        foreach (HistoryEntry entry in TranscriptionHistory.List())
        {
            Entries.Add(entry);
        }

        // The rows are new instances after a rebuild, so the selection is restored
        // by path: the reader keeps both their place and their multi-selection.
        SelectedEntries.Clear();
        foreach (string path in selectedPaths)
        {
            if (Entries.FirstOrDefault(e => e.FilePath == path) is { } again)
            {
                SelectedEntries.Add(again);
            }
        }

        SelectedEntry = current is not null
            ? Entries.FirstOrDefault(e => e.FilePath == current.FilePath) ?? Entries.FirstOrDefault()
            : Entries.FirstOrDefault();

        long bytes = Entries.Sum(e => e.FileSizeBytes);
        StatusMessage = Entries.Count == 0
            ? "No saved transcriptions yet. A finished run is saved here automatically."
            : $"{Entries.Count} saved transcription(s), {ModelSizeFormat.Format(bytes)} on disk.";
    }

    /// <summary>
    /// Replaces <see cref="SelectedEntries"/> with the grid's current selection.
    /// </summary>
    /// <param name="items">The selected rows, as the control reports them.</param>
    internal void UpdateSelection(IEnumerable<HistoryEntry> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        SelectedEntries.Clear();
        foreach (HistoryEntry entry in items)
        {
            SelectedEntries.Add(entry);
        }
    }

    /// <summary>Asks the window to load the selected run into the Transcription tab.</summary>
    [RelayCommand]
    private void Open()
    {
        if (SelectedEntry is { } entry)
        {
            OpenRequested?.Invoke(entry);
        }
        else
        {
            StatusMessage = "Select a saved transcription first.";
        }
    }

    /// <summary>Deletes the selected row, without asking: one row is one click.</summary>
    [RelayCommand]
    private void Delete()
    {
        if (SelectedEntry is not { } entry)
        {
            StatusMessage = "Select a saved transcription first.";
            return;
        }

        DeleteEntries([entry]);
    }

    /// <summary>
    /// Deletes every selected row, asking first when there is more than one.
    /// </summary>
    [RelayCommand]
    private void DeleteSelected()
    {
        HistoryEntry[] selected = SelectedEntries.Count > 0
            ? [.. SelectedEntries]
            : SelectedEntry is { } single ? [single] : [];

        if (selected.Length == 0)
        {
            StatusMessage = "Select a saved transcription first.";
            return;
        }

        if (selected.Length == 1)
        {
            DeleteEntries(selected);
            return;
        }

        AskConfirmation(
            $"Delete {selected.Length} saved transcriptions?",
            () => DeleteEntries(selected));
    }

    /// <summary>Deletes the whole history, after a confirmation.</summary>
    [RelayCommand]
    private void DeleteAll()
    {
        if (Entries.Count == 0)
        {
            StatusMessage = "Nothing to delete.";
            return;
        }

        HistoryEntry[] all = [.. Entries];
        AskConfirmation(
            $"Delete all {all.Length} saved transcription(s)?",
            () => DeleteEntries(all));
    }

    /// <summary>Carries out the pending bulk delete.</summary>
    [RelayCommand]
    private void ConfirmDelete()
    {
        Action? action = pendingDelete;
        ClearConfirmation();
        action?.Invoke();
    }

    /// <summary>Drops the pending bulk delete and says so.</summary>
    [RelayCommand]
    private void CancelDelete()
    {
        ClearConfirmation();
        StatusMessage = "Nothing was deleted.";
    }

    private void AskConfirmation(string question, Action action)
    {
        pendingDelete = action;
        ConfirmationMessage = question + " This cannot be undone.";
        IsConfirmingDelete = true;
    }

    private void ClearConfirmation()
    {
        pendingDelete = null;
        IsConfirmingDelete = false;
        ConfirmationMessage = string.Empty;
    }

    private void DeleteEntries(HistoryEntry[] entries)
    {
        int removed = 0;
        string? firstError = null;

        foreach (HistoryEntry entry in entries)
        {
            try
            {
                TranscriptionHistory.Delete(entry);
                removed++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // One file that cannot be removed must not stop the others; the
                // status line names the first failure instead of hiding it.
                firstError ??= ex.Message;
            }
        }

        ClearConfirmation();
        Refresh();

        StatusMessage = firstError is null
            ? $"Deleted {removed} saved transcription(s)."
            : $"Deleted {removed} of {entries.Length}; the rest failed: {firstError}";
    }
}
