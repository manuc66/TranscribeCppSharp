using System.Text.Json;
using System.Text.Json.Serialization;
using TranscribeCppSharp.Models;
using TranscribeCppSharp.UI.Models;

namespace TranscribeCppSharp.UI.Services;

/// <summary>
/// The few things the app remembers between runs.
/// </summary>
/// <remarks>
/// One small JSON file beside the log, under the same directory the Settings tab
/// names. It holds the model each tab last ran, so opening the app reselects the
/// model you were using instead of the first one alphabetically. Only the last
/// <em>run</em> is recorded — picking a model in the list and not running it is
/// not a use.
/// <para>
/// Read and written best-effort: a missing, unreadable or unwritable file falls
/// back to the picker's own first choice rather than failing the app. Nothing
/// here is a claim about the model, only a note of what was run.
/// </para>
/// </remarks>
public static class UiSettings
{
    private static readonly object Gate = new();

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Key for the Transcription tab's last-run model.</summary>
    public const string TranscriptionTab = "transcription";

    /// <summary>Key for the Streaming tab's last-run model.</summary>
    public const string StreamingTab = "streaming";

    /// <summary>Key for the Batch tab's last-run model.</summary>
    public const string BatchTab = "batch";

    /// <summary>
    /// The export options last saved, or the defaults when the section is absent
    /// or a value cannot mean anything.
    /// </summary>
    /// <remarks>
    /// The range checks are the ones the controls enforce (threshold 1–60 s,
    /// interval 0–600 s). A hand-edited file therefore falls back to the default
    /// for the value it got wrong rather than making the export do something the
    /// UI cannot show.
    /// </remarks>
    public static ExportSettings LoadExportSettings()
    {
        lock (Gate)
        {
            ExportSection? section = Read().Export;
            if (section is null)
            {
                return DefaultExportSettings;
            }

            int threshold = section.PauseThresholdSeconds is >= 1 and <= 60
                ? section.PauseThresholdSeconds
                : DefaultExportSettings.PauseThresholdSeconds;
            int interval = section.TimestampIntervalSeconds is >= 0 and <= 600
                ? section.TimestampIntervalSeconds
                : DefaultExportSettings.TimestampIntervalSeconds;

            return new ExportSettings(section.MarkPauses, threshold, interval);
        }
    }

    /// <summary>Records the export options, keeping whatever else the file holds.</summary>
    public static void SaveExportSettings(ExportSettings settings)
    {
        lock (Gate)
        {
            Document document = Read();
            document.Export = new ExportSection
            {
                MarkPauses = settings.MarkPauses,
                PauseThresholdSeconds = settings.PauseThresholdSeconds,
                TimestampIntervalSeconds = settings.TimestampIntervalSeconds,
            };
            Write(document);
        }
    }

    /// <summary>The options before anything was chosen.</summary>
    public static ExportSettings DefaultExportSettings { get; } = new(false, 1, 60);

    /// <summary>
    /// The last measurement recorded for a run configuration, or null when that
    /// configuration has not run on this machine.
    /// </summary>
    /// <param name="key">A key from <see cref="MemoryPreflight.RunKey"/>.</param>
    public static MemoryObservation? LoadMemoryObservation(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return null;
        }

        lock (Gate)
        {
            if (!Read().Memory.TryGetValue(key, out MemoryRecord? record))
            {
                return null;
            }

            return new MemoryObservation(
                record.DeviceName, record.TotalBytes, record.MinimumFreeBytes, record.MeasuredAt);
        }
    }

    /// <summary>Records what one run observed about its device's free memory.</summary>
    /// <param name="key">A key from <see cref="MemoryPreflight.RunKey"/>.</param>
    /// <param name="observation">The measurement; ignored when the key is empty.</param>
    public static void SaveMemoryObservation(string key, MemoryObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);

        if (string.IsNullOrEmpty(key))
        {
            return;
        }

        lock (Gate)
        {
            Document document = Read();
            document.Memory[key] = new MemoryRecord
            {
                DeviceName = observation.DeviceName,
                TotalBytes = observation.TotalBytes,
                MinimumFreeBytes = observation.MinimumFreeBytes,
                MeasuredAt = observation.MeasuredAt,
            };
            Write(document);
        }
    }

    /// <summary>
    /// Where the settings are written. Null means the default location under the
    /// app's cache directory; tests point it at a temporary file.
    /// </summary>
    public static string? FilePathOverride { get; set; }

    /// <summary>The settings file's path.</summary>
    public static string FilePath => FilePathOverride ?? DefaultPath();

    /// <summary>
    /// The alias <paramref name="tab"/> last ran, or null when it never has.
    /// </summary>
    /// <param name="tab">One of the tab keys on this class.</param>
    public static string? LastModel(string tab)
    {
        lock (Gate)
        {
            return Read().LastModels.TryGetValue(tab, out string? alias)
                && !string.IsNullOrEmpty(alias)
                    ? alias
                    : null;
        }
    }

    /// <summary>
    /// Records the alias a run started with. An empty alias is ignored, so a tab
    /// that starts nothing does not erase what was there.
    /// </summary>
    /// <param name="tab">One of the tab keys on this class.</param>
    /// <param name="alias">The alias the run resolved, as the picker spells it.</param>
    public static void SetLastModel(string tab, string alias)
    {
        if (string.IsNullOrEmpty(alias))
        {
            return;
        }

        lock (Gate)
        {
            Document document = Read();
            document.LastModels[tab] = alias;
            Write(document);
        }
    }

    /// <summary><c>&lt;cache&gt;/../ui-settings.json</c>, beside the log.</summary>
    private static string DefaultPath()
        => Path.Combine(
            Path.GetDirectoryName(ModelStore.CacheRoot())!,
            "ui-settings.json");

    private static Document Read()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<Document>(File.ReadAllText(FilePath), Json) ?? new Document()
                : new Document();
        }
        catch (Exception)
        {
            // A damaged settings file is not worth failing over: the picker falls
            // back to its own first choice.
            return new Document();
        }
    }

    private static void Write(Document document)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(document, Json));
        }
        catch (Exception)
        {
            // Losing the preference must not become the failure the reader sees.
        }
    }

    /// <summary>What the file holds. A shape of its own, never read as anything else.</summary>
    private sealed class Document
    {
        [JsonPropertyName("lastModels")]
        public Dictionary<string, string> LastModels { get; init; } = new(StringComparer.Ordinal);

        [JsonPropertyName("export")]
        public ExportSection? Export { get; set; }

        [JsonPropertyName("memory")]
        public Dictionary<string, MemoryRecord> Memory { get; init; } = new(StringComparer.Ordinal);
    }

    /// <summary>One measured run, as it sits on disk.</summary>
    private sealed class MemoryRecord
    {
        [JsonPropertyName("deviceName")]
        public string DeviceName { get; init; } = string.Empty;

        [JsonPropertyName("totalBytes")]
        public ulong TotalBytes { get; init; }

        [JsonPropertyName("minimumFreeBytes")]
        public ulong MinimumFreeBytes { get; init; }

        [JsonPropertyName("measuredAt")]
        public DateTimeOffset MeasuredAt { get; init; }
    }

    /// <summary>The Transcription tab's export options as they sit on disk.</summary>
    /// <remarks>
    /// The initializers are the defaults: a file written before this section
    /// existed, or one missing a property, reads every absent value as the
    /// default instead of zero.
    /// </remarks>
    private sealed class ExportSection
    {
        [JsonPropertyName("markPauses")]
        public bool MarkPauses { get; init; }

        [JsonPropertyName("pauseThresholdSeconds")]
        public int PauseThresholdSeconds { get; init; } = 1;

        [JsonPropertyName("timestampIntervalSeconds")]
        public int TimestampIntervalSeconds { get; init; } = 60;
    }
}
