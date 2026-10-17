#nullable enable

using TranscribeCppSharp.Interop;
using Xunit;

namespace TranscribeCppSharp.Interop.Tests;

/// <summary>
/// Offline diarization against the real Sortformer weights
/// (diar_streaming_sortformer_4spk-v2.1-Q8_0, 139 MB, fetched by
/// scripts/run-integration-tests.sh). Sortformer serves TRANSCRIBE_ROLE_DIARIZE
/// in transcribe.cpp 0.4, so <see cref="DiarizeSession"/> runs against the real
/// native library and the ASR path is expected to refuse it.
/// </summary>
public class DiarizeSessionTests
{
    private static string SortformerPath => Path.Combine(TestConfig.RepoRoot, "test-models", "diar_streaming_sortformer_4spk-v2.1-Q8_0.gguf");

    private static bool Ready => File.Exists(SortformerPath) && File.Exists(TestConfig.AudioPath);

    private static float[] Pcm() => PcmExtensions.ReadWavToPcm(TestConfig.AudioPath);

    private static Model LoadSortformer()
        => Model.Load(SortformerPath, p => p.WithBackend(BackendRequest.BackendCpu));

    [SkippableFact]
    public void TheModelServesTheDiarizeRole_NotAsr()
    {
        Skip.IfNot(Ready, "Sortformer model or audio not present.");

        using var model = LoadSortformer();

        Assert.True(model.Roles.HasFlag(TranscribeRole.Diarize));
        Assert.False(model.Roles.HasFlag(TranscribeRole.Asr));

        // 0.4 makes `supports(diarization)` false for a pure diarizer; the role
        // query is the way to ask.
        Assert.False(model.Supports(Feature.FeatureDiarization));

        var info = model.GetDiarizeInfo();
        Assert.Equal(16000, info.SampleRate);
        Assert.Equal(4, info.MaxSpeakers);
    }

    [SkippableFact]
    public void CreateSession_OnADiarizer_ThrowsUnsupportedRole()
    {
        Skip.IfNot(Ready, "Sortformer model or audio not present.");

        using var model = LoadSortformer();

        var ex = Assert.Throws<TranscribeException>(() => model.CreateSession());
        Assert.Equal(Status.ErrUnsupportedRole, ex.StatusCode);
    }

    [SkippableFact]
    public void Run_ReturnsSpeakerSegments()
    {
        Skip.IfNot(Ready, "Sortformer model or audio not present.");

        using var model = LoadSortformer();
        using var diarize = model.CreateDiarizeSession();

        var segments = diarize.Run(Pcm());

        Assert.NotEmpty(segments);
        Assert.All(segments, s => Assert.True(s.End > s.Start));
        // speaker_id is 1-based; 0 means "row not present".
        Assert.All(segments, s => Assert.InRange(s.SpeakerId, 1, 4));
        // p is the attribution confidence, or NaN when the model does not
        // produce one (upstream: "NaN = not produced"). Sortformer does not,
        // so both a real confidence and NaN are valid here.
        Assert.All(segments, s => Assert.True(float.IsNaN(s.Probability) || (s.Probability >= 0f && s.Probability <= 1f)));
    }

    [SkippableFact]
    public void Run_WithASortformerPreset_Works()
    {
        Skip.IfNot(Ready, "Sortformer model or audio not present.");

        using var model = LoadSortformer();
        using var diarize = model.CreateDiarizeSession();
        using var preset = new SortformerDiarizeExtBuilder()
            .WithPreset(SortformerPreset.SortformerPresetHighLatency);

        var segments = diarize.Run(Pcm(), p => p.WithSortformerExt(preset));
        Assert.NotEmpty(segments);
    }

    [SkippableFact]
    public void Run_WithAThreadCount_Works()
    {
        Skip.IfNot(Ready, "Sortformer model or audio not present.");

        using var model = LoadSortformer();
        using var diarize = model.CreateDiarizeSession(p => p.WithThreads(2));

        Assert.NotEmpty(diarize.Run(Pcm()));
    }

    [SkippableFact]
    public void SegmentCount_AgreesWithTheSegments()
    {
        Skip.IfNot(Ready, "Sortformer model or audio not present.");

        using var model = LoadSortformer();
        using var diarize = model.CreateDiarizeSession();
        var segments = diarize.Run(Pcm());

        Assert.Equal(segments.Count, diarize.SegmentCount);
        Assert.Equal(segments.Count, diarize.ReadSegments().Count);
    }

    [SkippableFact]
    public void GetTimings_ReturnsStatistics()
    {
        Skip.IfNot(Ready, "Sortformer model or audio not present.");

        using var model = LoadSortformer();
        using var diarize = model.CreateDiarizeSession();
        diarize.Run(Pcm());

        Assert.NotNull(diarize.GetTimings());
    }

    [SkippableFact]
    public void Run_AlreadyCancelled_ThrowsBeforeRunning()
    {
        Skip.IfNot(Ready, "Sortformer model or audio not present.");

        using var model = LoadSortformer();
        using var diarize = model.CreateDiarizeSession();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() => diarize.Run(Pcm(), ct: cts.Token));
    }

    [SkippableFact]
    public void Run_WithAnUncancelledToken_InstallsAndClearsTheAbortSource()
    {
        Skip.IfNot(Ready, "Sortformer model or audio not present.");

        using var model = LoadSortformer();
        using var diarize = model.CreateDiarizeSession();

        using var cts = new CancellationTokenSource();
        Assert.NotEmpty(diarize.Run(Pcm(), ct: cts.Token));
    }

    [SkippableFact]
    public void Dispose_IsIdempotent_AndUseAfterDisposeThrows()
    {
        Skip.IfNot(Ready, "Sortformer model or audio not present.");

        using var model = LoadSortformer();
        var diarize = model.CreateDiarizeSession();
        diarize.Dispose();
        diarize.Dispose();

        Assert.Throws<ObjectDisposedException>(() => diarize.Run(Pcm()));
    }

    [SkippableFact]
    public void Run_NonFiniteAudio_IsRejected()
    {
        Skip.IfNot(Ready, "Sortformer model or audio not present.");

        using var model = LoadSortformer();
        using var diarize = model.CreateDiarizeSession();

        var pcm = new float[16000];
        pcm[100] = float.NaN;

        var ex = Assert.Throws<TranscribeException>(() => diarize.Run(pcm));
        Assert.Equal(Status.ErrInvalidArg, ex.StatusCode);
    }

    [SkippableFact]
    public void ReadSegments_BeforeAnyRun_IsEmpty()
    {
        Skip.IfNot(Ready, "Sortformer model or audio not present.");

        using var model = LoadSortformer();
        using var diarize = model.CreateDiarizeSession();

        Assert.Empty(diarize.ReadSegments());
        Assert.Equal(0, diarize.SegmentCount);
    }

    [SkippableFact]
    public void Run_AbortedMidRun_ThrowsOperationCanceledException()
    {
        Skip.IfNot(Ready, "Sortformer model or audio not present.");

        using var model = LoadSortformer();
        using var diarize = model.CreateDiarizeSession();

        // Cancel from the configure callback: Run invokes it after its
        // pre-cancelled check and before the native call, so the token is already
        // cancelled when the native library first polls it and the run aborts.
        // Deterministic, unlike a timer or a background thread, which the parallel
        // test host can delay past the native abort check (the pre-cancelled case
        // is a separate test). Diarization runs whole, so this also drops the
        // multi-second input the previous version needed.
        using var cts = new CancellationTokenSource();
        Assert.Throws<OperationCanceledException>(
            () => diarize.Run(Pcm(), configure: _ => cts.Cancel(), ct: cts.Token));
    }
}
