using TranscribeCppSharp.Models;
using TranscribeCppSharp.Ui.Models;
using TranscribeCppSharp.Ui.ViewModels;
using Xunit;

namespace TranscribeCppSharp.Ui.Tests;

/// <summary>
/// What the three model-grid indicators say, and what they refuse to say.
/// </summary>
/// <remarks>
/// On disk became a yes or a no, WER appeared, and Diarization stopped showing a
/// question mark for models whose answer is recorded upstream. The common risk in
/// all three is a cell that states something its source does not support — a
/// cached size where none exists, an accuracy figure with no benchmark behind it,
/// or a capability claimed by someone who never checked it. Each test below is
/// about which of those the column is entitled to print.
/// </remarks>
public class ModelGridIndicatorTests
{
    private static ModelCatalogItem Item(string alias)
    {
        ModelDescriptor? descriptor = ModelStore.Find(alias);
        Assert.NotNull(descriptor);
        return new ModelCatalogItem(descriptor!);
    }

    [Fact]
    public void OnDiskIsAyesOrANo()
    {
        ModelCatalogItem item = Item("whisper-tiny");

        // Nothing in the temp cache in this test run, so: no.
        Assert.False(item.IsCached);
        Assert.Equal("no", item.OnDiskText);

        // What was given up by dropping the per-row size still exists twice: the
        // Size column beside it, and the total on the line above the grid. The
        // label on that line is XAML's, so what is asserted here is that the total
        // is still computed rather than silently dropped along with the column.
        Assert.True(item.Descriptor.Size > 0);
        Assert.False(string.IsNullOrWhiteSpace(item.SizeText));

        var viewModel = new TranscribeCppSharp.Ui.ViewModels.ModelManagerViewModel();
        Assert.False(string.IsNullOrWhiteSpace(viewModel.CacheSizeText));
    }

    [Fact]
    public void TheWerColumnCarriesAfigureAndSaysWhoseItIs()
    {
        ModelCatalogItem item = Item("whisper-tiny");

        Assert.NotEqual("-", item.WerText);
        Assert.Contains("%", item.WerText, StringComparison.Ordinal);

        // The tooltip is where the attribution lives: the column holds a number,
        // and a number with no source in sight is the thing to avoid.
        Assert.Contains("transcribe.cpp", item.WerDetail, StringComparison.Ordinal);
        Assert.Contains("librispeech", item.WerDetail, StringComparison.Ordinal);
        Assert.Contains("Not a measurement of your audio", item.WerDetail, StringComparison.Ordinal);
    }

    /// <summary>
    /// The figure is for the quantization this manifest pins, not the first row
    /// upstream happens to publish.
    /// </summary>
    [Fact]
    public void TheWerFigureIsForTheQuantizationThatIsPinned()
    {
        ModelCatalogItem item = Item("whisper-tiny");

        Assert.Equal("Q5_K_M", item.Descriptor.Quant);
        Assert.Contains("Q5_K_M", item.WerDetail, StringComparison.Ordinal);
        Assert.DoesNotContain("F32 —", item.WerDetail, StringComparison.Ordinal);
    }

    [Fact]
    public void DiarizationComesFromUpstreamWhenNothingHasBeenCheckedHere()
    {
        // A local check is what both these cells would otherwise need, and neither
        // has been loaded in this test — so both come from the catalog.
        ModelCatalogItem whisper = Item("whisper-tiny");
        Assert.Equal(ModelCatalogItem.DiarizationSupport.Unknown, whisper.Diarization);
        Assert.Equal("no", whisper.DiarizationText);

        ModelCatalogItem moss = Item("moss-transcribe-diarize");
        Assert.Equal(ModelCatalogItem.DiarizationSupport.Unknown, moss.Diarization);
        Assert.Equal("yes", moss.DiarizationText);

        // The cell reads the same either way, so the pane is where the source goes.
        Assert.Contains("per transcribe.cpp's catalog", whisper.DiarizationDetail,
            StringComparison.Ordinal);
        Assert.Contains("unverified by them", moss.DiarizationDetail,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A local answer beats the catalog, including a local failure.
    /// </summary>
    [Fact]
    public void ACheckOnThisMachineOverridesTheCatalogEitherWay()
    {
        // Upstream says no; a model that was loaded and reported support says yes.
        ModelCatalogItem item = Item("whisper-tiny");
        Assert.Equal("no", item.DiarizationText);

        item.SetDiarization(ModelCatalogItem.DiarizationSupport.Supported);
        Assert.Equal("yes", item.DiarizationText);
        Assert.Equal("yes (checked on this machine)", item.DiarizationDetail);

        // And the reverse: the catalog says yes for moss, and a machine that could
        // not load it has no answer to print. Printing the catalog here would turn
        // a failure to load into a result.
        ModelCatalogItem moss = Item("moss-transcribe-diarize");
        moss.SetDiarization(ModelCatalogItem.DiarizationSupport.LoadFailed);
        Assert.Equal("?", moss.DiarizationText);
        Assert.Equal("unknown, the model could not be loaded", moss.DiarizationDetail);
    }
}