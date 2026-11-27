namespace TranscribeCppSharp.Voiceprint.Tests;

using System;
using System.Collections.Generic;
using TranscribeCppSharp.Audio;
using TranscribeCppSharp.SharedSource;
using TranscribeCppSharp.Voiceprint;
using Xunit;

public sealed class SpeakerNameMapperTests
{
    [Fact]
    public void SpeakerAssignmentJoinsNumericIdsAndMapperNamesOnlyKnown()
    {
        var segment = new MergedSegment(TimeSpan.Zero, TimeSpan.FromSeconds(1), "hello", SpeakerId: 0);
        var turn = new SpeakerSegmentResult(TimeSpan.Zero, TimeSpan.FromSeconds(1), SpeakerId: 1, Probability: 0.9f);

        List<MergedSegment> assigned = SpeakerAssignment.Apply([segment], [turn]);

        Assert.Equal(1, Assert.Single(assigned).SpeakerId); // numeric per-run id, not PersonId

        var identities = new[]
        {
            new SpeakerIdentity(1, KnownMatch("p1", "Alice")),
            new SpeakerIdentity(2, SpeakerMatch.Unknown()),
        };

        Dictionary<int, string> names = SpeakerNameMapper.KnownSpeakerNames(identities);

        Assert.Equal("Alice", names[1]);
        Assert.False(names.ContainsKey(2)); // unknown/ambiguous keep Speaker N
    }

    [Fact]
    public void SpeakerZeroAndBlankNamesAreNotNamed()
    {
        var identities = new[]
        {
            new SpeakerIdentity(0, KnownMatch("p0", "Nobody")),
            new SpeakerIdentity(3, KnownMatch("p3", " ")),
        };

        Assert.Empty(SpeakerNameMapper.KnownSpeakerNames(identities));
    }

    private static SpeakerMatch KnownMatch(string personId, string displayName) => new(
        SpeakerIdentityDecision.Known,
        personId,
        displayName,
        BestScore: 1.0,
        SecondBestScore: double.NaN,
        Margin: double.PositiveInfinity,
        ReverifiedCount: 0);
}
