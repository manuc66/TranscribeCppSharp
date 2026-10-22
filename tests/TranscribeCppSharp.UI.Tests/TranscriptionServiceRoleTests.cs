using TranscribeCppSharp.UI.Models;
using TranscribeCppSharp.UI.Services;
using Xunit;

namespace TranscribeCppSharp.UI.Tests;

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

    /// <summary>
    /// One transcription through the real service, with the test's own
    /// cancellation token (xUnit1051): a transcription that hangs is then
    /// cancelled when the test is.
    /// </summary>
    private static Task<TranscriptionResult> Transcribe(string model, TranscriptionOptions options)
        => new TranscriptionService().TranscribeAsync(
            Audio,
            model,
            options,
            cancellationToken: TestContext.Current.CancellationToken);

    [Fact]
    public async Task ADiarizationModel_ReturnsSpeakerRows()
    {
        string model = Model("diar_streaming_sortformer_4spk-v2.1-Q8_0.gguf");
        Require(model);

        var result = await Transcribe(model, Cpu());

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

        var result = await Transcribe(model, Cpu());

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

        var result = await Transcribe(model, options);

        Assert.Equal(2, result.LangIdCandidates.Count);
        Assert.Contains(result.LangIdCandidates, c => c.Code == "en");
    }

    [Fact]
    public async Task AVadModel_ReturnsASpeechReport()
    {
        string model = Model("silero-vad-v6.2-F32.gguf");
        Require(model);

        var result = await Transcribe(model, Cpu());

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

        var result = await Transcribe(model, Cpu());

        Assert.True(result.Roles.HasFlag(TranscribeRole.Asr));
        Assert.NotEmpty(result.FullText);
        Assert.NotNull(result.Duration);
        Assert.InRange(result.Duration.Value.TotalSeconds, 10.5, 11.5);
    }

    [Fact]
    public async Task TheWindowSettingSplitsTheAudio()
    {
        // The service used to pass seconds to WindowPlanner.TryPlan, which takes
        // milliseconds: every value below 1000 meant a window shorter than the 1 s
        // overlap, planning failed, and the whole file went to the model in one
        // call. 11 s of audio in 5 s windows must be three windows, not one.
        string model = Model("ggml-tiny.bin");
        Require(model);

        var options = Cpu();
        options.WindowSeconds = 5;
        var steps = new List<TranscriptionStep>();
        var service = new TranscriptionService();

        TranscriptionResult result = await service.TranscribeAsync(
            Audio,
            model,
            options,
            stepProgress: new InlineProgress<TranscriptionStep>(steps.Add),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotEmpty(steps);
        Assert.True(steps.Max(s => s.Total) >= 3, $"planned windows: {steps.Max(s => s.Total)}");
        Assert.All(steps, s => Assert.Equal(5, s.WindowSeconds));
        Assert.Equal(5, result.EffectiveWindowSeconds);
        Assert.Null(result.RequestedWindowSeconds);
    }

    /// <summary>Runs the callback on the reporting thread, so a test can assert after the run.</summary>
    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    [Fact]
    public async Task TheMemoryProbeRunsOneWindowOfTheSelectedFile()
    {
        string model = Model("ggml-tiny.bin");
        Require(model);

        TranscriptionOptions options = Cpu();
        options.WindowSeconds = 5;

        var service = new TranscriptionService();
        MemoryProbeResult result = await service.ProbeMemoryAsync(
            Audio, model, options, TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.False(result.OutOfMemory);

        // A device may report no free figure; the probe still has to succeed.
        if (result.Observation is { } observation)
        {
            Assert.True(observation.TotalBytes > 0);
            Assert.True(observation.MinimumFreeBytes > 0);
        }
    }
}
