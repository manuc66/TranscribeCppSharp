using System.IO.Compression;
using System.Reflection;
using TranscribeCppSharp.Shared;
using Xunit;

namespace TranscribeCppSharp.Interop.Tests;

/// <summary>
/// The CLI carries the catalog, and reads it through the shared reader.
/// </summary>
/// <remarks>
/// The resource is added by the csproj, so nothing in the CLI's own code would
/// notice if the include were dropped or its path drifted: the build would still
/// succeed and the detail lines would simply stop appearing. This is the check
/// that says the bytes are there.
/// </remarks>
public class UpstreamCatalogEmbeddingTests
{
    private static Assembly CliAssembly => typeof(Cli.TranscribeCommand).Assembly;

    [Fact]
    public void TheCatalogIsEmbeddedInTheCliAssembly()
    {
        Assert.Contains(CliAssembly.GetManifestResourceNames(), n => n == "catalog.zip");

        using Stream? stream = CliAssembly.GetManifestResourceStream("catalog.zip");
        Assert.NotNull(stream);

        using var zip = new ZipArchive(stream!, ZipArchiveMode.Read);
        Assert.Equal(72, zip.Entries.Count(e => !string.IsNullOrEmpty(e.Name)));
    }

    [Fact]
    public void TheReaderAnswersForAnOrdinaryModel()
    {
        var record = UpstreamCatalog.Read("whisper-tiny");

        Assert.NotNull(record);
        Assert.Equal("whisper", record!.Family);
        Assert.True(record.Params > 0);
        Assert.StartsWith("openai/", record.UpstreamRepo!, StringComparison.Ordinal);
        Assert.Equal("169d4a4", record.UpstreamCommit);
    }

    /// <summary>
    /// Our alias for one model differs from upstream's, and the mapping is the
    /// only reason it resolves at all.
    /// </summary>
    [Fact]
    public void TheOneModelWhoseNameDiffersStillResolves()
    {
        var record = UpstreamCatalog.Read("sensevoicesmall");

        Assert.NotNull(record);
        Assert.Equal("sensevoice", record!.Family);
    }

    /// <summary>An alias upstream has no record for returns nothing, not a fabricated row.</summary>
    [Fact]
    public void AnUnknownAliasReturnsNoRecord()
    {
        Assert.Null(UpstreamCatalog.Read("not-a-model"));
    }

    [Fact]
    public void TheCatalogCarriesBothTheClaimAndTheCheckedFlag()
    {
        // The distinction the whole capability table rests on: a capability can be
        // supported without having been verified, and collapsing them would turn
        // upstream's own caveat into a claim they do not make.
        var record = UpstreamCatalog.Read("whisper-tiny");
        Assert.NotNull(record!.Capabilities);

        Assert.True(record.Capabilities!["transcribe"].Supported);
        Assert.False(record.Capabilities!["diarize"].Supported);
    }

    /// <summary>
    /// The section is printed, attributed, and reports the quantization this
    /// manifest pins rather than whichever upstream row happens to come first.
    /// </summary>
    [Fact]
    public void ModelInfoPrintsTheUpstreamCatalogSection()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        Assert.Equal(0, Cli.TranscribeCommand.Run(["--model-info", "whisper-tiny"], stdout, stderr));
        string text = stdout.ToString();

        Assert.Contains("upstream catalog (transcribe.cpp, their measurements):", text,
            StringComparison.Ordinal);
        Assert.Contains("Q4_K_M", text, StringComparison.Ordinal);
        Assert.Contains("unverified upstream", text, StringComparison.Ordinal);

        // Q5_K_M is what this manifest pins for whisper-tiny, so the figure quoted
        // has to be the one for the file a reader would download.
        Assert.Contains("Q5_K_M", text, StringComparison.Ordinal);
        Assert.DoesNotContain("on librispeech test-clean, en, F32", text, StringComparison.Ordinal);
    }

    // The other side of the branch — upstream has no record, so nothing is printed —
    // is not reachable through this command: every alias in the manifest has one, as
    // EveryModelInTheManifestHasUpstreamDetail asserts. It is covered where it can
    // be reached, in AnUnknownAliasReturnsNoRecord.

    [Fact]
    public void TheHeadlineBenchmarkNamesTheResultUpstreamSaysToQuote()
    {
        var record = UpstreamCatalog.Read("whisper-tiny");

        Assert.NotNull(record!.HeadlineBenchmark);
        Assert.Equal("librispeech", record.HeadlineBenchmark!.Dataset);
        Assert.Equal("wer", record.HeadlineBenchmark.Metric);
        Assert.NotEmpty(record.AccuracyBenchmarks ?? new List<UpstreamCatalog.Accuracy>());
    }
}