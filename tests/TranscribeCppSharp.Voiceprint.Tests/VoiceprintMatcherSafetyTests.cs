namespace TranscribeCppSharp.Voiceprint.Tests;

using System;
using TranscribeCppSharp.Voiceprint;
using Xunit;

public sealed class VoiceprintMatcherSafetyTests
{
    private static readonly AmbiguityPolicy Policy = new(0.75, 0.10, 1);

    [Fact]
    public void ModelDimensionAndEmptyCandidatesCannotMatch()
    {
        Voiceprint alice = Print("p1", "Alice", "model-v1", 2, [1f, 0f]);

        Assert.Equal(SpeakerIdentityDecision.Unknown, VoiceprintMatcher.Match([1f, 0f], "model-v2", [alice], Policy).Decision);
        Assert.Equal(SpeakerIdentityDecision.Unknown, VoiceprintMatcher.Match([1f, 0f, 0f], "model-v1", [alice], Policy).Decision);
        Assert.Equal(SpeakerIdentityDecision.Unknown, VoiceprintMatcher.Match([1f, 0f], "model-v1", [], Policy).Decision);
        Assert.Equal(SpeakerIdentityDecision.Unknown, VoiceprintMatcher.Match([], "model-v1", [alice], Policy).Decision);
    }

    [Fact]
    public void NonFiniteQueryAndPolicyAreRejected()
    {
        Voiceprint alice = Print("p1", "Alice", "model-v1", 2, [1f, 0f]);

        Assert.Throws<ArgumentException>(() => VoiceprintMatcher.Match([float.NaN, 0f], "model-v1", [alice], Policy));
        Assert.Throws<ArgumentException>(() => VoiceprintMatcher.Match([1f, float.PositiveInfinity], "model-v1", [alice], Policy));
        Assert.Throws<ArgumentException>(() => VoiceprintMatcher.MatchWithRechecks([1f, 0f], "model-v1", [alice], Policy, [[float.NaN, 0f]]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AmbiguityPolicy(double.NaN, 0.0).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new AmbiguityPolicy(1.1, 0.0).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new AmbiguityPolicy(0.0, double.PositiveInfinity).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new AmbiguityPolicy(0.0, -0.1).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new AmbiguityPolicy(0.0, 0.0, -1).Validate());
    }

    [Fact]
    public void KnownIsScoreThresholdNotProofOfRealPerson()
    {
        Voiceprint alice = Print("p1", "Alice", "model-v1", 2, [1f, 0f]);
        Voiceprint bob = Print("p2", "Bob", "model-v1", 2, [0f, 1f]);

        // Wrong saved person can still be `Known` under score+margin; separate `PersonCorrect` required.
        SpeakerMatch wrong = VoiceprintMatcher.Match([0f, 1f], "model-v1", [alice, bob], Policy);

        Assert.Equal(SpeakerIdentityDecision.Known, wrong.Decision);
        Assert.Equal("p2", wrong.PersonId); // actual top-1, not expected `p1`
        Assert.DoesNotContain("PersonCorrect", typeof(SpeakerMatch).GetProperties().Select(p => p.Name));
    }

    [Fact]
    public void UnknownVsAmbiguousAndMarginAreDistinct()
    {
        Voiceprint p1 = Print("p1", "A", "model-v1", 2, [1f, 0f]);
        Voiceprint p2 = Print("p2", "B", "model-v1", 2, [1f, 0f]);

        SpeakerMatch ambiguous = VoiceprintMatcher.Match([1f, 0f], "model-v1", [p1, p2], Policy);
        SpeakerMatch unknownLowScore = VoiceprintMatcher.Match([0f, 1f], "model-v1", [p1], Policy with { MinScore = 0.99, MinMargin = 0.0 });
        SpeakerMatch unknownNoCandidate = VoiceprintMatcher.Match([1f, 0f], "model-v1", [], Policy);

        Assert.Equal(SpeakerIdentityDecision.Ambiguous, ambiguous.Decision);
        Assert.True(ambiguous.NeedsReverification);
        Assert.Equal(SpeakerIdentityDecision.Unknown, unknownLowScore.Decision);
        Assert.Equal(SpeakerIdentityDecision.Unknown, unknownNoCandidate.Decision);
        Assert.True(double.IsNaN(unknownNoCandidate.BestScore));
        Assert.True(double.IsNaN(unknownNoCandidate.Margin));
    }

    [Fact]
    public void SavedSampleAndZeroNormCannotProduceKnown()
    {
        Voiceprint invalid = Print("p1", "A", "model-v1", 2, [float.NaN, 0f]);
        Voiceprint zero = Print("p2", "B", "model-v1", 2, [0f, 0f]);

        Assert.Equal(SpeakerIdentityDecision.Unknown, VoiceprintMatcher.Match([1f, 0f], "model-v1", [invalid], Policy).Decision);
        Assert.Equal(SpeakerIdentityDecision.Unknown, VoiceprintMatcher.Match([1f, 0f], "model-v1", [zero], Policy).Decision);
    }

    [Fact]
    public void TieBreakingIsStable()
    {
        Voiceprint p1 = Print("p1", "A", "model-v1", 2, [1f, 0f]);
        Voiceprint p2 = Print("p2", "B", "model-v1", 2, [1f, 0f]);

        SpeakerMatch match = VoiceprintMatcher.Match([1f, 0f], "model-v1", [p2, p1], Policy with { MinMargin = 0.0 });

        Assert.Equal("p1", match.PersonId); // equal score -> `PersonId` Ordinal
    }

    [Fact]
    public void RechecksAreBoundedAndReverifiedCountPropagated()
    {
        Voiceprint p1 = Print("p1", "A", "model-v1", 2, [1f, 0f]);
        var policy = Policy with { MaxRechecks = 1, MinScore = 0.99 };

        SpeakerMatch match = VoiceprintMatcher.MatchWithRechecks(
            [0.8f, 0.2f],
            "model-v1",
            [p1],
            policy,
            [[1f, 0f], [1f, 0f], [1f, 0f]]); // only first recheck considered

        Assert.Equal(1, match.ReverifiedCount);
    }

    private static Voiceprint Print(string id, string name, string modelId, int dimension, float[] embedding) => new(
        id,
        name,
        modelId,
        dimension,
        [embedding],
        DateTimeOffset.UnixEpoch,
        SourceSampleCount: 1);
}
