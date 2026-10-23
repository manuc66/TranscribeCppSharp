using TranscribeCppSharp;
using TranscribeCppSharp.Audio;
using TranscribeCppSharp.SharedSource;
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

    private static WordResult Word(int startMs, int endMs, string text = "word")
        => new(TimeSpan.FromMilliseconds(startMs), TimeSpan.FromMilliseconds(endMs), text);

    [Fact]
    public void WordsPickTheMajorityWhenTheSegmentLevelOverlapWouldPickTheOther()
    {
        // The segment overlaps speaker 2 for 600 ms and speaker 1 for 400 ms, so
        // the segment-level rule gives it to speaker 2. The words say two of the
        // three were speaker 1's; that is the case word timings exist for.
        var segments = new[] { Segment(0, 1000) };
        var turns = new[] { Turn(0, 400, 1), Turn(400, 1000, 2) };
        var words = new[]
        {
            Word(0, 300, "one"),
            Word(300, 390, "two"),
            Word(410, 1000, "three"),
        };

        List<MergedSegment> result = SpeakerAssignment.Apply(segments, turns, words);

        Assert.Equal(1, result[0].SpeakerId);
    }

    [Fact]
    public void AWordLevelTieGoesToTheLongerSpeechThenTheLowerId()
    {
        var segments = new[] { Segment(0, 1000) };
        var turns = new[] { Turn(0, 500, 1), Turn(500, 1000, 2) };
        var words = new[]
        {
            Word(0, 100, "short"),
            Word(800, 1000, "longer"),
        };

        List<MergedSegment> result = SpeakerAssignment.Apply(segments, turns, words);

        Assert.Equal(2, result[0].SpeakerId);
    }

    [Fact]
    public void ASegmentWithoutWordsStillUsesTheSegmentOverlap()
    {
        var segments = new[] { Segment(0, 1000) };
        var turns = new[] { Turn(0, 200, 1), Turn(200, 1000, 2) };
        var words = new[] { Word(2000, 2200, "elsewhere") };

        List<MergedSegment> result = SpeakerAssignment.Apply(segments, turns, words);

        Assert.Equal(2, result[0].SpeakerId);
    }

    [Fact]
    public void WordsThatOverlapNoTurnFallBackToTheSegment()
    {
        var segments = new[] { Segment(0, 1000) };
        // The turn covers only the first 100 ms; the words sit after it. The
        // segment-level overlap still knows the turn, so the segment is not left
        // unattributed just because the words missed it.
        var turns = new[] { Turn(0, 100, 2) };
        var words = new[] { Word(200, 300, "one"), Word(400, 500, "two") };

        List<MergedSegment> result = SpeakerAssignment.Apply(segments, turns, words);

        Assert.Equal(2, result[0].SpeakerId);
    }
}
