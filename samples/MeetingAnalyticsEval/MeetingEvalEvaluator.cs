namespace MeetingAnalyticsEval;

using System;
using System.Collections.Generic;
using System.Linq;
using TranscribeCppSharp.MeetingAnalytics;
using TranscribeCppSharp.MeetingAnalytics.Voiceprint;
using TranscribeCppSharp.Voiceprint;

/// <summary>Evaluates resolved meeting speakers against corpus expectations.</summary>
public static class MeetingEvalEvaluator
{
    public static MeetingEvalResult Evaluate(
        MeetingEvalCase meeting,
        string meetingId,
        TimeSpan duration,
        MeetingAnalytics analytics,
        MeetingSpeakerResolution resolution,
        double resolveWallMs,
        double resolveCpuMs)
    {
        ArgumentNullException.ThrowIfNull(meeting);
        ArgumentNullException.ThrowIfNull(analytics);
        ArgumentNullException.ThrowIfNull(resolution);

        Dictionary<int, SpeakerIdentity> identities = resolution.Identities.ToDictionary(i => i.RunSpeakerId);
        Dictionary<int, MeetingSpeakerLabel> labels = analytics.SpeakerLabels.ToDictionary(l => l.SpeakerId);
        var speakers = new List<MeetingEvalSpeakerResult>();

        foreach (SpeakerTalkStats stats in analytics.Speakers)
        {
            SpeakerIdentity identity = identities[stats.SpeakerId];
            MeetingSpeakerLabel label = labels[stats.SpeakerId];
            bool hasExpectation = meeting.ExpectedSpeakers.TryGetValue(stats.SpeakerId, out string? expected);
            bool target = hasExpectation && expected is not null;
            bool nonTarget = hasExpectation && !target;
            bool personCorrect = target && string.Equals(expected, identity.PersonId, StringComparison.Ordinal);

            speakers.Add(new MeetingEvalSpeakerResult(
                meetingId,
                stats.SpeakerId,
                hasExpectation,
                expected,
                identity.PersonId,
                personCorrect,
                target,
                nonTarget,
                identity.Decision,
                identity.Match.BestScore,
                identity.Match.SecondBestScore,
                identity.Match.Margin,
                identity.Match.ReverifiedCount,
                label.UnknownId));
        }

        List<MeetingEvalSpeakerResult> evaluated = speakers.Where(s => s.HasExpectation).ToList();
        int trueAccept = evaluated.Count(s => s.Decision == SpeakerIdentityDecision.Known && s.PersonCorrect);
        int falseAccept = evaluated.Count(s => s.Decision == SpeakerIdentityDecision.Known && !s.PersonCorrect);
        int falseReject = evaluated.Count(s => s.Decision != SpeakerIdentityDecision.Known && s.Target);
        int trueReject = evaluated.Count(s => s.Decision != SpeakerIdentityDecision.Known && s.NonTarget);
        int unknownRejected = evaluated.Count(s => s.ExpectedPersonId is null && s.Decision != SpeakerIdentityDecision.Known);

        return new MeetingEvalResult(
            meetingId,
            meeting.Audio,
            meeting.Language,
            meeting.Condition,
            duration.TotalMilliseconds,
            analytics.Speakers.Count,
            evaluated.Count,
            evaluated.Count(s => s.Target),
            evaluated.Count(s => s.NonTarget),
            evaluated.Count(s => s.ExpectedPersonId is null),
            evaluated.Count(s => s.PersonCorrect),
            trueAccept,
            falseAccept,
            falseReject,
            trueReject,
            unknownRejected,
            evaluated.Count(s => s.Decision == SpeakerIdentityDecision.Ambiguous),
            resolveWallMs,
            resolveCpuMs,
            speakers);
    }

    public static MeetingEvalSummary Summarize(IReadOnlyList<MeetingEvalResult> meetings)
    {
        ArgumentNullException.ThrowIfNull(meetings);

        int totalMeetings = meetings.Count;
        int totalSpeakers = meetings.Sum(m => m.SpeakerCount);
        int evaluated = meetings.Sum(m => m.EvaluatedSpeakerCount);
        int target = meetings.Sum(m => m.TargetCount);
        int nonTarget = meetings.Sum(m => m.NonTargetCount);
        int unknown = meetings.Sum(m => m.UnknownCount);
        int personCorrect = meetings.Sum(m => m.PersonCorrectCount);
        int trueAccept = meetings.Sum(m => m.TrueAccept);
        int falseAccept = meetings.Sum(m => m.FalseAccept);
        int falseReject = meetings.Sum(m => m.FalseReject);
        int trueReject = meetings.Sum(m => m.TrueReject);
        int unknownRejected = meetings.Sum(m => m.UnknownRejected);
        int ambiguous = meetings.Sum(m => m.AmbiguousCount);
        int known = trueAccept + falseAccept;
        double audioSeconds = meetings.Sum(m => m.DurationMs / 1000.0);
        double resolveWall = meetings.Sum(m => m.ResolveWallMs);
        double resolveCpu = meetings.Sum(m => m.ResolveCpuMs);
        double far = falseAccept + trueReject == 0 ? 0.0 : (double)falseAccept / (falseAccept + trueReject);
        double frr = trueAccept + falseReject == 0 ? 0.0 : (double)falseReject / (trueAccept + falseReject);

        return new MeetingEvalSummary(
            totalMeetings,
            totalSpeakers,
            evaluated,
            target,
            nonTarget,
            unknown,
            personCorrect,
            trueAccept,
            falseAccept,
            falseReject,
            trueReject,
            unknownRejected,
            ambiguous,
            far,
            frr,
            unknown == 0 ? double.NaN : (double)unknownRejected / unknown,
            target == 0 ? double.NaN : (double)personCorrect / target,
            evaluated == 0 ? double.NaN : (double)(trueAccept + trueReject) / evaluated,
            (far + frr) / 2.0,
            evaluated == 0 ? double.NaN : (double)known / evaluated,
            audioSeconds,
            resolveWall,
            resolveCpu,
            audioSeconds <= 0 ? double.NaN : resolveCpu / audioSeconds);
    }
}
