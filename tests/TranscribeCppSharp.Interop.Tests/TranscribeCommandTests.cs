#nullable enable

using System;
using System.IO;
using System.Text.Json;
using TranscribeCppSharp.Cli;
using TranscribeCppSharp.Interop;
using Xunit;
using Xunit.Abstractions;

namespace TranscribeCppSharp.Interop.Tests;

/// <summary>
/// End-to-end tests of the command itself, in process with the console injected
/// (<see cref="TranscribeCommand.Run"/>). These are the "first run" cases: the
/// paths a new user hits before any model is loaded, plus one real transcription
/// per export format when the integration assets are present.
///
/// The invariant asserted throughout: a reported problem is a message on stderr
/// and exit code 1 — never an unhandled exception, and never a stack trace on
/// the console.
/// </summary>
public class TranscribeCommandTests
{
    private readonly ITestOutputHelper _output;

    public TranscribeCommandTests(ITestOutputHelper output) => _output = output;

    private sealed record Result(int ExitCode, string Out, string Error);

    private static Result Run(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        int exitCode = TranscribeCommand.Run(args, stdout, stderr);
        return new Result(exitCode, stdout.ToString(), stderr.ToString());
    }

    private static void AssertNoCrash(Result result)
    {
        Assert.DoesNotContain("Unhandled exception", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("   at ", result.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("Unhandled exception", result.Out, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NoArguments_PrintsTheHelpAndFails()
    {
        Result result = Run();

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Usage: transcribe <audio> [model] [options]", result.Out);
        AssertNoCrash(result);
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    public void Help_IsPrintedAndSucceeds(string flag)
    {
        Result result = Run(flag);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Usage: transcribe <audio> [model] [options]", result.Out);
        // Every documented option is listed, so --help is the reference.
        Assert.Contains("--backend", result.Out);
        Assert.Contains("--device", result.Out);
        Assert.Contains("--list-devices", result.Out);
        Assert.Contains("--no-diarize", result.Out);
        AssertNoCrash(result);
    }

    [Fact]
    public void ListModels_ListsTheCuratedAliasesOffline()
    {
        Result result = Run("--list-models");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("alias", result.Out);
        Assert.Contains("whisper-tiny", result.Out);
        // The default model is in the list: --help advertises it.
        Assert.Contains("moss-transcribe-diarize", result.Out);
        Assert.Contains("non-commercial license", result.Out);
        AssertNoCrash(result);
    }

    [Fact]
    public void ModelInfo_ShowsThePinnedRecordForAKnownAlias()
    {
        Result result = Run("--model-info", "whisper-tiny");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("whisper-tiny", result.Out);
        Assert.Contains("repo       :", result.Out);
        Assert.Contains("revision   :", result.Out);
        Assert.Contains("license url:", result.Out);
        AssertNoCrash(result);
    }

    [Fact]
    public void ModelInfo_ForAnUnknownAlias_FailsWithTheListHint()
    {
        Result result = Run("--model-info", "not-a-real-alias");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Unknown alias 'not-a-real-alias'", result.Error);
        Assert.Contains("--list-models", result.Error);
        AssertNoCrash(result);
    }

    [Fact]
    public void ModelInfo_WithoutAnAlias_Fails()
    {
        Result result = Run("--model-info");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Unknown alias", result.Error);
        AssertNoCrash(result);
    }

    [Fact]
    public void AMistypedFileNameIsReportedBeforeTheModelIsResolved()
    {
        using var temp = new TempWorkspace();
        string missing = temp.Combine("nope.wav");

        // No model is downloaded for this: the input is checked first, and the
        // default model is 600+ MB.
        Result result = Run(missing);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal($"no such file: {missing}", result.Error.Trim());
        AssertNoCrash(result);
    }

    [Fact]
    public void AnInvalidOptionIsReportedBeforeTheModelIsResolved()
    {
        using var temp = new TempWorkspace();
        string audio = temp.WriteWav("a.wav", new float[16000]);

        Result result = Run(audio, "--format", "srt");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("unknown --format: srt", result.Error);
        AssertNoCrash(result);
    }

    [Fact]
    public void AnUnknownAliasIsReportedWithTheThreeAcceptedForms()
    {
        using var temp = new TempWorkspace();
        string audio = temp.WriteWav("a.wav", new float[16000]);

        Result result = Run(audio, "not-a-real-alias");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("neither an existing file, a known alias", result.Error);
        Assert.Contains("<owner>/<repo>/<file>", result.Error);
        AssertNoCrash(result);
    }

    [Fact]
    public void AFileThatIsNotAudioIsReportedAsOneLine()
    {
        using var temp = new TempWorkspace();
        string notAudio = temp.WriteText("notaudio.wav", "this is not audio");

        // Needs a real model path, so the run reaches the audio loader.
        Result result = Run(notAudio, LocalModel() ?? "unused.gguf");

        if (LocalModel() is null)
        {
            Assert.Equal(1, result.ExitCode);
            AssertNoCrash(result);
            return;
        }

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("16 kHz mono 16-bit WAV", result.Error);
        AssertNoCrash(result);
    }

    [Fact]
    public void AWindowThatCannotAdvancePastTheOverlapIsRejected()
    {
        using var temp = new TempWorkspace();
        string audio = temp.WriteWav("a.wav", new float[WindowPlanner.SampleRate * 5]);

        // --chunk 1 used to loop forever on the same window.
        Result result = Run(audio, LocalModel() ?? "unused.gguf", "--chunk", "1");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("overlap", result.Error);
        AssertNoCrash(result);
    }

    [Fact]
    public void AnUnwritableOutputPathIsReported()
    {
        using var temp = new TempWorkspace();
        string audio = temp.WriteWav("a.wav", new float[16000]);

        Result result = Run(audio, LocalModel() ?? "unused.gguf", "--out", temp.Combine("nodir") + "/out.txt");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("--out", result.Error);
        AssertNoCrash(result);
    }

    [Fact]
    public void ListDevices_PrintsTheTableWithoutLoadingAModel()
    {
        // Answers before any model resolution, so it must work on a machine with
        // no model and no network.
        Result result = Run("--list-devices");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("index", result.Out);
        Assert.Contains("kind", result.Out);
        // At least the CPU: the native library always registers it.
        Assert.Contains("cpu", result.Out);
        AssertNoCrash(result);
    }

    [Fact]
    public void AForcedBackendThatIsUnavailableFailsInsteadOfFallingBack()
    {
        using var temp = new TempWorkspace();
        string audio = temp.WriteWav("a.wav", new float[16000]);

        // Whatever this machine has, it cannot have all of these at once; the
        // point is that an unavailable forced backend is reported, never
        // silently replaced by the CPU.
        foreach (string backend in new[] { "cuda", "rocm" })
        {
            if (Backends.BackendAvailable(Parse(backend)))
            {
                continue;
            }

            Result result = Run(audio, temp.Combine("unused.gguf"), "--backend", backend);
            Assert.Equal(1, result.ExitCode);
            Assert.Contains($"--backend {backend} is not available", result.Error);
            AssertNoCrash(result);
        }

        static BackendRequest Parse(string name) => name switch
        {
            "cuda" => BackendRequest.BackendCuda,
            _ => BackendRequest.BackendRocm,
        };
    }

    [SkippableFact]
    public void TranscribesAndExportsTheThreeFormats()
    {
        Skip.IfNot(IsIntegrationEnv, "Integration test assets (test-models/ggml-tiny.bin, test-audio/jfk.wav) not present. Run ./scripts/run-integration-tests.sh to provision them.");

        using var temp = new TempWorkspace();
        string audio = TestConfig.AudioPath;
        string model = TestConfig.ModelPath;

        Result plain = Run(audio, model, "--out", temp.Combine("out.txt"));
        Assert.Equal(0, plain.ExitCode);
        _output.WriteLine(plain.Out);
        Assert.Contains("audio :", plain.Out);
        Assert.Contains("model : whisper/whisper-tiny", plain.Out);
        Assert.Contains("compute:", plain.Out);
        // Whisper has no diarization: the tool says so rather than pretending.
        Assert.Contains("diarization supported: False", plain.Out);
        Assert.Contains("=== summary ===", plain.Out);
        Assert.Contains("Speaker 0:", plain.Out);
        AssertNoCrash(plain);

        Result vtt = Run(audio, model, "--out", temp.Combine("out.vtt"), "--format", "vtt");
        Assert.Equal(0, vtt.ExitCode);
        string vttContent = File.ReadAllText(temp.Combine("out.vtt"));
        Assert.StartsWith("WEBVTT", vttContent);
        Assert.Contains("<v Speaker 0>", vttContent);

        Result json = Run(audio, model, "--out", temp.Combine("out.json"), "--format", "json");
        Assert.Equal(0, json.ExitCode);
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(temp.Combine("out.json")));
        Assert.False(string.IsNullOrEmpty(doc.RootElement.GetProperty("text").GetString()));
        Assert.NotEmpty(doc.RootElement.GetProperty("segments").EnumerateArray());
    }

    [SkippableFact]
    public void SplitsLongAudioIntoOverlappingWindows()
    {
        Skip.IfNot(IsIntegrationEnv, "Integration test assets (test-models/ggml-tiny.bin, test-audio/jfk.wav) not present. Run ./scripts/run-integration-tests.sh to provision them.");

        Result result = Run(TestConfig.AudioPath, TestConfig.ModelPath, "--chunk", "2");

        Assert.Equal(0, result.ExitCode);
        // 11 s of audio in 2 s windows, 1 s overlap -> 10 windows.
        Assert.Contains("windows: 10", result.Out);
        Assert.Contains("window 1 @ 00:00.0", result.Out);
        Assert.Contains("window 10 @ 00:09.0", result.Out);
        AssertNoCrash(result);
    }

    [SkippableFact]
    public void NoDiarize_AsksTheNativeLayerForNoSpeakerAttribution()
    {
        Skip.IfNot(IsIntegrationEnv, "Integration test assets (test-models/ggml-tiny.bin, test-audio/jfk.wav) not present. Run ./scripts/run-integration-tests.sh to provision them.");

        Result result = Run(TestConfig.AudioPath, TestConfig.ModelPath, "--no-diarize");

        Assert.Equal(0, result.ExitCode);
        // Whisper ignores the toggle, so every row is speaker 0. What is
        // observable here is that the run succeeds; the native WARN that the
        // request was refused is written to the process' own stdout by the
        // native library, so CliProcessTests asserts that one.
        Assert.Contains("Speaker 0:", result.Out);
        AssertNoCrash(result);
    }

    private static bool IsIntegrationEnv => TestConfig.IsIntegrationTestEnvironment();

    private static string? LocalModel()
        => File.Exists(TestConfig.ModelPath) ? TestConfig.ModelPath : null;
}
