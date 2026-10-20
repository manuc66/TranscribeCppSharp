namespace TranscribeCppSharp.UI.Models;

/// <summary>
/// One saved run in the local history: what ran on what, and when. The result
/// itself stays in the JSON file; this is what the History tab's grid shows.
/// </summary>
public sealed class HistoryEntry
{
    /// <summary>Absolute path of the JSON file holding the run.</summary>
    public required string FilePath { get; init; }

    /// <summary>When the run was saved, in local time.</summary>
    public DateTimeOffset SavedAt { get; init; }

    /// <summary>The audio file the run read, as it was named at the time.</summary>
    public string AudioPath { get; init; } = string.Empty;

    /// <summary>The file name alone, for the grid.</summary>
    public string AudioName => Path.GetFileName(AudioPath);

    public string ModelAlias { get; init; } = string.Empty;

    public string Language { get; init; } = string.Empty;

    /// <summary>Whether attribution was requested, as the exports spell it.</summary>
    public string Diarize { get; init; } = string.Empty;

    /// <summary>The window the run used, in seconds.</summary>
    public int WindowSeconds { get; init; }

    public int SegmentCount { get; init; }

    /// <summary>The saved time as the grid shows it, in the machine's time zone.</summary>
    public string SavedAtDisplay
        => SavedAt.ToLocalTime().ToString(
            "yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
}
