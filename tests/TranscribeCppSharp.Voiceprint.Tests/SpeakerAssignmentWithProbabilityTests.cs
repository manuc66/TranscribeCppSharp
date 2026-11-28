namespace TranscribeCppSharp.Voiceprint.Tests;

using System;
using System.Collections.Generic;
using TranscribeCppSharp;
using TranscribeCppSharp.Audio;
using TranscribeCppSharp.Voiceprint;
using Xunit;

public sealed class SpeakerAssignmentWithProbabilityTests
{
    [Fact]
    public void ProbabilityIsPropagatedBesideMergedSegmentNotInsideIt()
    {
        var segment = new MergedSegment(TimeSpan.Zero, TimeSpan.FromSeconds(1), "hello", SpeakerId: 0);
        var turn = new SpeakerSegmentResult(TimeSpan.Zero, TimeSpan.FromSeconds(1), SpeakerId: 1, Probability: 0.75f);

        AssignedSpeakerSegment assigned = Assert.Single(SpeakerAssignmentWithProbability.Apply([segment], [turn]));

        Assert.Equal(1, assigned.SpeakerId);
        Assert.Equal(0.75f, assigned.DiarizationProbability);
        Assert.Equal("value", assigned.DiarizationProbabilityState);
        Assert.Equal("hello", assigned.Segment.Text);
        Assert.DoesNotContain("Probability", typeof(MergedSegment).GetProperties().Select(p => p.Name));
        Assert.DoesNotContain("PersonId", typeof(AssignedSpeakerSegment).GetProperties().Select(p => p.Name));
    }

    [Fact]
    public void NaNAndNoTurnAreHonest()
    {
        var segment = new MergedSegment(TimeSpan.Zero, TimeSpan.FromSeconds(1), "hello", SpeakerId: 0);
        var turn = new SpeakerSegmentResult(TimeSpan.Zero, TimeSpan.FromSeconds(1), SpeakerId: 1, Probability: float.NaN);

        AssignedSpeakerSegment withNaN = Assert.Single(SpeakerAssignmentWithProbability.Apply([segment], [turn]));
        AssignedSpeakerSegment noTurn = Assert.Single(SpeakerAssignmentWithProbability.Apply([segment], []));

        Assert.True(float.IsNaN(withNaN.DiarizationProbability!.Value));
        Assert.Equal("NaN", withNaN.DiarizationProbabilityState);
        Assert.Null(noTurn.DiarizationProbability); // no invented probability
        Assert.Null(noTurn.DiarizationProbabilityState);
        Assert.Equal(0, noTurn.SpeakerId); // SpeakerAssignment no-turn rule
    }
}
