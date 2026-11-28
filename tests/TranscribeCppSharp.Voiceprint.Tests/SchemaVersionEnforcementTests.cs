namespace TranscribeCppSharp.Voiceprint.Tests;

using System;
using TranscribeCppSharp.Voiceprint;
using Xunit;

public sealed class SchemaVersionEnforcementTests
{
    private static readonly AmbiguityPolicy Policy = new(0.75, 0.0);

    [Fact]
    public void VoiceprintMatcherRejectsUnknownSchemaVersion()
    {
        var valid = Print(schemaVersion: "1");
        var invalid = valid with { SchemaVersion = "2" };

        Assert.Equal(SpeakerIdentityDecision.Known, VoiceprintMatcher.Match([1f, 0f], "model-v1", [valid], Policy).Decision);
        Assert.Equal(SpeakerIdentityDecision.Unknown, VoiceprintMatcher.Match([1f, 0f], "model-v1", [invalid], Policy).Decision);
        Assert.Throws<ArgumentException>(() => VoiceprintMatcher.Match([1f, 0f], "model-v1", [valid with { SchemaVersion = string.Empty }], Policy));
    }

    [Fact]
    public void UnknownSpeakerMatcherRejectsUnknownSchemaVersion()
    {
        UnknownSpeaker pending = new(
            "unknown-1",
            "model-v1",
            2,
            [[1f, 0f]],
            [],
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            UnknownSpeakerStatus.Pending,
            PersonId: null,
            DisplayName: "Unknown",
            SourceSampleCount: 1,
            SchemaVersion: "2");

        UnknownSpeakerMatch match = UnknownSpeakerMatcher.MatchPending([1f, 0f], "model-v1", [pending], Policy);

        Assert.False(match.Matched);
    }

    private static Voiceprint Print(string schemaVersion) => new(
        "p1",
        "Alice",
        "model-v1",
        2,
        [[1f, 0f]],
        DateTimeOffset.UnixEpoch,
        SourceSampleCount: 1,
        schemaVersion);
}
