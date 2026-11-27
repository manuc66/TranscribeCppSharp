namespace TranscribeCppSharp.Voiceprint.Tests;

using System;
using System.Collections.Generic;
using TranscribeCppSharp;
using TranscribeCppSharp.Audio;
using TranscribeCppSharp.SharedSource;
using Xunit;

public sealed class SpeakerAssignmentSafetyTests
{
    [Fact]
    public void WordLevelBeatsSegmentLevelMajority()
    {
        var segment = new MergedSegment(TimeSpan.Zero, TimeSpan.FromSeconds(2), "a b", SpeakerId: 0);
        var turns = new[]
        {
            new SpeakerSegmentResult(TimeSpan.Zero, TimeSpan.FromSeconds(2), SpeakerId: 1, Probability: 1f),
            new SpeakerSegmentResult(TimeSpan.Zero, TimeSpan.FromMilliseconds(100), SpeakerId: 2, Probability: 1f),
        };
        var words = new[]
        {
            new WordResult(TimeSpan.Zero, TimeSpan.FromMilliseconds(100), "a"),
            new WordResult(TimeSpan.FromMilliseconds(1500), TimeSpan.FromMilliseconds(1600), "b"),
        };

        List<MergedSegment> assigned = SpeakerAssignment.Apply([segment], turns, words);

        // word count picks speaker1, despite speaker2 having shorter noise turn; not identity
        Assert.Equal(1, Assert.Single(assigned).SpeakerId);
    }

    [Fact]
    public void SegmentFallbackTiesAndNoTurnAreDeterministic()
    {
        var segment = new MergedSegment(TimeSpan.Zero, TimeSpan.FromSeconds(2), "x", SpeakerId: 7);
        var equalTurns = new[]
        {
            new SpeakerSegmentResult(TimeSpan.Zero, TimeSpan.FromSeconds(1), SpeakerId: 2, Probability: 1f),
            new SpeakerSegmentResult(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), SpeakerId: 1, Probability: 1f),
        };

        Assert.Equal(1, Assert.Single(SpeakerAssignment.Apply([segment], equalTurns)).SpeakerId); // lower id tie
        Assert.Equal(7, Assert.Single(SpeakerAssignment.Apply([segment], [])).SpeakerId); // no invented attribution
        Assert.Equal(0, Assert.Single(SpeakerAssignment.Apply([segment with { SpeakerId = 0 }], [])).SpeakerId);
    }

    [Fact]
    public void NoProbabilityPropagationNoPersonIdNoSpeakerRewrite()
    {
        var segment = new MergedSegment(TimeSpan.Zero, TimeSpan.FromSeconds(1), "hello", SpeakerId: 0);
        var turn = new SpeakerSegmentResult(TimeSpan.Zero, TimeSpan.FromSeconds(1), SpeakerId: 1, Probability: float.NaN);

        MergedSegment assigned = Assert.Single(SpeakerAssignment.Apply([segment], [turn]));

        Assert.Equal(1, assigned.SpeakerId);
        Assert.Equal("hello", assigned.Text);
        Assert.DoesNotContain("Probability", typeof(MergedSegment).GetProperties().Select(p => p.Name));
        Assert.DoesNotContain("PersonId", typeof(MergedSegment).GetProperties().Select(p => p.Name));
    }
}
