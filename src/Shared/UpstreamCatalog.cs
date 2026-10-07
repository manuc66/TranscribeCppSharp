using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TranscribeCppSharp.Shared;

/// <summary>
/// Reads upstream's pinned model catalog out of the assembly that embedded it.
/// </summary>
/// <remarks>
/// Shared source, not a shared project: the CLI and the GUI both embed
/// <c>catalog.zip</c> and both need the same twenty lines, while the wrapper must
/// not — it is installed by every library consumer, and this data is for the two
/// front ends that display it. A linked file gives one implementation with no
/// package, no public type and no dependency added to either assembly.
/// <para>
/// The resource is read per entry: opening one model's record decompresses that
/// record only, which is why the catalog ships as a zip rather than as the
/// SQLite database upstream releases — that one needs a reader, and a reader in
/// .NET means a native binary.
/// </para>
/// </remarks>
internal static class UpstreamCatalog
{
    /// <summary>Embedded as this name by both front ends.</summary>
    private const string ResourceName = "catalog.zip";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Upstream's record for one model, or null when it has none.
    /// </summary>
    /// <remarks>
    /// Null is a real answer, not an error: our aliases are derived from the
    /// repository name and theirs are not, so one of the seventy-two only matches
    /// through <see cref="UpstreamName"/>. What is never done is inventing a record
    /// to fill a detail pane — an absent entry leaves the pane with what the
    /// manifest already said.
    /// </remarks>
    public static Record? Read(string alias)
    {
        try
        {
            using Stream? stream = typeof(UpstreamCatalog).Assembly
                .GetManifestResourceStream(ResourceName);
            if (stream is null)
            {
                return null;
            }

            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
            ZipArchiveEntry? entry = zip.GetEntry($"{UpstreamName(alias)}.json");
            if (entry is null)
            {
                return null;
            }

            using StreamReader reader = new(entry.Open());
            return JsonSerializer.Deserialize<Record>(reader.ReadToEnd(), Json);
        }
        catch (InvalidDataException)
        {
            // A damaged or missing catalog must not take a transcription down.
            return null;
        }
    }

    /// <summary>Our alias as upstream spells it. Only one of the seventy-two differs.</summary>
    private static string UpstreamName(string alias)
        => string.Equals(alias, "sensevoicesmall", StringComparison.Ordinal)
            ? "sensevoice-small"
            : alias;

    /// <summary>
    /// What upstream records about a model. Deliberately partial: their records
    /// carry accuracy and speed tables, and only what the front ends display is
    /// read here — nothing is fetched to be thrown away.
    /// </summary>
    public sealed record Record(
        string? Family,
        long Params,
        string? UpstreamRepo,
        string? UpstreamCommit,
        string[]? Languages,
        Dictionary<string, Capability>? Capabilities,
        List<Download>? Downloads,
        List<Accuracy>? AccuracyBenchmarks,
        Headline? HeadlineBenchmark)
    {
        public bool HasIdentity =>
            Family is not null || Params > 0 || UpstreamRepo is not null;
    }

    /// <summary>A capability with the flag that separates claimed from checked.</summary>
    public sealed record Capability(bool Supported, bool Verified);

    /// <summary>One published quantization of one model.</summary>
    public sealed record Download(string Quant, string Filename, long SizeBytes);

    /// <summary>One measured accuracy result, with its provenance.</summary>
    public sealed record Accuracy(
        string? Dataset,
        string? Split,
        string? Language,
        string? Quant,
        string? Metric,
        double? ErrPct,
        string? MeasurementProvenance);

    /// <summary>The result upstream says should be quoted for this model.</summary>
    public sealed record Headline(string? Dataset, string? Split, string? Language, string? Metric);
}