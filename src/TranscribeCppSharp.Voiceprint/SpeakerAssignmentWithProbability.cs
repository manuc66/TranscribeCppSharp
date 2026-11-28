namespace TranscribeCppSharp.Voiceprint;

using System;
using System.Collections.Generic;
using System.Linq;
using TranscribeCppSharp;
using TranscribeCppSharp.Audio;
using TranscribeCppSharp.SharedSource;

/// <summary>Adds diarization `Probability` provenance beside `SpeakerAssignment`, not inside `MergedSegment`.</summary>
public static class SpeakerAssignmentWithProbability
{
    /// <summary>
    /// Runs `SpeakerAssignment.Apply`, then reports source `SpeakerSegmentResult.Probability`.
    /// `NaN` preserved; missing -> null. No `PersonId`, no `SpeakerNames`, no probability invented, no `nl`.
    /// </summary>
    public static List<AssignedSpeakerSegment> Apply(
        IReadOnlyList<MergedSegment> segments,
        IReadOnlyList<SpeakerSegmentResult> turns,
        IReadOnlyList<WordResult>? words = null)
    {
        ArgumentNullException.ThrowIfNull(segments);
        ArgumentNullException.ThrowIfNull(turns);

        List<MergedSegment> assigned = SpeakerAssignment.Apply(segments, turns, words);
        var result = new List<AssignedSpeakerSegment>(assigned.Count);

        foreach (MergedSegment segment in assigned)
        {
            SpeakerSegmentResult? sourceTurn = turns
                .Where(t => t.SpeakerId == segment.SpeakerId)
                .OrderByDescending(t => OverlapTicks(segment.Start, segment.End, t.Start, t.End))
                .ThenBy(t => t.Start)
                .FirstOrDefault();

            float? probability = sourceTurn?.Probability;
            string? state = probability is null
                ? null
                : float.IsNaN(probability.Value)
                    ? "NaN"
                    : float.IsPositiveInfinity(probability.Value)
                        ? "Infinity"
                        : float.IsNegativeInfinity(probability.Value)
                            ? "-Infinity"
                            : "value";

            result.Add(new AssignedSpeakerSegment(segment, segment.SpeakerId, probability, state));
        }

        return result;
    }

    private static long OverlapTicks(TimeSpan aStart, TimeSpan aEnd, TimeSpan bStart, TimeSpan bEnd)
    {
        long start = Math.Max(aStart.Ticks, bStart.Ticks);
        long end = Math.Min(aEnd.Ticks, bEnd.Ticks);
        return Math.Max(0, end - start);
    }
}
