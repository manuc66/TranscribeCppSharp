using TranscribeCppSharp.Ui.Models;
using TranscribeCppSharp.Ui.Services;
using Xunit;

namespace TranscribeCppSharp.Ui.Tests;

/// <summary>
/// The transcription tab dispatches on the model's role (transcribe.cpp 0.4): an
/// ASR model is transcribed, a diarizer returns speaker rows, a language model
/// returns the top language, a VAD model returns a speech report. These run the
/// real service against the role weights fetched by scripts/run-integration-tests.sh.
/// </summary>
public class TranscriptionServiceRoleTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    private static string Model(string file) => Path.Combine(RepoRoot, "test-models", file);

    private static string Audio => Path.Combine(RepoRoot, "test-audio", "jfk.wav");

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "TranscribeCppSharp.slnx")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return AppContext.BaseDirectory;
    }

    private static void Require(string model)
    {
        if (!File.Exists(model) || !File.Exists(Audio))
        {
            Assert.Skip($"test assets missing ({model} or {Audio})");
        }
    }

    private static TranscriptionOptions Cpu()
        => new() { BackendRequest = TranscribeCppSharp.Interop.BackendRequest.BackendCpu };

    [Fact]
    public async Task ADiarizationModel_ReturnsSpeakerRows()
    {
        string model = Model("diar_streaming_sortformer_4spk-v2.1-Q8_0.gguf");
        Require(model);

        var result = await new TranscriptionService().TranscribeAsync(Audio, model, Cpu());

        Assert.True(result.Roles.HasFlag(TranscribeRole.Diarize));
        Assert.NotEmpty(result.SpeakerSegments);
        Assert.Contains("Diarization:", result.FullText);
        Assert.Empty(result.Words);
    }

    [Fact]
    public async Task ALanguageModel_ReturnsTheTopLanguage()
    {
        string model = Model("lang-id-voxlingua107-ecapa-Q8_0.gguf");
        Require(model);

        var result = await new TranscriptionService().TranscribeAsync(Audio, model, Cpu());

        Assert.True(result.Roles.HasFlag(TranscribeRole.Langid));
        Assert.Equal("en", result.DetectedLanguage);
        Assert.Contains("Language identification:", result.FullText);
        Assert.NotEmpty(result.LangIdCandidates);
    }

    [Fact]
    public async Task ALanguageModel_WithAllowedLanguages_RanksOnlyThose()
    {
        string model = Model("lang-id-voxlingua107-ecapa-Q8_0.gguf");
        Require(model);

        var options = Cpu();
        options.AllowedLanguages = ["en", "fr"];

        var result = await new TranscriptionService().TranscribeAsync(Audio, model, options);

        Assert.Equal(2, result.LangIdCandidates.Count);
        Assert.Contains(result.LangIdCandidates, c => c.Code == "en");
    }

    [Fact]
    public async Task AVadModel_ReturnsASpeechReport()
    {
        string model = Model("silero-vad-v6.2-F32.gguf");
        Require(model);

        var result = await new TranscriptionService().TranscribeAsync(Audio, model, Cpu());

        Assert.True(result.Roles.HasFlag(TranscribeRole.Vad));
        Assert.Contains("Voice activity:", result.FullText);
        Assert.Contains("] speech", result.FullText);
        Assert.NotEmpty(result.VadSegments);
    }

    [Fact]
    public async Task AnAsrModel_StillTranscribes()
    {
        string model = Model("ggml-tiny.bin");
        Require(model);

        var result = await new TranscriptionService().TranscribeAsync(Audio, model, Cpu());

        Assert.True(result.Roles.HasFlag(TranscribeRole.Asr));
        Assert.NotEmpty(result.FullText);
    }
}
