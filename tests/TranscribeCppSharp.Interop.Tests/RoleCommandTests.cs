#nullable enable

using TranscribeCppSharp.Cli;
using Xunit;
using Xunit.Abstractions;

namespace TranscribeCppSharp.Interop.Tests;

/// <summary>
/// The CLI dispatches on the model's role (transcribe.cpp 0.4): an ASR model is
/// transcribed, a diarization model prints speaker turns, a language model prints
/// ranked languages, a VAD model prints speech segments. These run the real
/// command against the role weights fetched by scripts/run-integration-tests.sh.
/// </summary>
public class RoleCommandTests
{
    private readonly ITestOutputHelper _output;

    public RoleCommandTests(ITestOutputHelper output) => _output = output;

    private sealed record Result(int ExitCode, string Out, string Error);

    private static Result Run(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        int exitCode = TranscribeCommand.Run(args, stdout, stderr);
        return new Result(exitCode, stdout.ToString(), stderr.ToString());
    }

    private static string Model(string file) => Path.Combine(TestConfig.RepoRoot, "test-models", file);

    private static string Sortformer => Model("diar_streaming_sortformer_4spk-v2.1-Q8_0.gguf");
    private static string LangId => Model("lang-id-voxlingua107-ecapa-Q8_0.gguf");
    private static string Vad => Model("silero-vad-v6.2-F32.gguf");

    private static bool Have(string model) => File.Exists(model) && File.Exists(TestConfig.AudioPath);

    [SkippableFact]
    public void ADiarizationModel_PrintsSpeakerTurns_InsteadOfFailing()
    {
        Skip.IfNot(Have(Sortformer), "Sortformer model or audio not present.");

        Result result = Run(TestConfig.AudioPath, Sortformer, "--backend", "cpu");
        _output.WriteLine(result.Out);

        // It used to crash here with an unhandled UNSUPPORTED_ROLE, because the
        // CLI only ever created an ASR session.
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("roles : diarize", result.Out);
        Assert.Contains("=== diarization ===", result.Out);
        Assert.Contains("Speaker 1", result.Out);
        Assert.DoesNotContain("diarization supported", result.Out);
        AssertNoCrash(result);
    }

    [SkippableFact]
    public void ADiarizationModel_WithVtt_IsRefusedWithAnExplanation()
    {
        Skip.IfNot(Have(Sortformer), "Sortformer model or audio not present.");

        Result result = Run(TestConfig.AudioPath, Sortformer, "--backend", "cpu", "--format", "vtt");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("--format vtt applies to a transcription (ASR) model", result.Error);
        AssertNoCrash(result);
    }

    [SkippableFact]
    public void ALanguageModel_PrintsRankedLanguages()
    {
        Skip.IfNot(Have(LangId), "LANGID model or audio not present.");

        Result result = Run(TestConfig.AudioPath, LangId, "--backend", "cpu");
        _output.WriteLine(result.Out);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("roles : langid", result.Out);
        Assert.Contains("=== language identification ===", result.Out);
        Assert.Contains("en", result.Out);
        AssertNoCrash(result);
    }

    [SkippableFact]
    public void ALanguageModel_WithAllowed_RanksOnlyThose()
    {
        Skip.IfNot(Have(LangId), "LANGID model or audio not present.");

        Result result = Run(TestConfig.AudioPath, LangId, "--backend", "cpu", "--allowed", "en,fr");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("(2 candidates)", result.Out);
        AssertNoCrash(result);
    }

    [SkippableFact]
    public void AVadModel_PrintsSpeechSegments()
    {
        Skip.IfNot(Have(Vad), "VAD model or audio not present.");

        Result result = Run(TestConfig.AudioPath, Vad, "--backend", "cpu");
        _output.WriteLine(result.Out);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("roles : vad", result.Out);
        Assert.Contains("=== speech activity ===", result.Out);
        Assert.Contains("] speech", result.Out);
        AssertNoCrash(result);
    }

    [SkippableFact]
    public void ADiarizationModel_WritesTheReportToOut()
    {
        Skip.IfNot(Have(Sortformer), "Sortformer model or audio not present.");

        using var temp = new TempWorkspace();
        string outFile = temp.Combine("diarize.txt");

        Result result = Run(TestConfig.AudioPath, Sortformer, "--backend", "cpu", "--out", outFile);
        Assert.Equal(0, result.ExitCode);

        string written = File.ReadAllText(outFile);
        Assert.Contains("# TranscribeCppSharp diarization report", written);
        Assert.Contains("Speaker 1", written);
        AssertNoCrash(result);
    }

    [SkippableFact]
    public void ADiarizationModel_WritesJsonToOut()
    {
        Skip.IfNot(Have(Sortformer), "Sortformer model or audio not present.");

        using var temp = new TempWorkspace();
        string outFile = temp.Combine("diarize.json");

        Result result = Run(TestConfig.AudioPath, Sortformer, "--backend", "cpu", "--out", outFile, "--format", "json");
        Assert.Equal(0, result.ExitCode);

        string written = File.ReadAllText(outFile);
        Assert.Contains("\"role\": \"diarize\"", written);
        Assert.Contains("\"speaker\":", written);
        AssertNoCrash(result);
    }

    [SkippableFact]
    public void AnAsrModel_StillReportsItsRole()
    {
        Skip.IfNot(File.Exists(TestConfig.ModelPath) && File.Exists(TestConfig.AudioPath),
            "Whisper test asset not present.");

        Result result = Run(TestConfig.AudioPath, TestConfig.ModelPath, "--backend", "cpu");
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("roles : asr", result.Out);
        Assert.Contains("diarization supported", result.Out);
        AssertNoCrash(result);
    }

    [SkippableFact]
    public void Allowed_WithAnEmptyEntry_IsRejected()
    {
        Skip.IfNot(File.Exists(TestConfig.AudioPath), "Test audio not present.");

        // A real file gets past the input check; no model is resolved, because
        // parsing fails first.
        Result result = Run(TestConfig.AudioPath, "--allowed", "en,,fr");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("invalid --allowed", result.Error);
    }

    private static void AssertNoCrash(Result result)
    {
        Assert.DoesNotContain("Unhandled exception", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("   at ", result.Error, StringComparison.Ordinal);
    }
}
