namespace TranscribeCppSharp.MeetingAnalytics;

using System;
using System.Collections.Generic;
using System.Linq;
using TranscribeCppSharp;

/// <summary>Anonymous per-recording speaker analytics. No `PersonId`, no `nl`, no biometric matching.</summary>
public static class MeetingAnalyzer
{
    /// <summary>Analyzes a transcript's speaker turns and words. Duration inferred from latest turn/word.</summary>
    public static MeetingAnalytics Analyze(Transcript transcript, MeetingAnalyticsOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        return Analyze(transcript.SpeakerSegments, transcript.Words, options);
    }

    /// <summary>
    /// Analyzes speaker turns. `Speaker 0`, invalid/reversed turns, and negative-start turns are ignored.
    /// `SpeakerId` is per-recording anonymous; `Probability` is not identity and may be NaN.
    /// </summary>
    public static MeetingAnalytics Analyze(
        IReadOnlyList<SpeakerSegmentResult> turns,
        IReadOnlyList<WordResult> words,
        MeetingAnalyticsOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(turns);
        ArgumentNullException.ThrowIfNull(words);
        options ??= MeetingAnalyticsOptions.Default;
        options.Validate();

        var valid = new List<TurnInterval>();
        int ignored = 0;

        foreach (SpeakerSegmentResult turn in turns)
        {
            if (turn is null || turn.SpeakerId == 0 || turn.Start < TimeSpan.Zero || turn.End <= turn.Start)
            {
                ignored++;
                continue;
            }

            valid.Add(new TurnInterval(turn.SpeakerId, turn.Start, turn.End, turn.Probability));
        }

        TimeSpan maxTurnEnd = valid.Count == 0 ? TimeSpan.Zero : valid.Max(t => t.End);
        TimeSpan maxWordEnd = words.Where(w => w is not null && w.End > TimeSpan.Zero).Select(w => w.End).DefaultIfEmpty().Max();
        TimeSpan duration = options.RecordingDuration is { } requested
            ? (requested > maxTurnEnd ? requested : maxTurnEnd)
            : (maxTurnEnd > maxWordEnd ? maxTurnEnd : maxWordEnd);

        List<TimeInterval> allSpeech = valid.Select(t => new TimeInterval(t.Start, t.End)).ToList();
        List<TimeInterval> union = MergeIntervals(allSpeech);
        TimeSpan totalSpeech = Sum(union);
        List<TimeInterval> overlapIntervals = BuildOverlapIntervals(valid, options.MinOverlap);
        TimeSpan totalOverlap = Sum(MergeIntervals(overlapIntervals));

        List<SilenceEvent> silenceEvents = [];
        TimeSpan totalSilence = TimeSpan.Zero;
        TimeSpan longestSilence = TimeSpan.Zero;
        long cursor = 0;

        if (valid.Count > 0)
        {
            foreach (TimeInterval interval in union)
            {
                AddSilence(cursor, interval.Start.Ticks, options.MinSilence, silenceEvents, ref totalSilence, ref longestSilence);
                cursor = interval.End.Ticks;
            }

            AddSilence(cursor, duration.Ticks, options.MinSilence, silenceEvents, ref totalSilence, ref longestSilence);
        }
        else
        {
            totalSilence = duration;
            longestSilence = duration;
        }

        Dictionary<int, int> wordCounts = AssignWords(valid, words);
        List<SpeakerTalkStats> speakers = valid
            .GroupBy(t => t.SpeakerId)
            .Select(group =>
            {
                List<TurnInterval> speakerTurns = group.ToList();
                TimeSpan talkTime = Sum(MergeIntervals(speakerTurns.Select(t => new TimeInterval(t.Start, t.End))));
                TimeSpan rawTalk = TimeSpan.FromTicks(speakerTurns.Sum(t => t.Duration.Ticks));
                int wordCount = wordCounts.GetValueOrDefault(group.Key);
                return new SpeakerTalkStats(
                    group.Key,
                    talkTime,
                    duration > TimeSpan.Zero ? talkTime.Ticks / (double)duration.Ticks : 0.0,
                    speakerTurns.Count,
                    TimeSpan.FromTicks(rawTalk.Ticks / speakerTurns.Count),
                    speakerTurns.Max(t => t.Duration),
                    speakerTurns.Min(t => t.Start),
                    speakerTurns.Max(t => t.End),
                    wordCount,
                    talkTime > TimeSpan.Zero ? wordCount / talkTime.TotalMinutes : 0.0);
            })
            .OrderBy(s => s.SpeakerId)
            .ToList();

        List<OverlapEvent> overlaps = BuildPairwiseOverlaps(valid, options.MinOverlap);
        List<SpeakerChangeEvent> changes = BuildSpeakerChanges(valid);
        List<InterruptionEvent> interruptions = [];
        List<InterruptionEvent> backchannels = [];
        BuildInterruptionEvents(valid, options, interruptions, backchannels);

        SpeakerTalkStats? dominant = speakers
            .OrderByDescending(s => s.TalkTime)
            .ThenBy(s => s.SpeakerId)
            .FirstOrDefault();

        double dominantShare = dominant is null || duration <= TimeSpan.Zero ? 0.0 : dominant.TalkTime.Ticks / (double)duration.Ticks;
        double meanShare = speakers.Count == 0 ? 0.0 : speakers.Average(s => s.TalkShare);
        double variance = speakers.Count == 0 ? 0.0 : speakers.Average(s => Math.Pow(s.TalkShare - meanShare, 2));
        int assignedWords = wordCounts.Values.Sum();

        var summary = new MeetingSummary(
            duration,
            speakers.Count,
            totalSpeech,
            totalSilence,
            longestSilence,
            totalOverlap,
            totalSpeech > TimeSpan.Zero ? totalOverlap.Ticks / (double)totalSpeech.Ticks : 0.0,
            dominant?.SpeakerId,
            dominantShare,
            Math.Sqrt(variance),
            changes.Count,
            ignored,
            assignedWords);

        return new MeetingAnalytics(
            summary,
            speakers,
            overlaps,
            silenceEvents,
            interruptions,
            backchannels,
            changes,
            ignored);
    }

    private static Dictionary<int, int> AssignWords(IReadOnlyList<TurnInterval> turns, IReadOnlyList<WordResult> words)
    {
        var counts = new Dictionary<int, int>();

        foreach (WordResult word in words)
        {
            if (word is null || word.End <= word.Start)
            {
                continue;
            }

            TurnInterval? best = null;
            long bestOverlap = 0;
            foreach (TurnInterval turn in turns)
            {
                long overlap = OverlapTicks(word.Start, word.End, turn.Start, turn.End);
                if (overlap > bestOverlap || (best is not null && overlap == bestOverlap && overlap > 0 && turn.SpeakerId < best.SpeakerId))
                {
                    best = turn;
                    bestOverlap = overlap;
                }
            }

            if (best is not null && bestOverlap > 0)
            {
                counts[best.SpeakerId] = counts.GetValueOrDefault(best.SpeakerId) + 1;
            }
        }

        return counts;
    }

    private static List<OverlapEvent> BuildPairwiseOverlaps(IReadOnlyList<TurnInterval> turns, TimeSpan minOverlap)
    {
        var events = new List<OverlapEvent>();
        for (int i = 0; i < turns.Count; i++)
        {
            for (int j = i + 1; j < turns.Count; j++)
            {
                TurnInterval a = turns[i];
                TurnInterval b = turns[j];
                if (a.SpeakerId == b.SpeakerId)
                {
                    continue;
                }

                TimeSpan start = a.Start > b.Start ? a.Start : b.Start;
                TimeSpan end = a.End < b.End ? a.End : b.End;
                TimeSpan overlap = end - start;
                if (overlap > TimeSpan.Zero && overlap >= minOverlap)
                {
                    int first = Math.Min(a.SpeakerId, b.SpeakerId);
                    int second = Math.Max(a.SpeakerId, b.SpeakerId);
                    events.Add(new OverlapEvent(start, end, first, second));
                }
            }
        }

        return events.OrderBy(e => e.Start).ThenBy(e => e.End).ThenBy(e => e.SpeakerA).ThenBy(e => e.SpeakerB).ToList();
    }

    private static List<TimeInterval> BuildOverlapIntervals(IReadOnlyList<TurnInterval> turns, TimeSpan minOverlap)
        => BuildPairwiseOverlaps(turns, minOverlap).Select(e => new TimeInterval(e.Start, e.End)).ToList();

    private static List<SpeakerChangeEvent> BuildSpeakerChanges(IReadOnlyList<TurnInterval> turns)
    {
        var events = new List<SpeakerChangeEvent>();
        int? previous = null;

        foreach (TurnInterval turn in turns.OrderBy(t => t.Start).ThenBy(t => t.End).ThenBy(t => t.SpeakerId))
        {
            if (previous is { } from && from != turn.SpeakerId)
            {
                events.Add(new SpeakerChangeEvent(turn.Start, from, turn.SpeakerId));
            }

            previous = turn.SpeakerId;
        }

        return events;
    }

    private static void BuildInterruptionEvents(
        IReadOnlyList<TurnInterval> turns,
        MeetingAnalyticsOptions options,
        List<InterruptionEvent> interruptions,
        List<InterruptionEvent> backchannels)
    {
        for (int i = 0; i < turns.Count; i++)
        {
            for (int j = i + 1; j < turns.Count; j++)
            {
                (TurnInterval earlier, TurnInterval later) = OrderByStart(turns[i], turns[j]);
                if (earlier.SpeakerId == later.SpeakerId)
                {
                    continue;
                }

                TimeSpan speechBefore = later.Start - earlier.Start;
                if (speechBefore < options.MinInterruptedSpeech)
                {
                    continue;
                }

                TimeSpan start = later.Start;
                TimeSpan end = earlier.End < later.End ? earlier.End : later.End;
                TimeSpan overlap = end - start;
                if (overlap <= TimeSpan.Zero)
                {
                    continue;
                }

                bool isBackchannel = later.Duration <= options.BackchannelMaxDuration && later.End <= earlier.End;
                InterruptionKind kind = isBackchannel ? InterruptionKind.Backchannel : InterruptionKind.Interruption;
                var item = new InterruptionEvent(start, end, earlier.SpeakerId, later.SpeakerId, kind);
                (isBackchannel ? backchannels : interruptions).Add(item);
            }
        }
    }

    private static (TurnInterval Earlier, TurnInterval Later) OrderByStart(TurnInterval a, TurnInterval b)
    {
        if (a.Start < b.Start || (a.Start == b.Start && a.SpeakerId <= b.SpeakerId))
        {
            return (a, b);
        }

        return (b, a);
    }

    private static List<TimeInterval> MergeIntervals(IEnumerable<TimeInterval> intervals)
    {
        List<TimeInterval> sorted = intervals
            .Where(i => i.End > i.Start)
            .OrderBy(i => i.Start)
            .ThenBy(i => i.End)
            .ToList();

        var merged = new List<TimeInterval>();
        foreach (TimeInterval interval in sorted)
        {
            if (merged.Count == 0 || interval.Start > merged[^1].End)
            {
                merged.Add(interval);
                continue;
            }

            TimeInterval previous = merged[^1];
            if (interval.End > previous.End)
            {
                merged[^1] = previous with { End = interval.End };
            }
        }

        return merged;
    }

    private static TimeSpan Sum(IEnumerable<TimeInterval> intervals)
        => TimeSpan.FromTicks(intervals.Sum(i => i.Duration.Ticks));

    private static long OverlapTicks(TimeSpan aStart, TimeSpan aEnd, TimeSpan bStart, TimeSpan bEnd)
    {
        long start = Math.Max(aStart.Ticks, bStart.Ticks);
        long end = Math.Min(aEnd.Ticks, bEnd.Ticks);
        return Math.Max(0, end - start);
    }

    private static void AddSilence(
        long start,
        long end,
        TimeSpan minSilence,
        List<SilenceEvent> events,
        ref TimeSpan total,
        ref TimeSpan longest)
    {
        if (end <= start)
        {
            return;
        }

        TimeSpan silence = TimeSpan.FromTicks(end - start);
        total += silence;
        if (silence > longest)
        {
            longest = silence;
        }

        if (silence >= minSilence)
        {
            events.Add(new SilenceEvent(TimeSpan.FromTicks(start), TimeSpan.FromTicks(end)));
        }
    }

    private sealed record TurnInterval(int SpeakerId, TimeSpan Start, TimeSpan End, float Probability)
    {
        public TimeSpan Duration => End - Start;
    }

    private sealed record TimeInterval(TimeSpan Start, TimeSpan End)
    {
        public TimeSpan Duration => End - Start;
    }
}
