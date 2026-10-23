using TranscribeCppSharp.Audio;

namespace TranscribeCppSharp.UI.Services;

/// <summary>
/// Assigns each transcript segment the speaker whose turn overlaps it most.
/// </summary>
/// <remarks>
/// This is how a dedicated diarizer (a model that serves only the DIARIZE role,
/// e.g. Sortformer) is combined with a transcriber that cannot attribute speakers
/// itself: the two are separate native runs — transcribe.cpp has no combined entry
/// point — and this is the join between them, on our side.
/// <para>
/// The rule is the largest time overlap, not "the turn containing the segment's
/// start": a segment can span a speaker change, and the majority of its audio is
/// the better answer. A segment that overlaps no turn keeps the speaker it already
/// had (0 for a run that did not diarize), because guessing one would be inventing
/// an attribution the diarizer never made.
/// </para>
/// </remarks>
public static class SpeakerAssignment
{
    /// <summary>
    /// Returns <paramref name="segments"/> with each <c>SpeakerId</c> set from
    /// <paramref name="turns"/>. The input is not modified.
    /// </summary>
    /// <param name="segments">Transcript segments, timed against the whole file.</param>
    /// <param name="turns">Speaker turns from the diarizer.</param>
    /// <returns>The segments, with speakers where a turn overlaps.</returns>
    public static List<MergedSegment> Apply(
        IReadOnlyList<MergedSegment> segments,
        IReadOnlyList<SpeakerSegmentResult> turns)
    {
        ArgumentNullException.ThrowIfNull(segments);
        ArgumentNullException.ThrowIfNull(turns);

        var assigned = new List<MergedSegment>(segments.Count);
        foreach (MergedSegment segment in segments)
        {
            if (turns.Count == 0)
            {
                assigned.Add(segment);
                continue;
            }

            int speaker = segment.SpeakerId;
            long best = 0;
            foreach (SpeakerSegmentResult turn in turns)
            {
                long overlap = OverlapTicks(segment.Start, segment.End, turn.Start, turn.End);
                if (overlap > best)
                {
                    best = overlap;
                    speaker = turn.SpeakerId;
                }
            }

            assigned.Add(best > 0 ? segment with { SpeakerId = speaker } : segment);
        }

        return assigned;
    }

    /// <summary>Overlap of two half-open intervals, in ticks; 0 when they do not touch.</summary>
    private static long OverlapTicks(TimeSpan aStart, TimeSpan aEnd, TimeSpan bStart, TimeSpan bEnd)
    {
        long start = Math.Max(aStart.Ticks, bStart.Ticks);
        long end = Math.Min(aEnd.Ticks, bEnd.Ticks);
        return Math.Max(0, end - start);
    }
}
