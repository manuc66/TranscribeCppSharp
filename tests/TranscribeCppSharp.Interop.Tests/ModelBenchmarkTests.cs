#nullable enable

using System.Globalization;
using TranscribeCppSharp.Models;
using TranscribeCppSharp.Performance;
using Xunit;

namespace TranscribeCppSharp.Interop.Tests;

/// <summary>
/// Tests for the model benchmark's bookkeeping: how a number reads, which machine
/// it belongs to, and how the speed order treats a model that was never measured.
/// </summary>
/// <remarks>
/// The measurement itself is not tested. It needs a real model loaded on real
/// hardware, and a timing taken on the test machine would not be a portable
/// assertion anyway — docs/streaming-bench.md spells out why. What is tested here
/// is every rule that decides whether a number may be compared with another,
/// because those rules are where an honest ranking is won or lost.
/// </remarks>
public class ModelBenchmarkTests
{
    private const string Here = "this-machine";
    private const string Elsewhere = "another-machine";

    private static ModelBenchmark Bench(
        double rtf = 0.25,
        double loadMs = 700,
        string machineKey = Here,
        double? encodeMs = null,
        double? decodeMs = null)
        => new()
        {
            Alias = "unused",
            Rtf = rtf,
            LoadMs = loadMs,
            EncodeMs = encodeMs,
            DecodeMs = decodeMs,
            Passes = 3,
            AudioSeconds = 30,
            AudioName = "clip.wav",
            Backend = "BackendAuto",
            Device = "TestDevice",
            Threads = 0,
            MachineKey = machineKey,
            MeasuredAt = new DateTimeOffset(2026, 1, 2, 3, 4, 0, TimeSpan.Zero),
        };

    /// <summary>
    /// A stand-in for a grid row. The ordering is generic over the row type, so it
    /// is tested here without dragging the Avalonia project into the test run.
    /// </summary>
    /// <param name="Alias">Model alias.</param>
    /// <param name="SizeBytes">Download size.</param>
    private sealed record Row(string Alias, long SizeBytes);

    [Theory]
    [InlineData(0.5, "0.50")]
    [InlineData(2.0, "2.00")]
    [InlineData(0.085, "0.085")]
    [InlineData(0.0, "-")]
    public void RtfText_IsFormattedForAColumnNotForProse(double rtf, string expected)
        // Invariant on purpose: a comma decimal separator reads as a thousands
        // separator when the value sits in a numeric column.
        => Assert.Equal(expected, Bench(rtf: rtf).RtfText);

    [Fact]
    public void RtfText_DoesNotFollowTheAmbientCulture()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            // French writes 0,5 for a half. In a column beside other rows that is
            // unreadable, and it would disagree with what the sort key is doing.
            CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
            Assert.Equal("0.50", Bench(rtf: 0.5).RtfText);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Theory]
    [InlineData(120, "120 ms")]
    [InlineData(1500, "1.5 s")]
    [InlineData(90_000, "1.5 min")]
    public void LoadText_ScalesToSomethingReadable(double loadMs, string expected)
        => Assert.Equal(expected, Bench(loadMs: loadMs).LoadText);

    [Fact]
    public void PhasesText_SaysSoWhenTheNativeLayerReportedNothing()
    {
        // The native contract is that zero means "unknown / not measured", so
        // nothing must be printed as "0.0 ms encode".
        Assert.Equal("no phase timings reported", Bench().PhasesText);
        Assert.Equal(
            "encode not reported, decode not reported",
            Bench(encodeMs: 0, decodeMs: 0).PhasesText);
    }

    [Fact]
    public void PhasesText_RoundsAwayFloatNoise()
    {
        // Timings arrive as float. Printed raw they read 1832.3199462890625,
        // which is precision, not signal.
        ModelBenchmark bench = Bench(encodeMs: 1832.3199462890625, decodeMs: 628.3099975585938);

        Assert.Equal("1832.3 ms encode, 628.3 ms decode", bench.PhasesText);
    }

    [Fact]
    public void PhasesText_MarksOnePhaseMissingRatherThanZero()
    {
        ModelBenchmark onlyEncode = Bench(encodeMs: 1808.2);

        Assert.Equal("1808.2 ms encode, decode not reported", onlyEncode.PhasesText);
    }

    [Fact]
    public void ConditionsText_CarriesEveryConditionTheNumberDependsOn()
    {
        string text = Bench(rtf: 0.08, loadMs: 600).ConditionsText;

        Assert.Contains("0.080x realtime", text);
        Assert.Contains("load 600 ms", text);
        Assert.Contains("3 passes", text);
        Assert.Contains("30s of clip.wav", text);
        Assert.Contains("BackendAuto", text);
        Assert.Contains("TestDevice", text);
        Assert.Contains("2026-01-02", text);
    }

    [Fact]
    public void MachineKey_IsStableAcrossCallsAndNamesTheCoreCount()
    {
        string first = ModelBenchmarkService.MachineKey();

        Assert.Equal(first, ModelBenchmarkService.MachineKey());
        Assert.Contains(
            Environment.ProcessorCount.ToString(CultureInfo.InvariantCulture),
            first,
            StringComparison.Ordinal);
    }

    [Fact]
    public void MachineKey_DoesNotDependOnWhichDeviceARunResolvedTo()
    {
        // A 16 GiB model may fall back to the CPU where a small one stays on the
        // GPU. Keying on the resolved device would give two models measured on
        // this machine different keys and hide both from the comparison.
        string key = ModelBenchmarkService.MachineKey();

        Assert.DoesNotContain("vulkan", key, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cuda", key, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("metal", key, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SpeedOrder_PutsMeasuredModelsFirstAndTheRestAlphabetical()
    {
        // A model that was never timed has no place among the measured ones:
        // first invents a speed, last invents a slowness.
        Row fast = new("whisper-tiny", 100);
        Row slow = new("whisper-large-v3", 900);
        Row alpha = new("breeze-asr-25", 200);
        Row zulu = new("cohere-transcribe-03-2026", 300);

        var benchmarks = new Dictionary<string, ModelBenchmark>(StringComparer.Ordinal)
        {
            [fast.Alias] = Bench(rtf: 0.10),
            [slow.Alias] = Bench(rtf: 2.0),
        };

        List<string> ordered = ModelBenchmarkOrder.BySpeed(
            new[] { zulu, slow, alpha, fast },
            r => r.Alias,
            benchmarks,
            Here,
            fastestFirst: true).Select(r => r.Alias).ToList();

        Assert.Equal([fast.Alias, slow.Alias, alpha.Alias, zulu.Alias], ordered);
    }

    [Fact]
    public void SpeedOrder_ReversesWhenSlowestFirstIsAsked()
    {
        Row fast = new("whisper-tiny", 100);
        Row slow = new("whisper-large-v3", 900);
        var benchmarks = new Dictionary<string, ModelBenchmark>(StringComparer.Ordinal)
        {
            [fast.Alias] = Bench(rtf: 0.10),
            [slow.Alias] = Bench(rtf: 2.0),
        };

        List<string> ordered = ModelBenchmarkOrder.BySpeed(
            new[] { fast, slow },
            r => r.Alias,
            benchmarks,
            Here,
            fastestFirst: false).Select(r => r.Alias).ToList();

        Assert.Equal([slow.Alias, fast.Alias], ordered);
    }

    [Fact]
    public void SpeedOrder_TreatsAResultFromAnotherMachineAsUnmeasured()
    {
        // The reason a machine key travels with a number: someone else's laptop
        // must not be ranked against this machine, however fast it was.
        Row here = new("whisper-tiny", 100);
        Row elsewhere = new("whisper-large-v3", 900);

        var benchmarks = new Dictionary<string, ModelBenchmark>(StringComparer.Ordinal)
        {
            // Elsewhere is three times the "speed", so if it were counted it
            // would come first.
            [here.Alias] = Bench(rtf: 0.30, machineKey: Here),
            [elsewhere.Alias] = Bench(rtf: 0.10, machineKey: Elsewhere),
        };

        List<string> ordered = ModelBenchmarkOrder.BySpeed(
            new[] { elsewhere, here },
            r => r.Alias,
            benchmarks,
            Here,
            fastestFirst: true).Select(r => r.Alias).ToList();

        Assert.Equal([here.Alias, elsewhere.Alias], ordered);
    }

    [Fact]
    public void SpeedOrder_WithNothingMeasuredIsPlainlyAlphabetical()
    {
        Row first = new("breeze-asr-25", 200);
        Row last = new("whisper-tiny", 100);

        List<string> ordered = ModelBenchmarkOrder.BySpeed(
            new[] { last, first },
            r => r.Alias,
            new Dictionary<string, ModelBenchmark>(StringComparer.Ordinal),
            Here,
            fastestFirst: true).Select(r => r.Alias).ToList();

        Assert.Equal([first.Alias, last.Alias], ordered);
    }

    [Fact]
    public void SizeOrder_PutsTheSmallestDownloadFirstAndBreaksTiesByName()
    {
        var rows = new[]
        {
            new Row("whisper-large-v3", 900),
            new Row("whisper-tiny", 100),
            new Row("breeze-asr-25", 100),
        };

        List<string> ordered = ModelBenchmarkOrder.BySize(rows, r => r.Alias, r => r.SizeBytes)
            .Select(r => r.Alias)
            .ToList();

        Assert.Equal(["breeze-asr-25", "whisper-tiny", "whisper-large-v3"], ordered);
    }

    [Fact]
    public void Measure_RefusesAModelThatIsNotOnDiskAndFetchesNothing()
    {
        // Nothing is downloaded in order to be measured: the 72 aliases come to
        // 58 GiB, so a measurement must never start a fetch behind the user's
        // back, and a model that was never downloaded has no local speed.
        ModelDescriptor missing = ModelStore.Find("whisper-large")!;
        string? previous = ModelStore.CacheRootOverride;
        try
        {
            ModelStore.CacheRootOverride = Path.Combine(Path.GetTempPath(), "tcs-bench-empty");

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => ModelBenchmarkService.Measure(missing, "nowhere.wav", 5, null, null, null));

            Assert.Contains("not on disk", ex.Message, StringComparison.Ordinal);
            Assert.Contains("download it first", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            ModelStore.CacheRootOverride = previous;
        }
    }
}