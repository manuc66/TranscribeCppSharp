namespace TranscribeCppSharp.Voiceprint.Tests;

using System;
using System.Collections.Generic;
using TranscribeCppSharp.Audio;
using TranscribeCppSharp.Voiceprint;
using Xunit;

public sealed class SpeakerNameMappingSafetyTests
{
    [Fact]
    public void OnlyKnownNonZeroNonBlankNamesAreMapped()
    {
        var identities = new[]
        {
            new SpeakerIdentity(1, Known("p1", "Alice")),
            new SpeakerIdentity(2, SpeakerMatch.Unknown()),
            new SpeakerIdentity(3, Known("p3", " ")),
            new SpeakerIdentity(0, Known("p0", "Nobody")),
        };

        Dictionary<int, string> names = SpeakerNameMapper.KnownSpeakerNames(identities);

        Assert.Equal("Alice", names[1]);
        Assert.False(names.ContainsKey(2));
        Assert.False(names.ContainsKey(3));
        Assert.False(names.ContainsKey(0));
    }

    [Fact]
    public void RetroactiveNamesRequireIdentifiedSourceAndDoNotRewriteOriginal()
    {
        UnknownSpeaker pending = Unknown(UnknownSpeakerStatus.Pending);
        UnknownSpeaker identified = Unknown(UnknownSpeakerStatus.Identified) with { PersonId = "person-1", DisplayName = "Alice" };
        var original = new[] { new MergedSegment(TimeSpan.Zero, TimeSpan.FromSeconds(1), "hello", SpeakerId: 1) };

        Dictionary<int, string> names = RetroactiveSpeakerRelabeler.NamesForTranscript("t1", original, [pending, identified]);

        Assert.Equal("Alice", names[1]);
        Assert.Equal(1, original[0].SpeakerId);
        Assert.Equal("hello", original[0].Text);
        Assert.False(names.ContainsKey(0));
    }

    private static SpeakerMatch Known(string personId, string displayName) => new(
        SpeakerIdentityDecision.Known,
        personId,
        displayName,
        BestScore: 1.0,
        SecondBestScore: double.NaN,
        Margin: double.PositiveInfinity,
        ReverifiedCount: 0);

    private static UnknownSpeaker Unknown(UnknownSpeakerStatus status) => new(
        "unknown-1",
        "model-v1",
        Dimension: 2,
        Embeddings: [[1f, 0f]],
        Sources: [new UnknownSpeakerSource("t1", 1, TimeSpan.Zero, TimeSpan.FromSeconds(1), float.NaN, "sha", string.Empty, string.Empty)],
        FirstSeen: DateTimeOffset.UnixEpoch,
        LastSeen: DateTimeOffset.UnixEpoch,
        status,
        PersonId: null,
        DisplayName: "Unknown",
        SourceSampleCount: 1);
}
