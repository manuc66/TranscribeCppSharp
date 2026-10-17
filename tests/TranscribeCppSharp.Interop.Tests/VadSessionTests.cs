#nullable enable

using Xunit;

namespace TranscribeCppSharp.Interop.Tests;

/// <summary>
/// Voice activity detection against the real Silero VAD v6.2 weights
/// (1.2 MB, fetched by scripts/run-integration-tests.sh). The model serves
/// TRANSCRIBE_ROLE_VAD, so every path through <see cref="VadSession"/> runs
/// against the real native library, not a stub.
/// </summary>
public class VadSessionTests
{
    private static string VadPath => Path.Combine(TestConfig.RepoRoot, "test-models", "silero-vad-v6.2-F32.gguf");

    private static bool Ready => File.Exists(VadPath) && File.Exists(TestConfig.AudioPath);

    private static float[] Pcm() => PcmExtensions.ReadWavToPcm(TestConfig.AudioPath);

    private static Model LoadVad()
        => Model.Load(VadPath, p => p.WithBackend(BackendRequest.BackendCpu));

    [SkippableFact]
    public void TheModelServesTheVadRole_AndReportsItsFrameSize()
    {
        Skip.IfNot(Ready, "Silero VAD model or audio not present.");

        using var model = LoadVad();

        Assert.True(model.Roles.HasFlag(TranscribeRole.Vad));
        Assert.False(model.Roles.HasFlag(TranscribeRole.Asr));

        var info = model.GetVadInfo();
        Assert.Equal(16000, info.SampleRate);
        Assert.Equal(512, info.FrameSamples);
    }

    [SkippableFact]
    public void Run_ReturnsOneProbabilityPerFrameAndTheSpeechSegments()
    {
        Skip.IfNot(Ready, "Silero VAD model or audio not present.");

        using var model = LoadVad();
        using var vad = model.CreateVadSession();

        var result = vad.Run(Pcm());

        Assert.NotEmpty(result.Probabilities);
        Assert.All(result.Probabilities, p => Assert.InRange(p, 0f, 1f));
        Assert.Equal(0, result.FirstFrame);

        // The JFK sample is continuous speech: at least one segment, well-formed.
        Assert.NotEmpty(result.Segments);
        Assert.All(result.Segments, s => Assert.True(s.EndSample > s.StartSample));
        Assert.All(result.Segments, s => Assert.True(s.StartSample >= 0));
    }

    [SkippableFact]
    public void Run_WithATightThreshold_StillProducesWellFormedSegments()
    {
        Skip.IfNot(Ready, "Silero VAD model or audio not present.");

        using var model = LoadVad();
        using var vad = model.CreateVadSession();

        var result = vad.Run(Pcm(), p => p.WithThreshold(0.9).WithMinSpeechMs(0).WithSpeechPadMs(0));

        Assert.NotEmpty(result.Probabilities);
        Assert.All(result.Segments, s => Assert.True(s.EndSample > s.StartSample));
    }

    [SkippableFact]
    public void Run_WithAThreadCount_Works()
    {
        Skip.IfNot(Ready, "Silero VAD model or audio not present.");

        using var model = LoadVad();
        using var vad = model.CreateVadSession(p => p.WithThreads(2));

        var result = vad.Run(Pcm());
        Assert.NotEmpty(result.Probabilities);
    }

    [SkippableFact]
    public void Streaming_FeedFlushReset_YieldsProbabilities()
    {
        Skip.IfNot(Ready, "Silero VAD model or audio not present.");

        using var model = LoadVad();
        using var vad = model.CreateVadSession();
        float[] pcm = Pcm();

        // Feed in two halves, then flush. A feed produces probabilities but no
        // segments; segments are an offline (Run) concept.
        vad.Feed(pcm.AsSpan(0, pcm.Length / 2));
        var afterFirstFeed = vad.ReadResult();
        Assert.NotEmpty(afterFirstFeed.Probabilities);
        Assert.Empty(afterFirstFeed.Segments);

        vad.Feed(pcm.AsSpan(pcm.Length / 2));
        vad.Flush();
        var afterFlush = vad.ReadResult();
        Assert.NotEmpty(afterFlush.Probabilities);
        Assert.Empty(afterFlush.Segments);

        // Reset then feed again: the session is reusable.
        vad.Reset();
        vad.Feed(pcm);
        Assert.NotEmpty(vad.ReadResult().Probabilities);
    }

    [SkippableFact]
    public void GetTimings_ReturnsStatistics()
    {
        Skip.IfNot(Ready, "Silero VAD model or audio not present.");

        using var model = LoadVad();
        using var vad = model.CreateVadSession();
        vad.Run(Pcm());

        var timings = vad.GetTimings();
        Assert.NotNull(timings);
    }

    [SkippableFact]
    public void Run_AlreadyCancelled_ThrowsBeforeRunning()
    {
        Skip.IfNot(Ready, "Silero VAD model or audio not present.");

        using var model = LoadVad();
        using var vad = model.CreateVadSession();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() => vad.Run(Pcm(), ct: cts.Token));
    }

    [SkippableFact]
    public void Run_WithAnUncancelledToken_InstallsAndClearsTheAbortSource()
    {
        Skip.IfNot(Ready, "Silero VAD model or audio not present.");

        using var model = LoadVad();
        using var vad = model.CreateVadSession();

        // CanBeCanceled true but never cancelled: exercises the abort-source
        // install/clear path without aborting the run.
        using var cts = new CancellationTokenSource();
        var result = vad.Run(Pcm(), ct: cts.Token);

        Assert.NotEmpty(result.Probabilities);
    }

    [SkippableFact]
    public void Dispose_IsIdempotent_AndUseAfterDisposeThrows()
    {
        Skip.IfNot(Ready, "Silero VAD model or audio not present.");

        using var model = LoadVad();
        var vad = model.CreateVadSession();
        vad.Dispose();
        vad.Dispose(); // second dispose is a no-op

        Assert.Throws<ObjectDisposedException>(() => vad.Run(Pcm()));
    }

    [SkippableFact]
    public void Run_NonFiniteAudio_IsRejected()
    {
        Skip.IfNot(Ready, "Silero VAD model or audio not present.");

        using var model = LoadVad();
        using var vad = model.CreateVadSession();

        var pcm = new float[16000];
        pcm[100] = float.NaN;

        var ex = Assert.Throws<TranscribeException>(() => vad.Run(pcm));
        Assert.Equal(Status.ErrInvalidArg, ex.StatusCode);
    }

    [SkippableFact]
    public void Feed_NonFiniteAudio_IsRejected()
    {
        Skip.IfNot(Ready, "Silero VAD model or audio not present.");

        using var model = LoadVad();
        using var vad = model.CreateVadSession();

        var pcm = new float[1024];
        pcm[10] = float.PositiveInfinity;

        var ex = Assert.Throws<TranscribeException>(() => vad.Feed(pcm));
        Assert.Equal(Status.ErrInvalidArg, ex.StatusCode);
    }

    [SkippableFact]
    public void ReadResult_BeforeAnyRun_IsEmpty()
    {
        Skip.IfNot(Ready, "Silero VAD model or audio not present.");

        using var model = LoadVad();
        using var vad = model.CreateVadSession();

        var result = vad.ReadResult();
        Assert.Empty(result.Probabilities);
        Assert.Empty(result.Segments);
        Assert.Equal(0, result.FirstFrame);
    }

    [SkippableFact]
    public void Run_AbortedMidRun_ThrowsOperationCanceledException()
    {
        Skip.IfNot(Ready, "Silero VAD model or audio not present.");

        using var model = LoadVad();
        using var vad = model.CreateVadSession();

        // Minutes of audio so the native run keeps going long enough to poll the
        // abort callback after the token fires (measured ~1.8 s here).
        float[] one = Pcm();
        var longPcm = new float[one.Length * 200];
        for (int i = 0; i < 200; i++)
        {
            one.CopyTo(longPcm, i * one.Length);
        }

        // Cancelled from a dedicated thread, not a thread-pool timer: a loaded CI
        // can starve the timer callback past the end of the run. See
        // MidRunCancellation.
        using var cancel = new MidRunCancellation(TimeSpan.FromMilliseconds(10));
        Assert.Throws<OperationCanceledException>(() => vad.Run(longPcm, ct: cancel.Token));
    }
}
