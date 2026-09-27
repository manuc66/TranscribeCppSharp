#nullable enable

using System;
using System.Diagnostics;
using System.IO;
using TranscribeCppSharp.Cli;
using Xunit;
using Xunit.Abstractions;

namespace TranscribeCppSharp.Interop.Tests;

/// <summary>
/// Tests that run the real `transcribe` executable, so the compiler-generated
/// entry point is covered too (and so the native log lines, which go to the
/// process' own stdout and not to the injected writer, can be asserted).
///
/// The invariant: whatever the user gets wrong on a first run, the process
/// exits 1 with a message — never an unhandled exception, never a stack trace,
/// never a core dump (exit code 134).
/// </summary>
public class CliProcessTests
{
    private readonly ITestOutputHelper _output;

    public CliProcessTests(ITestOutputHelper output) => _output = output;

    private sealed record ProcessResult(int ExitCode, string Out, string Error);

    private static string CliPath => Path.Combine(AppContext.BaseDirectory, "TranscribeCppSharp.Cli.dll");

    private static ProcessResult Run(params string[] args)
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = AppContext.BaseDirectory,
        };
        psi.ArgumentList.Add(CliPath);
        foreach (string arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Could not start the CLI.");

        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new ProcessResult(process.ExitCode, stdout, stderr);
    }

    private static void AssertCleanFailure(ProcessResult result, int expectedExitCode = 1)
    {
        Assert.Equal(expectedExitCode, result.ExitCode);
        AssertNoStackTrace(result);
    }

    private static void AssertNoStackTrace(ProcessResult result)
    {
        string all = result.Out + result.Error;
        Assert.DoesNotContain("Unhandled exception", all, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("   at TranscribeCppSharp", all, StringComparison.Ordinal);
    }

    [Fact]
    public void Help_ExitsZero()
    {
        ProcessResult result = Run("--help");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Usage: transcribe <audio> [model] [options]", result.Out);
        AssertNoStackTrace(result);
    }

    [Fact]
    public void NoArguments_PrintsTheHelpAndExitsOne()
    {
        ProcessResult result = Run();

        AssertCleanFailure(result);
        Assert.Contains("Usage: transcribe", result.Out);
    }

    [Fact]
    public void ListModels_WorksOffline()
    {
        ProcessResult result = Run("--list-models");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("moss-transcribe-diarize", result.Out);
        AssertNoStackTrace(result);
    }

    [Fact]
    public void AMistypedFileName_ExitsOneWithoutDownloadingAModel()
    {
        using var temp = new TempWorkspace();
        string missing = temp.Combine("nope.wav");

        ProcessResult result = Run(missing);

        AssertCleanFailure(result);
        Assert.Contains($"no such file: {missing}", result.Error);
        // Nothing was fetched: the input is checked before the model.
        Assert.DoesNotContain("Downloading", result.Error);
    }

    [Fact]
    public void AFileThatIsNotAudio_ExitsOneWithAMessageAndNoStackTrace()
    {
        using var temp = new TempWorkspace();
        string notAudio = temp.WriteText("notaudio.wav", "this is not audio");
        // The run needs to reach the audio loader, so it needs a model path; the
        // loader runs before the model is loaded.
        string model = File.Exists(TestConfig.ModelPath) ? TestConfig.ModelPath : temp.Combine("dummy.gguf");

        ProcessResult result = Run(notAudio, model);

        if (model.EndsWith("dummy.gguf", StringComparison.Ordinal))
        {
            // Without the integration model the run stops at the model argument,
            // which is also a clean failure.
            AssertCleanFailure(result);
            return;
        }

        AssertCleanFailure(result);
        Assert.Contains("16 kHz mono 16-bit WAV", result.Error);
    }

    [Fact]
    public void ATruncatedFile_ExitsOneWithAMessageAndNoStackTrace()
    {
        using var temp = new TempWorkspace();
        // Used to end the process with a stack trace and exit code 134.
        string junk = temp.WriteBytes("tiny.bin", [1, 2, 3, 4, 5]);
        string model = File.Exists(TestConfig.ModelPath) ? TestConfig.ModelPath : temp.Combine("dummy.gguf");

        ProcessResult result = Run(junk, model);

        AssertCleanFailure(result);
        AssertNoStackTrace(result);
    }

    [Fact]
    public void AnOutputPathInAMissingDirectory_ExitsOne()
    {
        using var temp = new TempWorkspace();
        string audio = temp.WriteWav("a.wav", new float[16000]);
        string model = File.Exists(TestConfig.ModelPath) ? TestConfig.ModelPath : temp.Combine("dummy.gguf");

        ProcessResult result = Run(audio, model, "--out", Path.Combine(temp.Combine("nodir"), "out.txt"));

        AssertCleanFailure(result);
        Assert.Contains("no such directory", result.Error);
    }

    [SkippableFact]
    public void AWindowThatCannotAdvancePastTheOverlap_ExitsOneInsteadOfLoopingForever()
    {
        Skip.IfNot(File.Exists(TestConfig.ModelPath), "Integration test asset (test-models/ggml-tiny.bin) not present.");

        using var temp = new TempWorkspace();
        // 5 s of silence: enough audio for several windows.
        string audio = temp.WriteWav("a.wav", new float[WindowPlanner.SampleRate * 5]);

        // --chunk 1 used to loop on the same window forever (the test itself
        // would hang, which is why the window planner now rejects it).
        ProcessResult result = Run(audio, TestConfig.ModelPath, "--chunk", "1");

        AssertCleanFailure(result);
        Assert.Contains("overlap", result.Error);
    }

    [SkippableFact]
    public void ARealTranscription_ReportsTheComputeAndTheDiarizationSupport()
    {
        Skip.IfNot(TestConfig.IsIntegrationTestEnvironment(), "Integration test assets (test-models/ggml-tiny.bin, test-audio/jfk.wav) not present. Run ./scripts/run-integration-tests.sh to provision them.");

        ProcessResult result = Run(TestConfig.AudioPath, TestConfig.ModelPath, "--out", Path.Combine(Path.GetTempPath(), "cli-process-test.txt"));

        _output.WriteLine(result.Out);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("model : whisper/whisper-tiny", result.Out);
        Assert.Contains("compute:", result.Out);
        // The native WARN is written by the native library, which logs to the
        // process' stderr; this is where the tool's request to attribute
        // speakers can be observed on a model that cannot.
        Assert.Contains("does not support diarization", result.Error);
        Assert.Contains("Speaker 0:", result.Out);
        AssertNoStackTrace(result);

        File.Delete(Path.Combine(Path.GetTempPath(), "cli-process-test.txt"));
    }
}
