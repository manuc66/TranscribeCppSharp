using TranscribeCppSharp.Audio;

namespace TranscribeCppSharp.SharedSource;

/// <summary>
/// Assigns each transcript segment the speaker whose turn overlaps it most, at
/// word level when the model reports word timings.
/// </summary>
/// <remarks>
/// This is how a dedicated diarizer (a model that serves only the DIARIZE role,
/// e.g. Sortformer) is combined with a transcriber that cannot attribute speakers
/// itself: the two are separate native runs — transcribe.cpp has no combined
/// entry point — and this is the join between them, on our side.
/// <para>
/// Word timings are preferred because a Whisper segment can span a speaker
/// change: assigning the whole segment by its largest overlap gives the segment to
/// one speaker even when the words say the other spoke more of it. Each word takes
/// the turn that overlaps it most, then the segment takes the speaker of most of
/// its words (ties go to the longer speech, then the lower id). A segment with no
/// words, or whose words overlap no turn, falls back to the segment-level rule.
/// </para>
/// <para>
/// The rule is a heuristic, not a verified alignment: word timings are the
/// decoder's, the turns are the diarizer's, and the two can disagree. Nothing here
/// claims accuracy against a reference. A segment that overlaps no turn keeps the
/// speaker it already had (0 for a run that did not diarize), because guessing one
/// would be inventing an attribution the diarizer never made.
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
    /// <param name="words">
    /// Word timings for the same transcript, or null/empty to use the segment-level
    /// rule. The words are the model's own; no alignment is performed here.
    /// </param>
    /// <returns>The segments, with speakers where a turn overlaps.</returns>
    public static List<MergedSegment> Apply(
        IReadOnlyList<MergedSegment> segments,
        IReadOnlyList<SpeakerSegmentResult> turns,
        IReadOnlyList<WordResult>? words = null)
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

            // Word-level first; the segment-level overlap is the fallback both when
            // the model has no words and when its words fall outside every turn.
            int? byWords = words is { Count: > 0 } ? SpeakerFromWords(segment, turns, words) : null;
            int? speaker = byWords ?? SpeakerFromOverlap(segment, turns);

            assigned.Add(speaker is { } id ? segment with { SpeakerId = id } : segment);
        }

        return assigned;
    }

    /// <summary>
    /// The speaker of most of the segment's words, or null when no word overlaps a
    /// turn. Ties go to the speaker with more word duration, then the lower id.
    /// </summary>
    private static int? SpeakerFromWords(
        MergedSegment segment,
        IReadOnlyList<SpeakerSegmentResult> turns,
        IReadOnlyList<WordResult> words)
    {
        var counts = new Dictionary<int, int>();
        var durations = new Dictionary<int, long>();

        foreach (WordResult word in words)
        {
            // A word belongs to the segment its midpoint falls in: word boundaries
            // can sit a few milliseconds outside the segment the decoder reported,
            // and a midpoint is stable across that noise.
            long midpoint = word.Start.Ticks + ((word.End - word.Start).Ticks / 2);
            if (midpoint < segment.Start.Ticks || midpoint > segment.End.Ticks)
            {
                continue;
            }

            int? speaker = SpeakerFromOverlap(word.Start, word.End, turns);
            if (speaker is not { } id)
            {
                continue;
            }

            counts[id] = counts.GetValueOrDefault(id) + 1;
            durations[id] = durations.GetValueOrDefault(id) + Math.Max(0, (word.End - word.Start).Ticks);
        }

        if (counts.Count == 0)
        {
            return null;
        }

        return counts
            .OrderByDescending(kv => kv.Value)
            .ThenByDescending(kv => durations[kv.Key])
            .ThenBy(kv => kv.Key)
            .First()
            .Key;
    }

    /// <summary>The speaker of the segment's largest turn overlap, or null.</summary>
    private static int? SpeakerFromOverlap(
        MergedSegment segment,
        IReadOnlyList<SpeakerSegmentResult> turns)
        => SpeakerFromOverlap(segment.Start, segment.End, turns);

    /// <summary>The speaker of the largest turn overlap, or null when none.</summary>
    private static int? SpeakerFromOverlap(
        TimeSpan start,
        TimeSpan end,
        IReadOnlyList<SpeakerSegmentResult> turns)
    {
        int? speaker = null;
        long best = 0;
        foreach (SpeakerSegmentResult turn in turns)
        {
            long overlap = OverlapTicks(start, end, turn.Start, turn.End);
            if (overlap > best)
            {
                best = overlap;
                speaker = turn.SpeakerId;
            }
        }

        return speaker;
    }

    /// <summary>Overlap of two half-open intervals, in ticks; 0 when they do not touch.</summary>
    private static long OverlapTicks(TimeSpan aStart, TimeSpan aEnd, TimeSpan bStart, TimeSpan bEnd)
    {
        long start = Math.Max(aStart.Ticks, bStart.Ticks);
        long end = Math.Min(aEnd.Ticks, bEnd.Ticks);
        return Math.Max(0, end - start);
    }
}
