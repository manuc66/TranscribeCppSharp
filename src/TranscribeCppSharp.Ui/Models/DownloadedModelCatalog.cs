using TranscribeCppSharp.Models;

namespace TranscribeCppSharp.Ui.Models;

/// <summary>
/// What the model pickers offer: the aliases that are actually on disk.
/// </summary>
/// <remarks>
/// The three pickers (Transcription, Streaming, Batch) used to list the whole
/// catalogue, so a first run showed 72 entries of which at most one could be used
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
    /// One <c>File.Exists</c> per entry — 72 on a full cache. Cheap enough to run on
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
}
