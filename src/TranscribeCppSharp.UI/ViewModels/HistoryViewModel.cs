using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TranscribeCppSharp.UI.Models;
using TranscribeCppSharp.UI.Services;

namespace TranscribeCppSharp.UI.ViewModels;

/// <summary>
/// The History tab: what finished runs were saved locally, with open and delete.
/// </summary>
/// <remarks>
/// The list is rebuilt from disk rather than kept in memory, so the tab reflects
/// what is actually there after a delete made outside the app. A saved entry is
/// self-contained: opening one does not need the original audio file.
/// </remarks>
public partial class HistoryViewModel : ObservableObject
{
    [ObservableProperty]
    private HistoryEntry? _selectedEntry;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>The saved runs, newest first.</summary>
    public ObservableCollection<HistoryEntry> Entries { get; } = new();

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

        Entries.Clear();
        foreach (HistoryEntry entry in TranscriptionHistory.List())
        {
            Entries.Add(entry);
        }

        // Entries are rebuilt from disk, so the old instance is never the same
        // object as the new one; match by file path instead, so the reader's place
        // survives a refresh.
        SelectedEntry = current is not null
            ? Entries.FirstOrDefault(e => e.FilePath == current.FilePath) ?? Entries.FirstOrDefault()
            : Entries.FirstOrDefault();
        StatusMessage = Entries.Count == 0
            ? "No saved transcriptions yet. A finished run is saved here automatically."
            : $"{Entries.Count} saved transcription(s).";
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

    /// <summary>Deletes the selected entry's file, then re-reads the list.</summary>
    [RelayCommand]
    private void Delete()
    {
        if (SelectedEntry is not { } entry)
        {
            StatusMessage = "Select a saved transcription first.";
            return;
        }

        try
        {
            TranscriptionHistory.Delete(entry);
            Refresh();
            StatusMessage = "Deleted.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusMessage = $"Could not delete the entry: {ex.Message}";
        }
    }
}
