using System.IO.Compression;
using System.Reflection;
using TranscribeCppSharp.Models;
using TranscribeCppSharp.Shared;
using TranscribeCppSharp.Ui.Models;
using Xunit;

namespace TranscribeCppSharp.Ui.Tests;

/// <summary>
/// The detail pane shows upstream's catalog, attributed, and shows nothing when
/// upstream has none.
/// </summary>
/// <remarks>
/// The GUI reads the same embedded zip the CLI does, through the same linked
/// reader. What is being asserted here is the boundary: these lines are their
/// records and their measurements, and a model without a record must leave them
/// blank rather than fill them with a dash or a zero that reads as a measurement.
/// </remarks>
public class UpstreamCatalogDetailTests
{
    private static ModelCatalogItem Item(string alias)
    {
        ModelDescriptor? descriptor = ModelStore.Find(alias);
        Assert.NotNull(descriptor);
        return new ModelCatalogItem(descriptor!);
    }

    [Fact]
    public void TheCatalogIsEmbeddedInTheGuiAssembly()
    {
        Assembly assembly = typeof(UpstreamCatalog).Assembly;
        Assert.Contains(assembly.GetManifestResourceNames(), n => n == "catalog.zip");

        using Stream? stream = assembly.GetManifestResourceStream("catalog.zip");
        Assert.NotNull(stream);
        using var zip = new ZipArchive(stream!, ZipArchiveMode.Read);
        Assert.Equal(72, zip.Entries.Count(e => !string.IsNullOrEmpty(e.Name)));
    }

    [Fact]
    public void TheDetailPaneCarriesUpstreamIdentity()
    {
        ModelCatalogItem item = Item("whisper-tiny");

        Assert.Contains("parameters", item.UpstreamDetail, StringComparison.Ordinal);
        Assert.Contains("openai/whisper-tiny", item.UpstreamDetail, StringComparison.Ordinal);
        Assert.Contains("169d4a4", item.UpstreamDetail, StringComparison.Ordinal);
        Assert.Contains("family whisper", item.UpstreamDetail, StringComparison.Ordinal);
    }

    [Fact]
    public void TheDetailPaneListsEveryPublishedQuantization()
    {
        // The manifest pins one. The others exist upstream and are named as such —
        // none has been fetched or verified here, which is what the tooltip says.
        ModelCatalogItem item = Item("whisper-tiny");

        Assert.StartsWith("Published:", item.QuantizationsDetail, StringComparison.Ordinal);
        Assert.Contains("Q4_K_M", item.QuantizationsDetail, StringComparison.Ordinal);
        Assert.Contains("Q5_K_M", item.QuantizationsDetail, StringComparison.Ordinal);
        Assert.Contains("F32", item.QuantizationsDetail, StringComparison.Ordinal);
    }

    [Fact]
    public void CapabilitiesKeepSupportedApartFromVerified()
    {
        ModelCatalogItem item = Item("whisper-tiny");

        Assert.Contains("transcribe", item.CapabilitiesDetail, StringComparison.Ordinal);
        Assert.DoesNotContain("diarize", item.CapabilitiesDetail, StringComparison.Ordinal);

        // Their own caveat must survive into the display: whisper's capabilities are
        // recorded as supported and unverified, and dropping the suffix would turn
        // their hedge into a claim.
        Assert.Contains("unverified upstream", item.CapabilitiesDetail, StringComparison.Ordinal);
    }

    /// <summary>The accuracy line is theirs, and says whose it is.</summary>
    [Fact]
    public void TheAccuracyLineIsAttributedUpstream()
    {
        ModelCatalogItem item = Item("whisper-tiny");

        Assert.Contains("upstream", item.HeadlineAccuracyDetail, StringComparison.Ordinal);
        Assert.Contains("%", item.HeadlineAccuracyDetail, StringComparison.Ordinal);
        Assert.Contains("librispeech", item.HeadlineAccuracyDetail, StringComparison.Ordinal);
        // The provenance is printed, including when it says the build was not
        // recorded — an absent engine_sha is part of what is known.
        Assert.Contains("legacy-published", item.HeadlineAccuracyDetail, StringComparison.Ordinal);
    }

    /// <summary>
    /// Upstream records every model in the manifest, so a blank pane would mean the
    /// reader is broken rather than the record missing.
    /// </summary>
    [Fact]
    public void EveryModelInTheManifestHasUpstreamDetail()
    {
        string[] blank = ModelStore.Catalog
            .Where(d =>
            {
                ModelCatalogItem item = new(d);
                return string.IsNullOrEmpty(item.UpstreamDetail)
                    || string.IsNullOrEmpty(item.QuantizationsDetail)
                    || string.IsNullOrEmpty(item.CapabilitiesDetail)
                    || string.IsNullOrEmpty(item.HeadlineAccuracyDetail);
            })
            .Select(d => d.Alias)
            .ToArray();

        Assert.True(blank.Length == 0,
            $"{blank.Length} model(s) with an incomplete detail pane: {string.Join(", ", blank.Take(10))}");
    }

    /// <summary>Nothing fabricated for an alias upstream does not know.</summary>
    [Fact]
    public void AnUnknownAliasProducesNoDetailAtAll()
    {
        var record = new ModelCatalogItem(new ModelDescriptor(
            Alias: "invented-alias",
            Repo: "someone/invented-gguf",
            Revision: "rev",
            License: "mit",
            LicenseUrl: "https://example.invalid",
            Quant: "Q8_0",
            File: "invented.gguf",
            Sha256: "00",
            Size: 1));

        Assert.Equal(string.Empty, record.UpstreamDetail);
        Assert.Equal(string.Empty, record.QuantizationsDetail);
        Assert.Equal(string.Empty, record.CapabilitiesDetail);
        Assert.Equal(string.Empty, record.HeadlineAccuracyDetail);
    }
}