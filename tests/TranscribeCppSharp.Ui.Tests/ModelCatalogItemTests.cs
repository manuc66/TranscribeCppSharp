using TranscribeCppSharp.Models;
using TranscribeCppSharp.Ui.Models;
using Xunit;

namespace TranscribeCppSharp.Ui.Tests;

public class ModelCatalogItemTests
{
    private static ModelCatalogItem Item()
        => new(new ModelDescriptor(
            Alias: "sample",
            Repo: "owner/sample-gguf",
            Revision: "rev",
            License: "mit",
            LicenseUrl: "https://example.invalid",
            Quant: "Q5_K_M",
            File: "sample.gguf",
            Sha256: "00",
            Size: 1));

    [Fact]
    public void DiarizationStartsUnknownAndSaysSo()
    {
        ModelCatalogItem item = Item();

        Assert.Equal(ModelCatalogItem.DiarizationSupport.Unknown, item.Diarization);
        Assert.Equal("?", item.DiarizationText);
        // Not on disk here, so the detail says so rather than claiming a check
        // that never ran.
        Assert.Equal("unknown, not downloaded", item.DiarizationDetail);
    }

    [Fact]
    public void DiarizationTextReflectsEachCheckedState()
    {
        ModelCatalogItem item = Item();

        item.SetDiarization(ModelCatalogItem.DiarizationSupport.Checking);
        Assert.Equal("checking…", item.DiarizationText);

        item.SetDiarization(ModelCatalogItem.DiarizationSupport.Supported);
        Assert.Equal("yes", item.DiarizationText);
        Assert.Contains("checked on this machine", item.DiarizationDetail);

        item.SetDiarization(ModelCatalogItem.DiarizationSupport.Unsupported);
        Assert.Equal("no", item.DiarizationText);
        Assert.Contains("checked on this machine", item.DiarizationDetail);
    }

    [Fact]
    public void ALoadFailureIsNotReportedAsNo()
    {
        ModelCatalogItem item = Item();

        item.SetDiarization(ModelCatalogItem.DiarizationSupport.LoadFailed);

        Assert.Equal("?", item.DiarizationText);
        Assert.Contains("could not be loaded", item.DiarizationDetail);
    }

    [Fact]
    public void SetDiarizationRaisesTheDisplayProperties()
    {
        ModelCatalogItem item = Item();
        var raised = new List<string?>();
        item.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        item.SetDiarization(ModelCatalogItem.DiarizationSupport.Supported);

        Assert.Contains(nameof(ModelCatalogItem.Diarization), raised);
        Assert.Contains(nameof(ModelCatalogItem.DiarizationText), raised);
        Assert.Contains(nameof(ModelCatalogItem.DiarizationDetail), raised);
    }
}
