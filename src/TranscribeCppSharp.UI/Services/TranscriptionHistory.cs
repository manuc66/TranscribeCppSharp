using System.Text.Json;
using System.Text.Json.Serialization;
using TranscribeCppSharp.Models;
using TranscribeCppSharp.UI.Models;

namespace TranscribeCppSharp.UI.Services;

/// <summary>
/// The local history: one JSON file per finished transcription, under
/// <c>&lt;cache&gt;/../history</c>, beside the log and the settings.
/// </summary>
/// <remarks>
/// Nothing leaves the machine, and nothing is inferred: the file holds the run's
/// result and the few facts needed to name it (audio path, model, language). It
/// is self-contained, so an entry can be reopened after the audio file has been
/// moved or deleted. A file that cannot be parsed is skipped rather than taking
/// the whole list down.
/// <para>
/// Writes go to a temporary file and are then moved into place, so a crash
/// mid-write leaves no half-written entry for the next launch to skip.
/// </para>
/// </remarks>
public static class TranscriptionHistory
{
    private static readonly object Gate = new();

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Where the entries are written. Null means the default beside the log;
    /// tests point it at a temporary directory.
    /// </summary>
    public static string? DirectoryOverride { get; set; }

    /// <summary>The history directory's path.</summary>
    public static string DirectoryPath
        => DirectoryOverride
        ?? Path.Combine(Path.GetDirectoryName(ModelStore.CacheRoot())!, "history");

    /// <summary>
    /// Saves one finished run and returns its entry.
    /// </summary>
    /// <param name="audioPath">The audio file the run read.</param>
    /// <param name="modelAlias">The model the run used, as the picker spells it.</param>
    /// <param name="language">The language the run reports, or was asked for.</param>
    /// <param name="diarize">Whether attribution was requested, as the exports spell it.</param>
    /// <param name="windowSeconds">The window the run used.</param>
    /// <param name="result">The transcript to keep.</param>
    /// <param name="savedAt">When to record it; now when omitted.</param>
    /// <exception cref="IOException">The directory or file could not be written.</exception>
    public static HistoryEntry Save(
        string audioPath,
        string modelAlias,
        string language,
        string diarize,
        int windowSeconds,
        TranscriptionResult result,
        DateTimeOffset? savedAt = null)
    {
        ArgumentNullException.ThrowIfNull(result);

        DateTimeOffset stamp = savedAt ?? DateTimeOffset.Now;
        var document = new SavedRun
        {
            SavedAt = stamp,
            AudioPath = audioPath,
            ModelAlias = modelAlias,
            Language = language,
            Diarize = diarize,
            WindowSeconds = windowSeconds,
            Result = result,
        };

        lock (Gate)
        {
            string path = UniquePath(stamp);

            // The directory is owner-only and the file is written 0600: a saved
            // transcript is the audio's content, and the default 0644 let every
            // local account read it.
            PrivateFile.WriteAllText(path, JsonSerializer.Serialize(document, Json));

            return Entry(path, document);
        }
    }

    /// <summary>Every readable entry, newest first. A broken file is skipped.</summary>
    /// <remarks>
    /// A directory that cannot be enumerated is reported as empty rather than
    /// taking the History tab down: the tab exists to show what is readable, and a
    /// permission change on the folder is not a reason for the app to fail. A
    /// failure part-way through keeps the entries read so far.
    /// </remarks>
    public static IReadOnlyList<HistoryEntry> List()
    {
        lock (Gate)
        {
            if (!Directory.Exists(DirectoryPath))
            {
                return [];
            }

            var entries = new List<HistoryEntry>();
            try
            {
                // One-time migration for entries an earlier version wrote 0644:
                // every transcript already on disk becomes owner-only when the
                // list is read, not only when it is saved again.
                foreach (string path in Directory.EnumerateFiles(DirectoryPath, "*.json"))
                {
                    PrivateFile.EnsureFile(path);

                    SavedRun? document = Read(path);
                    if (document?.Result is not null)
                    {
                        entries.Add(Entry(path, document));
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Return what was readable: an unreadable folder must not become
                // a failure the History tab has to report and recover from.
            }

            return entries
                .OrderByDescending(e => e.SavedAt)
                .ThenByDescending(e => e.FilePath, StringComparer.Ordinal)
                .ToList();
        }
    }

    /// <summary>The run's result, or null when the file is gone or unreadable.</summary>
    public static TranscriptionResult? Load(HistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        lock (Gate)
        {
            return Read(entry.FilePath)?.Result;
        }
    }

    /// <summary>Deletes the entry's file. A file already gone is not an error.</summary>
    public static void Delete(HistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        lock (Gate)
        {
            try
            {
                if (File.Exists(entry.FilePath))
                {
                    File.Delete(entry.FilePath);
                }
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                // Removed by someone else between the check and the delete; the
                // postcondition (the file is gone) still holds.
            }
        }
    }

    /// <summary>
    /// One file name per save, with a suffix when two runs land in the same
    /// millisecond. The name sorts chronologically, which is what the list uses
    /// as its tie-break.
    /// </summary>
    private static string UniquePath(DateTimeOffset stamp)
    {
        string baseName = stamp.ToLocalTime().ToString("yyyyMMdd-HHmmss-fff", System.Globalization.CultureInfo.InvariantCulture);
        string path = Path.Combine(DirectoryPath, baseName + ".json");
        for (int suffix = 1; Path.Exists(path); suffix++)
        {
            path = Path.Combine(DirectoryPath, $"{baseName}-{suffix}.json");
        }

        return path;
    }

    private static HistoryEntry Entry(string path, SavedRun document)
        => new()
        {
            FilePath = path,
            SavedAt = document.SavedAt,
            AudioPath = document.AudioPath,
            ModelAlias = document.ModelAlias,
            Language = document.Language,
            Diarize = document.Diarize,
            WindowSeconds = document.WindowSeconds,
            SegmentCount = document.Result?.Segments.Count ?? 0,
            Duration = document.Result?.Duration,
            FileSizeBytes = SizeOf(path),
        };

    /// <summary>
    /// The file's size, or 0 when it vanished or cannot be read. Used for the
    /// tab's total; a size is not worth failing a list over.
    /// </summary>
    private static long SizeOf(string path)
    {
        try
        {
            var file = new FileInfo(path);
            return file.Exists ? file.Length : 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static SavedRun? Read(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<SavedRun>(File.ReadAllText(path), Json)
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A damaged or unreadable entry is left out of the list rather than
            // failing the tab. Deleting it from the folder is the reader's call.
            return null;
        }
    }

    /// <summary>The file's shape. A shape of its own, never read as anything else.</summary>
    private sealed class SavedRun
    {
        [JsonPropertyName("savedAt")]
        public DateTimeOffset SavedAt { get; init; }

        [JsonPropertyName("audioPath")]
        public string AudioPath { get; init; } = string.Empty;

        [JsonPropertyName("modelAlias")]
        public string ModelAlias { get; init; } = string.Empty;

        [JsonPropertyName("language")]
        public string Language { get; init; } = string.Empty;

        [JsonPropertyName("diarize")]
        public string Diarize { get; init; } = string.Empty;

        [JsonPropertyName("windowSeconds")]
        public int WindowSeconds { get; init; }

        [JsonPropertyName("result")]
        public TranscriptionResult? Result { get; init; }
    }
}
