using System.Linq;
using TranscribeCppSharp.Models;
using TranscribeCppSharp.SharedSource;

namespace TranscribeCppSharp.UI.Models;

/// <summary>
/// What the model pickers offer: the aliases that are actually on disk.
/// </summary>
/// <remarks>
/// The three pickers (Transcription, Streaming, Batch) used to list the whole
/// catalogue, so a first run showed 76 entries of which at most one could be used
/// without a download. Listing only what is cached makes the list mean something,
/// and moves the decision to the Models tab — which is where the hint below sends
/// the reader.
/// <para>
/// Shared rather than written three times so the wording of the hint cannot drift
/// between tabs: the same sentence, wherever the picker is.
/// </para>
/// </remarks>
public static class DownloadedModelCatalog
{
    /// <summary>
    /// Aliases already downloaded, in manifest order.
    /// </summary>
    /// <remarks>
    /// One <c>File.Exists</c> per entry — 76 on a full cache. Cheap enough to run on
    /// a tab change, which is the only place it runs outside startup: the list is
    /// built once when the window is created, and a model downloaded afterwards
    /// would otherwise never appear.
    /// </remarks>
    public static IReadOnlyList<string> Aliases()
    {
        List<string> aliases = new();
        foreach (ModelDescriptor descriptor in ModelStore.Catalog)
        {
            if (ModelStore.IsCached(descriptor))
            {
                aliases.Add(descriptor.Alias);
            }
        }

        return aliases;
    }

    /// <summary>
    /// Downloaded aliases that serve only the DIARIZE role — candidates for the
    /// diarizer that is paired with a transcriber which cannot attribute speakers.
    /// </summary>
    /// <remarks>
    /// Read from upstream's catalog (diarize supported, transcribe not), because a
    /// model's roles are only known once it is loaded; the run confirms the role
    /// before using it. Only one model qualifies today (Sortformer), but the list is
    /// derived rather than written down.
    /// </remarks>
    public static IReadOnlyList<string> DiarizerAliases()
    {
        List<string> aliases = new();
        foreach (ModelDescriptor descriptor in ModelStore.Catalog)
        {
            if (!ModelStore.IsCached(descriptor))
            {
                continue;
            }

            Dictionary<string, UpstreamCatalog.Capability>? caps =
                UpstreamCatalog.Read(descriptor.Alias)?.Capabilities;
            if (caps is null
                || !caps.TryGetValue("diarize", out UpstreamCatalog.Capability? diarize) || !diarize.Supported
                || !caps.TryGetValue("transcribe", out UpstreamCatalog.Capability? transcribe) || transcribe.Supported)
            {
                continue;
            }

            aliases.Add(descriptor.Alias);
        }

        return aliases;
    }

    /// <summary>
    /// What sits under the picker, saying what is on disk and where to get more.
    /// </summary>
    /// <param name="onDisk">How many aliases <see cref="Aliases"/> returned.</param>
    /// <param name="catalogued">How many the manifest lists.</param>
    /// <returns>Never empty: an empty cache still has to say where to go.</returns>
    /// <remarks>
    /// Counts, not recommendations. This project publishes no comparison between the
    /// models, so the hint cannot say which one to fetch — only that they are there.
    /// </remarks>
    public static string Hint(int onDisk, int catalogued)
        => onDisk == 0
            ? "Nothing on disk yet. The Models tab downloads one."
            : $"{onDisk} of {catalogued} on disk — the Models tab downloads more.";

    /// <summary>
    /// What sits under the streaming picker, which offers only models that may
    /// stream.
    /// </summary>
    /// <param name="offered">How many of the downloaded aliases may stream.</param>
    /// <param name="onDisk">How many aliases are downloaded in total.</param>
    /// <returns>Never empty: an empty picker still has to say where to go.</returns>
    /// <remarks>
    /// The count is the offer, not what is on disk: a cache holding only models
    /// that cannot stream would read "0 of 1" with no reason, so the two empty
    /// cases are told apart — nothing downloaded, and nothing downloaded that
    /// streams — and both name the capability filter that gets you there.
    /// </remarks>
    public static string StreamingHint(int offered, int onDisk)
        => offered > 0
            ? $"{offered} of {onDisk} on disk can stream — the Models tab downloads more."
            : onDisk == 0
                ? "Nothing on disk yet. The Models tab can filter on the 'streaming' capability."
                : "No streaming model on disk. The Models tab can filter on the 'streaming' capability.";

    /// <summary>
    /// Which alias a picker selects.
    /// </summary>
    /// <param name="current">The picker's present selection.</param>
    /// <param name="offered">The aliases the picker offers, in catalogue order.</param>
    /// <param name="remembered">The alias this tab last ran, or null.</param>
    /// <returns>An alias from <paramref name="offered"/>, or empty when there are none.</returns>
    /// <remarks>
    /// The current selection wins so that a rebuild after a download or a delete
    /// does not move a choice the reader already made; what was last run is the
    /// fallback for a fresh start, and the first alias is the last resort. A
    /// remembered alias that is no longer offered — deleted, or not streamable —
    /// is ignored rather than reselected.
    /// </remarks>
    public static string Selection(string current, IReadOnlyList<string> offered, string? remembered)
        => offered.Count == 0
            ? string.Empty
            : offered.Contains(current)
                ? current
                : remembered is not null && offered.Contains(remembered)
                    ? remembered
                    : offered[0];
}
