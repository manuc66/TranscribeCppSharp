namespace MeetingAnalyticsEval.Tests;

using System;
using System.Collections.Generic;
using MeetingAnalyticsEval;
using TranscribeCppSharp.MeetingAnalytics;
using TranscribeCppSharp.MeetingAnalytics.Voiceprint;
using TranscribeCppSharp.Voiceprint;
using Xunit;

public sealed class MeetingEvalEvaluatorTests
{
    [Fact]
    public void EvaluatesKnownTargetAndUnknownNonTarget()
    {
        var meeting = new MeetingEvalCase
        {
            Audio = "meeting.wav",
            Language = "fr",
            Condition = "clean",
            ExpectedSpeakers = new Dictionary<int, string?> { [1] = "p1", [2] = null },
        };
        var speakers = new[] { Stats(1), Stats(2) };
        var labels = new[]
        {
            new MeetingSpeakerLabel(1, "p1", "Alice", MeetingSpeakerIdentityStatus.Known),
            new MeetingSpeakerLabel(2, null, "Unknown x", MeetingSpeakerIdentityStatus.Unknown, "unknown-x"),
        };
        var identities = new[]
        {
            new SpeakerIdentity(1, Known("p1", "Alice")),
            new SpeakerIdentity(2, SpeakerMatch.Unknown()),
        };
        var analytics = new MeetingAnalytics(
            new MeetingSummary(TimeSpan.FromSeconds(10), 2, TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2), TimeSpan.Zero, 0, 1, 0.5, 1, 0, 0, 0),
            speakers,
            [],
            [],
            [],
            [],
            [],
            0,
            labels);
        var resolution = new MeetingSpeakerResolution(analytics, identities, [], new Dictionary<int, string> { [1] = "Alice" });

        MeetingEvalResult result = MeetingEvalEvaluator.Evaluate(
            meeting,
            "meeting-1",
            TimeSpan.FromSeconds(10),
            analytics,
            resolution,
            resolveWallMs: 10,
            resolveCpuMs: 12);

        Assert.Equal(2, result.EvaluatedSpeakerCount);
        Assert.Equal(1, result.TargetCount);
        Assert.Equal(1, result.NonTargetCount);
        Assert.Equal(1, result.UnknownCount);
        Assert.Equal(1, result.PersonCorrectCount);
        Assert.Equal(1, result.TrueAccept);
        Assert.Equal(0, result.FalseAccept);
        Assert.Equal(0, result.FalseReject);
        Assert.Equal(1, result.TrueReject);
        Assert.Equal(1, result.UnknownRejected);
        Assert.Equal("unknown-x", result.Speakers[1].UnknownId);
    }

    private static SpeakerTalkStats Stats(int speakerId) => new(
        speakerId,
        TimeSpan.FromSeconds(5),
        0.5,
        1,
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(5),
        TimeSpan.Zero,
        TimeSpan.FromSeconds(5),
        0,
        0);

    private static SpeakerMatch Known(string personId, string displayName) => new(
        SpeakerIdentityDecision.Known,
        personId,
        displayName,
        BestScore: 0.9,
        SecondBestScore: 0.5,
        Margin: 0.4,
        ReverifiedCount: 0);
}
