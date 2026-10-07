#nullable enable

using TranscribeCppSharp.Cli;
using Xunit;

namespace TranscribeCppSharp.Interop.Tests;

/// <summary>
/// Tests for the CLI's argument parsing. Every rule the user can hit before any
/// model is loaded lives here, so a typo costs a message instead of a crash or a
/// wasted download. No native calls, no network: these run anywhere.
/// </summary>
public class CliOptionsTests
{
    [Fact]
    public void TryParse_WithoutArguments_ReportsTheMissingAudioFile()
    {
        Assert.False(CliOptions.TryParse([], out var options, out var error));
        Assert.Null(options);
        Assert.Contains("no audio file", error);
        Assert.Contains("--help", error);
    }

    [Fact]
    public void TryParse_WithOnlyTheAudioFile_UsesTheDocumentedDefaults()
    {
        using var temp = new TempWorkspace();
        string audio = temp.WriteWav("a.wav", new float[16000]);

        Assert.True(CliOptions.TryParse([audio], out var options, out var error));

        Assert.Null(error);
        Assert.NotNull(options);
        Assert.Equal(audio, options.AudioPath);
        // The diarization model, not a Whisper alias: attribution is requested.
        Assert.Equal("moss-transcribe-diarize", options.ModelSpec);
        Assert.Null(options.Quant);
        Assert.Equal("en", options.Language);
        Assert.Equal(300, options.ChunkSeconds);
        Assert.Equal(DiarizeMode.DiarizeModeOn, options.Diarize);
        Assert.Null(options.OutPath);
        Assert.Equal(TranscriptFormat.Plain, options.Format);
        Assert.Equal(BackendRequest.BackendAuto, options.Backend);
        Assert.Null(options.DeviceIndex);
        Assert.False(options.WritesFile);
    }

    [Fact]
    public void TryParse_WithAPositionalModel_UsesItOverTheDefault()
    {
        using var temp = new TempWorkspace();
        string audio = temp.WriteWav("a.wav", new float[16000]);

        Assert.True(CliOptions.TryParse([audio, "whisper-tiny"], out var options, out _));

        Assert.Equal("whisper-tiny", options!.ModelSpec);
    }

    [Fact]
    public void TryParse_WithModelFlag_UsesItOverThePositionalArgument()
    {
        using var temp = new TempWorkspace();
        string audio = temp.WriteWav("a.wav", new float[16000]);

        Assert.True(CliOptions.TryParse([audio, "whisper-tiny", "--model", "parakeet-tdt-0.6b-v3"], out var options, out _));

        Assert.Equal("parakeet-tdt-0.6b-v3", options!.ModelSpec);
    }

    [Fact]
    public void TryParse_ResolvesTheAudioAndOutputPathsToAbsolutePaths()
    {
        using var temp = new TempWorkspace();
        string audio = temp.WriteWav("a.wav", new float[16000]);

        Assert.True(CliOptions.TryParse([audio, "--out", "sub.txt"], out var options, out _));

        Assert.True(Path.IsPathRooted(options!.AudioPath));
        Assert.True(Path.IsPathRooted(options.OutPath!));
        Assert.True(options.WritesFile);
    }

    [Fact]
    public void TryParse_WithNoDiarize_TurnsAttributionOff()
    {
        using var temp = new TempWorkspace();
        string audio = temp.WriteWav("a.wav", new float[16000]);

        Assert.True(CliOptions.TryParse([audio, "--no-diarize"], out var options, out _));

        Assert.Equal(DiarizeMode.DiarizeModeOff, options!.Diarize);
    }

    [Theory]
    [InlineData("plain", TranscriptFormat.Plain)]
    [InlineData("vtt", TranscriptFormat.Vtt)]
    [InlineData("json", TranscriptFormat.Json)]
    public void TryParse_AcceptsTheDocumentedFormats(string name, object expected)
    {
        using var temp = new TempWorkspace();
        string audio = temp.WriteWav("a.wav", new float[16000]);

        Assert.True(CliOptions.TryParse([audio, "--format", name], out var options, out _));

        // The parameter is boxed as object: TranscriptFormat is internal to the
        // CLI and a public test method cannot expose it.
        Assert.Equal(expected, options!.Format);
        // Echoed back in the console and the summary with the same spelling.
        Assert.Equal(name, TranscribeCommand.FormatName(options.Format));
    }

    [Theory]
    [InlineData("srt")]
    [InlineData("VTT")]
    [InlineData("")]
    public void TryParse_RejectsAnUnknownFormat(string name)
    {
        using var temp = new TempWorkspace();
        string audio = temp.WriteWav("a.wav", new float[16000]);

        Assert.False(CliOptions.TryParse([audio, "--format", name], out _, out var error));

        Assert.Contains("plain, vtt or json", error);
    }

    [Fact]
    public void TryParse_RejectsAMissingAudioFile()
    {
        using var temp = new TempWorkspace();
        string missing = temp.Combine("nope.wav");

        Assert.False(CliOptions.TryParse([missing], out _, out var error));

        Assert.Equal($"no such file: {missing}", error);
    }

    [Fact]
    public void TryParse_RejectsADirectoryGivenAsAudio()
    {
        using var temp = new TempWorkspace();

        Assert.False(CliOptions.TryParse([temp.Path], out _, out var error));

        // "no such file" would be wrong: the path exists, it is just not a file.
        Assert.Contains("not a file", error);
        Assert.Contains("one file per run", error);
    }

    [Fact]
    public void TryParse_RejectsAnOutputDirectoryThatDoesNotExist()
    {
        using var temp = new TempWorkspace();
        string audio = temp.WriteWav("a.wav", new float[16000]);
        string missingDir = temp.Combine("nodir");

        Assert.False(CliOptions.TryParse([audio, "--out", Path.Combine(missingDir, "out.txt")], out _, out var error));

        Assert.Contains("--out", error);
        Assert.Contains("no such directory", error);
    }

    [Fact]
    public void TryParse_RejectsAnUnknownOption()
    {
        using var temp = new TempWorkspace();
        string audio = temp.WriteWav("a.wav", new float[16000]);

        // A typo used to be ignored, so the run silently used another setting.
        Assert.False(CliOptions.TryParse([audio, "--formt", "json"], out _, out var error));

        Assert.Contains("unknown option: --formt", error);
        Assert.Contains("--help", error);
    }

    [Fact]
    public void TryParse_RejectsAnUnexpectedPositionalArgument()
    {
        using var temp = new TempWorkspace();
        string audio = temp.WriteWav("a.wav", new float[16000]);

        Assert.False(CliOptions.TryParse([audio, "whisper-tiny", "leftover"], out _, out var error));

        Assert.Contains("unexpected argument: leftover", error);
    }

    [Fact]
    public void TryParse_ReportsAMissingFlagValue()
    {
        using var temp = new TempWorkspace();
        string audio = temp.WriteWav("a.wav", new float[16000]);

        Assert.False(CliOptions.TryParse([audio, "--model"], out _, out var atEnd));
        Assert.Contains("missing value for --model", atEnd);

        // The next flag is not swallowed as a value.
        Assert.False(CliOptions.TryParse([audio, "--model", "--quant", "Q8_0"], out _, out var nextFlag));
        Assert.Contains("missing value for --model", nextFlag);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("1.5")]
    public void TryParse_RejectsAnUnusableChunkInsteadOfIgnoringIt(string value)
    {
        using var temp = new TempWorkspace();
        string audio = temp.WriteWav("a.wav", new float[16000]);

        Assert.False(CliOptions.TryParse([audio, "--chunk", value], out _, out var error));

        Assert.Contains("--chunk", error);
    }

    [Fact]
    public void TryParse_RejectsAChunkThatCannotAdvancePastTheOverlap()
    {
        using var temp = new TempWorkspace();
        string audio = temp.WriteWav("a.wav", new float[16000]);

        // Windows overlap by 1 s, so a 1 s window would loop on the same window
        // forever. Rejected, with the smallest usable value in the message.
        Assert.False(CliOptions.TryParse([audio, "--chunk", "1"], out _, out var error));

        Assert.Contains("1s overlap", error);
        Assert.Contains("2 or more", error);
    }

    [Fact]
    public void TryParse_RejectsAnEmptyLanguage()
    {
        using var temp = new TempWorkspace();
        string audio = temp.WriteWav("a.wav", new float[16000]);

        Assert.False(CliOptions.TryParse([audio, "--lang", ""], out _, out var error));

        Assert.Contains("--lang", error);
    }

    [Fact]
    public void TryParse_KeepsTheLanguageAsTyped()
    {
        using var temp = new TempWorkspace();
        string audio = temp.WriteWav("a.wav", new float[16000]);

        Assert.True(CliOptions.TryParse([audio, "--lang", "fr"], out var options, out _));

        Assert.Equal("fr", options!.Language);
    }

    [Fact]
    public void TryParse_PassesTheQuantThrough()
    {
        using var temp = new TempWorkspace();
        string audio = temp.WriteWav("a.wav", new float[16000]);

        Assert.True(CliOptions.TryParse([audio, "whisper-tiny", "--quant", "Q8_0"], out var options, out _));

        Assert.Equal("Q8_0", options!.Quant);
    }

    [Fact]
    public void TryParse_AcceptsTheDocumentedBackends()
    {
        using var temp = new TempWorkspace();
        string audio = temp.WriteWav("a.wav", new float[16000]);

        Assert.True(CliOptions.TryParse([audio, "--backend", "CPU"], out var options, out _));

        Assert.Equal(BackendRequest.BackendCpu, options!.Backend);
        // The error messages quote the CLI spelling, not the enum name.
        Assert.Equal("cpu", options.BackendName);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("x")]
    public void TryParse_RejectsAnUnusableDeviceIndex(string value)
    {
        using var temp = new TempWorkspace();
        string audio = temp.WriteWav("a.wav", new float[16000]);

        Assert.False(CliOptions.TryParse([audio, "--device", value], out _, out var error));

        Assert.Contains("--device", error);
        Assert.Contains("--list-devices", error);
    }

    [Fact]
    public void TryParse_KeepsTheDeviceIndex()
    {
        using var temp = new TempWorkspace();
        string audio = temp.WriteWav("a.wav", new float[16000]);

        Assert.True(CliOptions.TryParse([audio, "--device", "2"], out var options, out _));

        Assert.Equal(2, options!.DeviceIndex);
    }

    [Fact]
    public void TryParse_AcceptsTheFlagsAnsweredBeforeParsing()
    {
        using var temp = new TempWorkspace();
        string audio = temp.WriteWav("a.wav", new float[16000]);

        // These are handled before parsing; if they ever reach the parser they
        // must not be reported as unknown options.
        Assert.True(CliOptions.TryParse([audio, "--help"], out _, out var help));
        Assert.Null(help);
        Assert.True(CliOptions.TryParse([audio, "--list-models"], out _, out var list));
        Assert.Null(list);
        // The value of --model-info is consumed, not mistaken for the audio file.
        Assert.False(CliOptions.TryParse(["--model-info", "whisper-tiny"], out _, out var info));
        Assert.Contains("no audio file", info);
    }

    [Fact]
    public void ValueAfter_ReturnsTheValueOfAFlag()
    {
        Assert.Equal("whisper-tiny", CliOptions.ValueAfter(["--model-info", "whisper-tiny"], "--model-info"));
    }

    [Fact]
    public void ValueAfter_ReturnsNullWhenTheValueIsMissing()
    {
        Assert.Null(CliOptions.ValueAfter(["--model-info"], "--model-info"));
        Assert.Null(CliOptions.ValueAfter(["--model-info", "--other"], "--model-info"));
    }
}
