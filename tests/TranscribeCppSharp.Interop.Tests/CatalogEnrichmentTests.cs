using System.Globalization;
using TranscribeCppSharp.Models;
using Xunit;

namespace TranscribeCppSharp.Interop.Tests;

/// <summary>
/// The identity fields upstream records reach the manifest, and reach the reader.
/// </summary>
/// <remarks>
/// These replace what the project used to work out for itself — the family was a
/// guess from the repository name, which produces nothing at all for
/// <c>SenseVoiceSmall-gguf</c>. The point of the assertion is that the manifest is
/// now upstream's answer rather than a derivation, so a future regeneration that
/// silently drops the fields fails here instead of quietly reverting the grid and
/// <c>--model-info</c> to guesses.
/// </remarks>
public class CatalogEnrichmentTests
{
    [Fact]
    public void EveryModelDeclaresItsUpstreamIdentity()
    {
        string[] missingFamily = ModelStore.Catalog
            .Where(m => string.IsNullOrEmpty(m.Family))
            .Select(m => m.Alias).ToArray();
        string[] missingParams = ModelStore.Catalog
            .Where(m => m.Params <= 0)
            .Select(m => m.Alias).ToArray();
        string[] missingUpstream = ModelStore.Catalog
            .Where(m => string.IsNullOrEmpty(m.UpstreamRepo) || string.IsNullOrEmpty(m.UpstreamCommit))
            .Select(m => m.Alias).ToArray();

        Assert.True(missingFamily.Length == 0, $"no family: {string.Join(", ", missingFamily)}");
        Assert.True(missingParams.Length == 0, $"no parameter count: {string.Join(", ", missingParams)}");
        Assert.True(missingUpstream.Length == 0, $"no checkpoint: {string.Join(", ", missingUpstream)}");
    }

    /// <summary>
    /// The one model whose alias this project derives differently from upstream,
    /// and the reason the join is spelled out rather than assumed.
    /// </summary>
    [Fact]
    public void TheModelWhoseNameWeDeriveDifferentlyIsStillEnriched()
    {
        ModelDescriptor? model = ModelStore.Find("sensevoicesmall");
        Assert.NotNull(model);

        // Derived from "SenseVoiceSmall-gguf" this is "sensvoicesmall" and there is
        // no family to read; upstream records the variant as "sensevoice-small".
        Assert.Equal("sensevoice", model!.Family);
        Assert.True(model.Params > 0);
        Assert.StartsWith("FunAudioLLM/", model.UpstreamRepo!, StringComparison.Ordinal);
    }

    [Fact]
    public void ModelInfoPrintsTheUpstreamIdentity()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        Assert.True(ModelStore.Info("whisper-tiny", output, error));
        string text = output.ToString();

        // Not a fixture of the whole block — only that the lines are produced, and
        // produced with values read from the manifest rather than invented.
        Assert.Contains("family     : whisper", text, StringComparison.Ordinal);
        Assert.Contains("upstream   : openai/whisper-tiny @ 169d4a4", text, StringComparison.Ordinal);

        // Same format the command uses: this CLI follows the locale by design
        // (documented), so pinning an invariant separator here would be testing
        // something the tool deliberately does not do.
        ModelDescriptor model = ModelStore.Find("whisper-tiny")!;
        Assert.Contains(model.Params.ToString("N0", CultureInfo.CurrentCulture), text,
            StringComparison.Ordinal);
    }

    /// <summary>The fields ride along beside the pins, they do not replace them.</summary>
    [Fact]
    public void EnrichmentLeftTheDownloadPinsIntact()
    {
        foreach (ModelDescriptor model in ModelStore.Catalog)
        {
            Assert.False(string.IsNullOrWhiteSpace(model.Revision), $"{model.Alias}: no revision");
            Assert.False(string.IsNullOrWhiteSpace(model.Sha256), $"{model.Alias}: no sha256");
            Assert.Equal(64, model.Sha256.Length);
            Assert.True(model.Size > 0, $"{model.Alias}: no size");
        }
    }
}
