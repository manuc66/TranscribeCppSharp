namespace TranscribeCppSharp.Voiceprint.Tests;

using System;
using TranscribeCppSharp.Voiceprint;
using Xunit;

public sealed class VoiceprintMatcherTests
{
    private static readonly AmbiguityPolicy Policy = new(MinScore: 0.75, MinMargin: 0.10, MaxRechecks: 1);

    [Fact]
    public void KnownWhenScoreAndMarginPassPolicy()
    {
        var voiceprints = new[] { Print("p1", "Alice", [1f, 0f]), Print("p2", "Bob", [0f, 1f]) };

        SpeakerMatch match = VoiceprintMatcher.Match([1f, 0f], "model-v1", voiceprints, Policy);

        Assert.Equal(SpeakerIdentityDecision.Known, match.Decision);
        Assert.Equal("p1", match.PersonId);
        Assert.Equal("Alice", match.DisplayName);
        Assert.False(match.NeedsReverification);
    }

    [Fact]
    public void AmbiguousWhenMarginIsTooSmall()
    {
        var voiceprints = new[] { Print("p1", "Alice", [1f, 0f]), Print("p2", "Bob", [1f, 0f]) };

        SpeakerMatch match = VoiceprintMatcher.Match([1f, 0f], "model-v1", voiceprints, Policy);

        Assert.Equal(SpeakerIdentityDecision.Ambiguous, match.Decision);
        Assert.True(match.NeedsReverification);
    }

    [Fact]
    public void UnknownWhenModelIdDoesNotMatch()
    {
        var voiceprints = new[] { Print("p1", "Alice", [1f, 0f]) };

        SpeakerMatch match = VoiceprintMatcher.Match([1f, 0f], "other-model", voiceprints, Policy);

        Assert.Equal(SpeakerIdentityDecision.Unknown, match.Decision);
        Assert.Null(match.PersonId);
    }

    [Fact]
    public void BoundedRecheckCanTurnAmbiguousIntoKnown()
    {
        var voiceprints = new[] { Print("p1", "Alice", [1f, 0f]), Print("p2", "Bob", [0f, 1f]) };
        float[] ambiguous = [0.7071f, 0.7071f];
        float[] recheck = [0.99f, 0.01f];
        AmbiguityPolicy policy = Policy with { MinScore = 0.5 };

        SpeakerMatch match = VoiceprintMatcher.MatchWithRechecks(ambiguous, "model-v1", voiceprints, policy, [recheck]);

        Assert.Equal(SpeakerIdentityDecision.Known, match.Decision);
        Assert.Equal(1, match.ReverifiedCount);
    }

    [Fact]
    public void NaNEmbeddingIsRejected()
    {
        var voiceprints = new[] { Print("p1", "Alice", [1f, 0f]) };

        Assert.Throws<ArgumentException>(() => VoiceprintMatcher.Match([float.NaN, 0f], "model-v1", voiceprints, Policy));
    }

    private static Voiceprint Print(string id, string name, float[] embedding) => new(
        id,
        name,
        "model-v1",
        embedding.Length,
        [embedding],
        DateTimeOffset.UnixEpoch,
        SourceSampleCount: 1);
}
