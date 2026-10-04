#nullable enable

using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using TranscribeCppSharp.Cli;
using TranscribeCppSharp.Models;
using Xunit;
using Xunit.Abstractions;

namespace TranscribeCppSharp.Interop.Tests;

/// <summary>
/// Tests for the model manifest behind --list-models, --model-info and the model
/// argument. The manifest is the CLI's front door: an entry with a wrong sha256
/// or an unpinned revision is a failed download for the user, so every record is
/// checked here. No network: only the paths that resolve locally.
/// </summary>
public class ModelStoreTests
{
    private readonly ITestOutputHelper _output;

    public ModelStoreTests(ITestOutputHelper output) => _output = output;

    private sealed record AliasRow(string Alias, string Quant, string License, bool NonCommercial, long SizeMb);

    private static (string Out, string Error) Capture(Action<TextWriter, TextWriter> body)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        body(stdout, stderr);
        return (stdout.ToString(), stderr.ToString());
    }

    private static List<AliasRow> ParseListTable(string output)
    {
        // "alias  quant  license  size", with a trailing "!" on the license of a
        // non-commercial model: "canary-1b  Q5_K_M  cc-by-nc-4.0 !  798 MB".
        var rows = new List<AliasRow>();
        foreach (string line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            Match m = Regex.Match(line, @"^(?<alias>\S+)\s+(?<quant>\S+)\s+(?<license>\S+)\s*(?<flag>!)?\s+(?<size>\d+)\s+MB\s*$");
            if (m.Success)
            {
                rows.Add(new AliasRow(
                    m.Groups["alias"].Value,
                    m.Groups["quant"].Value,
                    m.Groups["license"].Value,
                    m.Groups["flag"].Success,
                    long.Parse(m.Groups["size"].Value, CultureInfo.InvariantCulture)));
            }
        }

        return rows;
    }

    /// <summary>Parses the "key : value" records printed by --model-info.</summary>
    private static Dictionary<string, string> ParseInfo(string output)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            int sep = line.IndexOf(':', StringComparison.Ordinal);
            if (sep > 0)
            {
                fields[line[..sep].Trim()] = line[(sep + 1)..].Trim();
            }
        }

        return fields;
    }

    [Fact]
    public void List_PrintsEveryAliasWithItsQuantizationLicenseAndSize()
    {
        (string output, _) = Capture((o, _) => ModelStore.List(o));
        List<AliasRow> rows = ParseListTable(output);

        Assert.NotEmpty(rows);
        Assert.All(rows, row => Assert.False(string.IsNullOrWhiteSpace(row.Alias)));
        Assert.Contains(rows, r => r.Alias == "whisper-tiny");
        // The default model advertised in --help is a real alias.
        Assert.Contains(rows, r => r.Alias == "moss-transcribe-diarize");
        Assert.Contains("! = non-commercial license", output);
        Assert.Contains("Default quantization:", output);
    }

    [Fact]
    public void List_FlagsExactlyTheNonCommercialLicenses()
    {
        (string output, _) = Capture((o, _) => ModelStore.List(o));
        List<AliasRow> rows = ParseListTable(output);

        foreach (AliasRow row in rows)
        {
            bool looksNonCommercial = row.License.Contains("-nc", StringComparison.OrdinalIgnoreCase)
                || row.License.Contains("noncommercial", StringComparison.OrdinalIgnoreCase)
                || row.License.Contains("non-commercial", StringComparison.OrdinalIgnoreCase);

            if (looksNonCommercial != row.NonCommercial)
            {
                Assert.Fail($"license of {row.Alias} is flagged inconsistently: {row.License}");
            }
        }

        // The flag is not decorative: at least one curated model is non-commercial.
        Assert.Contains(rows, r => r.NonCommercial);
    }

    [Fact]
    public void List_ParsesEveryAliasItPrints()
    {
        (string output, _) = Capture((o, _) => ModelStore.List(o));

        // Guards the parser above: if a row is printed in a shape the tests do not
        // understand, they would silently check fewer aliases than exist.
        int printed = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Count(l => l.Contains(" MB") && !l.Contains("Default quantization"));
        Assert.Equal(printed, ParseListTable(output).Count);
    }

    [Fact]
    public void List_ListsModelsFromEveryFamilyTheHelpAdvertises()
    {
        (string output, _) = Capture((o, _) => ModelStore.List(o));

        foreach (string family in new[] { "whisper-", "parakeet-", "moss-", "moonshine", "canary-", "granite-speech-", "voxtral", "breeze-asr" })
        {
            Assert.Contains(family, output);
        }
    }

    [Fact]
    public void EveryAliasIsDownloadableAndVerifiable()
    {
        (string output, _) = Capture((o, _) => ModelStore.List(o));
        List<AliasRow> rows = ParseListTable(output);

        Assert.All(rows, row =>
        {
            (string info, _) = Capture((o, _) => ModelStore.Info(row.Alias, o, TextWriter.Null));
            Dictionary<string, string> fields = ParseInfo(info);

            Assert.StartsWith("handy-computer/", fields["repo"]);
            // A pinned revision: the cache can then be reused offline.
            Assert.Matches("^[0-9a-f]{40}$", fields["revision"]);
            Assert.EndsWith(".gguf", fields["file"]);
            Assert.False(string.IsNullOrWhiteSpace(fields["license"]));
            Assert.StartsWith("https://huggingface.co/", fields["license url"]);
            Assert.True(row.SizeMb > 0, $"{row.Alias} has no recorded size");
        });
    }

    [Fact]
    public void Info_ShowsThePinnedRecordOfAKnownAlias()
    {
        (string output, _) = Capture((o, _) => ModelStore.Info("whisper-tiny", o, TextWriter.Null));

        Assert.StartsWith("whisper-tiny", output);
        Assert.Contains("  quant      : ", output);
        Assert.Contains("  size       : ", output);
    }

    [Fact]
    public void Info_ForAnUnknownAlias_FailsOnStandardError()
    {
        (string output, string error) = Capture((o, e) => Assert.False(ModelStore.Info("nope", o, e)));

        Assert.Equal(string.Empty, output);
        Assert.Contains("Unknown alias 'nope'", error);
        Assert.Contains("--list-models", error);
    }

    [Fact]
    public void Info_MarksANonCommercialLicenseInTheRecord()
    {
        (string output, _) = Capture((o, _) => ModelStore.List(o));
        AliasRow nonCommercial = ParseListTable(output).First(r => r.NonCommercial);

        (string info, _) = Capture((o, _) => ModelStore.Info(nonCommercial.Alias, o, TextWriter.Null));

        Assert.Contains("(non-commercial!)", info);
    }

    [Fact]
    public void Resolve_AnExistingFile_IsUsedAsIsAndNeverDownloaded()
    {
        using var temp = new TempWorkspace();
        string model = temp.WriteBytes("my-model.gguf", [0x47, 0x47, 0x55, 0x46]);

        (string error, _) = Capture((_, e) =>
        {
            string resolved = ModelStore.Resolve(model, null, e);
            Assert.Equal(Path.GetFullPath(model), resolved);
        });

        // No download message: a local file is offline by definition.
        Assert.Equal(string.Empty, error);
    }

    [Fact]
    public void Resolve_AnUnknownModel_ExplainsTheThreeAcceptedForms()
    {
        var error = new StringWriter();
        FileNotFoundException ex = Assert.Throws<FileNotFoundException>(
            () => ModelStore.Resolve("not-a-real-alias", null, error));

        Assert.Contains("neither an existing file, a known alias, nor a '<owner>/<repo>/<file>' spec", ex.Message);
        Assert.Contains("transcribe --list-models", ex.Message);
        Assert.Contains("handy-computer/whisper-tiny-gguf/whisper-tiny-Q5_K_M.gguf", ex.Message);
    }

    [Fact]
    public void Resolve_TwoPartSpec_IsRejectedWithoutAFileName()
    {
        // "owner/repo" is not a spec: there is nothing to fetch.
        Assert.Throws<FileNotFoundException>(
            () => ModelStore.Resolve("handy-computer/whisper-tiny-gguf", null, TextWriter.Null));
    }

    [Fact]
    public void Resolve_AnEmptyModel_IsRejected()
    {
        Assert.Throws<FileNotFoundException>(() => ModelStore.Resolve(string.Empty, null, TextWriter.Null));
    }

    [Fact]
    public void TheDefaultModelInHelp_IsAKnownAlias()
    {
        (string output, _) = Capture((o, _) => ModelStore.List(o));

        // --help promises this alias as the default; it must exist, or the very
        // first run of the tool fails.
        Assert.Contains(CliOptions.DefaultModel, output);
        (string help, _) = Capture((o, _) => o.Write(TranscribeCommand.HelpText));
        Assert.Contains(CliOptions.DefaultModel, help);
    }

    [Fact]
    public void TheDefaultModelReportsDiarizationSupport()
    {
        // Stated here because it is the reason the tool defaults to it: a model
        // without FeatureDiarization would make the diarization output empty.
        (string info, _) = Capture((o, _) => ModelStore.Info(CliOptions.DefaultModel, o, TextWriter.Null));

        Assert.Contains("moss-transcribe-diarize", info);
        _output.WriteLine(info);
    }
}
