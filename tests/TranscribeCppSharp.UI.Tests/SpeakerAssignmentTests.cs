using TranscribeCppSharp.Audio;
using TranscribeCppSharp.UI.Services;
using Xunit;

namespace TranscribeCppSharp.UI.Tests;

/// <summary>
/// The join between a transcriber and a separate diarizer: each segment takes the
/// speaker whose turn overlaps it most.
/// </summary>
/// <remarks>
/// This is app-side logic, not the library's — transcribe.cpp has no combined
/// ASR+diarizer entry point — so it is pinned here rather than left to the run.
/// </remarks>
public class SpeakerAssignmentTests
{
    private static MergedSegment Segment(int startMs, int endMs)
        => new(TimeSpan.FromMilliseconds(startMs), TimeSpan.FromMilliseconds(endMs), "text", 0);

    private static SpeakerSegmentResult Turn(int startMs, int endMs, int speaker)
        => new(TimeSpan.FromMilliseconds(startMs), TimeSpan.FromMilliseconds(endMs), speaker, 1f);

    [Fact]
    public void EachSegmentTakesTheSpeakerWithTheLargestOverlap()
    {
        var segments = new[] { Segment(0, 1000), Segment(1000, 2000) };
        var turns = new[] { Turn(0, 600, 1), Turn(600, 2000, 2) };

        List<MergedSegment> result = SpeakerAssignment.Apply(segments, turns);

        // 0-1000 overlaps speaker 1 by 600 ms and speaker 2 by 400 ms.
        Assert.Equal(1, result[0].SpeakerId);
        Assert.Equal(2, result[1].SpeakerId);
    }

    [Fact]
    public void ASegmentSpanningASpeakerChangeTakesTheMajority()
    {
        var segments = new[] { Segment(0, 1000) };
        var turns = new[] { Turn(0, 200, 1), Turn(200, 1000, 2) };

        List<MergedSegment> result = SpeakerAssignment.Apply(segments, turns);

        Assert.Equal(2, result[0].SpeakerId);
    }

    [Fact]
    public void ASegmentWithNoOverlappingTurnKeepsItsSpeaker()
    {
        var segments = new[] { Segment(0, 1000) };
        var turns = new[] { Turn(2000, 3000, 3) };

        List<MergedSegment> result = SpeakerAssignment.Apply(segments, turns);

        // The diarizer never attributed this stretch, so nothing is invented.
        Assert.Equal(0, result[0].SpeakerId);
    }

    [Fact]
    public void NoTurnsLeavesTheSegmentsAlone()
    {
        var segments = new[] { Segment(0, 1000) };

        List<MergedSegment> result = SpeakerAssignment.Apply(segments, Array.Empty<SpeakerSegmentResult>());

        Assert.Equal(0, result[0].SpeakerId);
        Assert.Equal("text", result[0].Text);
    }
}
