#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using TranscribeCppSharp.Audio;
using Xunit;

namespace TranscribeCppSharp.Interop.Tests;

/// <summary>
/// Tests for the window merge. The seams are where a split run goes wrong: the
/// native layer reports each window in its own time base, so timestamps have to
/// be shifted and the repeated tail has to be dropped. Getting the sentinel or
/// the tolerance wrong is silent — it drops or doubles text, it does not throw.
/// </summary>
public class TranscriptMergerTests
{
    private static readonly AudioWindow First = new(0, WindowPlanner.SampleRate * 10);
    private static readonly AudioWindow Second = new(WindowPlanner.SampleRate * 9, WindowPlanner.SampleRate * 10);

    private static Transcript Of(params SegmentResult[] segments)
        => new() { Segments = segments };

    private static SegmentResult At(double startMs, double endMs, string text)
        => new(TimeSpan.FromMilliseconds(startMs), TimeSpan.FromMilliseconds(endMs), text);

    [Fact]
    public void ASingleSegmentOfTheFirstWindowIsKept()
    {
        // The regression this guards: a "nothing kept yet" sentinel that cannot
        // survive the SeamToleranceMs subtraction makes the first window look
        // like a seam and drops all of it.
        var merger = new TranscriptMerger();

        IReadOnlyList<MergedSegment> kept = merger.Add(First, Of(At(0, 1000, "hello")));

        Assert.Single(kept);
        Assert.Equal("hello", kept[0].Text);
    }

    [Fact]
    public void ASingleWindowKeepsEverySegment()
    {
        var merger = new TranscriptMerger();

        merger.Add(First, Of(At(0, 1000, "one"), At(1000, 2000, "two"), At(2000, 3000, "three")));

        Assert.Equal(["one", "two", "three"], merger.Segments.Select(s => s.Text));
    }

    [Fact]
    public void TimestampsAreShiftedIntoTheWholeFile()
    {
        var merger = new TranscriptMerger();

        IReadOnlyList<MergedSegment> kept = merger.Add(Second, Of(At(0, 1000, "second window")));

        Assert.Equal(TimeSpan.FromMilliseconds(9000), kept[0].Start);
        Assert.Equal(TimeSpan.FromMilliseconds(10_000), kept[0].End);
    }

    [Fact]
    public void TheRepeatedTailOfTheOverlapIsDropped()
    {
        // Second starts 1 s before First ends. Its first second repeats audio
        // already reported, so it must not appear twice.
        var merger = new TranscriptMerger();
        merger.Add(First, Of(At(9000, 10_000, "the end of window one")));
        merger.Add(Second, Of(At(0, 1000, "the repeat"), At(1000, 2000, "new audio")));

        Assert.Equal(["the end of window one", "new audio"], merger.Segments.Select(s => s.Text));
    }

    [Fact]
    public void ASmallMisalignmentAcrossTheSeamStillKeepsBothSides()
    {
        // The two windows do not cut at exactly the same place, so a segment can
        // start slightly before the previous one ended. The tolerance is what
        // stops the seam dropping real speech.
        var merger = new TranscriptMerger();
        merger.Add(First, Of(At(9000, 10_000, "before the seam")));
        merger.Add(Second, Of(At(0, 900, "slightly before"), At(900, 2000, "after the seam")));

        Assert.Equal(2, merger.Segments.Count);
    }

    [Fact]
    public void AWindowRepeatingWellBeforeTheSeamIsDropped()
    {
        var merger = new TranscriptMerger();
        merger.Add(First, Of(At(5000, 10_000, "long segment")));
        merger.Add(Second, Of(At(0, 4000, "well before the seam")));

        Assert.Equal(["long segment"], merger.Segments.Select(s => s.Text));
    }

    [Fact]
    public void BuildTextJoinsTheKeptSegments()
    {
        var merger = new TranscriptMerger();
        merger.Add(First, Of(At(0, 1000, "  Hello there.  ")));
        merger.Add(Second, Of(At(1000, 2000, "General Kenobi.")));

        Assert.Equal("Hello there. General Kenobi.", merger.BuildText());
    }

    [Fact]
    public void BuildTextOfARunWithNoSegmentsIsEmpty()
    {
        var merger = new TranscriptMerger();

        merger.Add(First, Of());

        Assert.Equal(string.Empty, merger.BuildText());
    }

    [Fact]
    public void ANonRetainingMergerStillReportsTheKeptSegments()
    {
        // This is the mode the CLI uses: it writes each window out as it goes,
        // so it needs the seam decision but not the accumulated list.
        var merger = new TranscriptMerger(retain: false);
        merger.Add(First, Of(At(9000, 10_000, "one")));

        IReadOnlyList<MergedSegment> kept = merger.Add(Second, Of(At(0, 1000, "repeat"), At(1000, 2000, "two")));

        Assert.Equal(["two"], kept.Select(s => s.Text));
        Assert.Empty(merger.Segments);
        Assert.Equal(string.Empty, merger.BuildText());
    }

    [Fact]
    public void SpeakerIdsSurviveTheMerge()
    {
        var merger = new TranscriptMerger();

        merger.Add(First, Of(new SegmentResult(TimeSpan.Zero, TimeSpan.FromSeconds(1), "a", 1)));
        merger.Add(Second, Of(new SegmentResult(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), "b", 2)));

        Assert.Equal([1, 2], merger.Segments.Select(s => s.SpeakerId));
    }

    [Fact]
    public void WordsAndSpeakerRowsAreShiftedIntoTheWholeFile()
    {
        var window = Second;
        var word = new WordResult(TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(400), "second");
        var speaker = new SpeakerSegmentResult(TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(400), 1, 0.9f);

        WordResult shiftedWord = TranscriptMerger.Shift(window, word);
        SpeakerSegmentResult shiftedSpeaker = TranscriptMerger.Shift(window, speaker);

        Assert.Equal(TimeSpan.FromMilliseconds(9100), shiftedWord.Start);
        Assert.Equal(TimeSpan.FromMilliseconds(9400), shiftedWord.End);
        Assert.Equal("second", shiftedWord.Text);
        Assert.Equal(TimeSpan.FromMilliseconds(9100), shiftedSpeaker.Start);
        Assert.Equal(1, shiftedSpeaker.SpeakerId);
        Assert.Equal(0.9f, shiftedSpeaker.Probability);
    }

    [Fact]
    public void AddingWindowsOutOfOrderStillReportsAbsoluteTimes()
    {
        // Not a supported use — the merger is sequential — but the timestamps it
        // reports are a function of the window alone, so they do not depend on
        // what was merged before.
        var merger = new TranscriptMerger();

        merger.Add(First, Of(At(0, 1000, "first")));
        IReadOnlyList<MergedSegment> kept = merger.Add(Second, Of(At(2000, 3000, "later")));

        Assert.Equal(TimeSpan.FromMilliseconds(11_000), kept[0].Start);
    }
}