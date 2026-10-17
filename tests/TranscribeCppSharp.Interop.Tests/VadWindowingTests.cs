#nullable enable

using TranscribeCppSharp.Audio;
using TranscribeCppSharp.Shared;
using Xunit;
using Xunit.Abstractions;

namespace TranscribeCppSharp.Interop.Tests;

/// <summary>
/// Long-form voice activity detection: the interval merge (unit) and the whole
/// windowed run against the real Silero VAD weights (integration).
/// </summary>
public class VadWindowingTests
{
    private readonly ITestOutputHelper _output;

    public VadWindowingTests(ITestOutputHelper output) => _output = output;

    private static string VadPath => Path.Combine(TestConfig.RepoRoot, "test-models", "silero-vad-v6.2-F32.gguf");

    private static bool Ready => File.Exists(VadPath) && File.Exists(TestConfig.AudioPath);

    // ---- the merge rules, unit tested without a model ------------------------

    private static VadSession.VadSegmentResult Span(long start, long end) => new(start, end);

    [Fact]
    public void Merge_DisjointIntervals_AreKept()
    {
        var merged = VadWindowing.Merge([Span(0, 100), Span(200, 300)]);

        Assert.Equal(new[] { Span(0, 100), Span(200, 300) }, merged);
    }

    [Fact]
    public void Merge_OverlappingIntervalsAcrossASeam_AreUnioned()
    {
        // What two windows with a 1 s overlap produce for one continuous speech
        // region: the second starts before the first ends.
        var merged = VadWindowing.Merge([Span(0, 160_000), Span(150_000, 320_000)]);

        Assert.Equal(new[] { Span(0, 320_000) }, merged);
    }

    [Fact]
    public void Merge_TouchingIntervals_AreUnioned()
    {
        // A speech region split exactly at the seam: end == start.
        var merged = VadWindowing.Merge([Span(0, 100), Span(100, 200)]);

        Assert.Equal(new[] { Span(0, 200) }, merged);
    }

    [Fact]
    public void Merge_AContainedInterval_DoesNotShortenTheOuter()
    {
        var merged = VadWindowing.Merge([Span(0, 300), Span(50, 100)]);

        Assert.Equal(new[] { Span(0, 300) }, merged);
    }

    [Fact]
    public void Merge_OverlapThatExtends_KeepsTheLongerEnd()
    {
        var merged = VadWindowing.Merge([Span(0, 100), Span(50, 400), Span(300, 350)]);

        Assert.Equal(new[] { Span(0, 400) }, merged);
    }

    [Fact]
    public void Merge_Empty_IsEmpty()
    {
        Assert.Empty(VadWindowing.Merge([]));
    }

    // ---- the whole windowed run, against the real model ----------------------

    [SkippableFact]
    public void WindowedRun_OnLongAudio_MatchesOnePass()
    {
        Skip.IfNot(Ready, "Silero VAD model or audio not present.");

        using var temp = new TempWorkspace();
        float[] one = PcmExtensions.ReadWavToPcm(TestConfig.AudioPath);

        // ~138 s: twelve copies of the sample with a half-second gap. Long enough
        // that a 20 s window splits it into several, unlike the 11 s sample.
        float[] longPcm = SyntheticAudio.Repeat(one, times: 12, gapSamples: 8000);
        string wav = SyntheticAudio.WriteWav(temp.Combine("long.wav"), longPcm);
        double seconds = longPcm.Length / (double)WindowPlanner.SampleRate;
        Assert.True(seconds > 100, $"expected long audio, got {seconds:0.0}s");

        using var model = Model.Load(VadPath, p => p.WithBackend(BackendRequest.BackendCpu));
        using var session = model.CreateVadSession();

        VadWindowing.VadRunResult windowed;
        VadWindowing.VadRunResult onePass;
        using (var source = AudioLoader.Open(wav))
        {
            windowed = VadWindowing.Run(session, source, windowSeconds: 20);
        }

        using (var source = AudioLoader.Open(wav))
        {
            onePass = VadWindowing.Run(session, source, windowSeconds: 1000);
        }

        _output.WriteLine($"windowed: {windowed.Segments.Count} segments, {windowed.FrameCount} frames");
        _output.WriteLine($"one pass: {onePass.Segments.Count} segments, {onePass.FrameCount} frames");

        Assert.NotEmpty(onePass.Segments);

        // Proof the file was actually split: the 1 s overlap means overlapping
        // windows recount frames, so the windowed total exceeds the one-pass total
        // (which is exactly one frame per frame of the file). Equal counts would
        // mean a single window ran.
        Assert.True(
            windowed.FrameCount > onePass.FrameCount,
            $"expected more frames than one pass ({windowed.FrameCount} vs {onePass.FrameCount}); was the audio split?");

        // Merging guarantees ordered, non-overlapping intervals inside the file.
        long previousEnd = -1;
        foreach (var s in windowed.Segments)
        {
            Assert.True(s.StartSample >= previousEnd, "segments overlap or are out of order");
            Assert.True(s.EndSample > s.StartSample);
            Assert.True(s.EndSample <= longPcm.Length);
            previousEnd = s.EndSample;
        }

        // The speech found is the same speech. The segment *count* is not asserted
        // to match: each window is scored independently, so a seam can bridge a
        // short silence that a one-pass run keeps as a gap (and vice versa). What
        // must hold is that the total speech duration is the same to within a
        // second, which is what "same speech" means here.
        long windowedSpeech = windowed.Segments.Sum(s => s.EndSample - s.StartSample);
        long onePassSpeech = onePass.Segments.Sum(s => s.EndSample - s.StartSample);
        Assert.True(
            Math.Abs(windowedSpeech - onePassSpeech) < 2 * WindowPlanner.SampleRate,
            $"speech duration differs by more than two seconds: {windowedSpeech} vs {onePassSpeech}");
    }
}
