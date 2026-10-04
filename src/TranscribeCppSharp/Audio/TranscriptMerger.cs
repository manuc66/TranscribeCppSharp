// Folds the per-window transcripts of a split run back into one transcript.
//
// The native API transcribes one buffer at a time, so audio longer than a
// model's window has to be split (see WindowPlanner). Two things then need
// fixing up per window, and both front ends have to do them the same way or the
// CLI and the GUI would report different transcripts for the same file:
//
//   1. timestamps are relative to the start of the window, so they must be
//      shifted by the window's offset into the file;
//   2. consecutive windows overlap (WindowPlanner.OverlapMs), so the tail of
//      one window repeats audio the next one transcribes again. Keeping both
//      copies would print every sentence at each seam twice.
//
// This lives in the wrapper rather than in either front end for that reason.

using System;
using System.Collections.Generic;
using System.Linq;

namespace TranscribeCppSharp.Audio;

/// <summary>
/// Accumulates the windows of one split run into a single transcript.
/// </summary>
/// <remarks>
/// Not thread-safe: feed it the windows in order from one thread. One instance
/// per run; reusing it across runs would carry the previous run's overlap
/// position into the next one and drop its first segments.
/// </remarks>
public sealed class TranscriptMerger
{
    /// <summary>
    /// How far before the end of the last kept segment a new one must start to
    /// be kept, in milliseconds.
    /// </summary>
    /// <remarks>
    /// Not zero: the two windows of a seam do not cut at exactly the same
    /// place, so the repeated part is not a byte-identical range and a
    /// segment boundary can land either side of the cut. Half a second of slack
    /// is what the CLI has always used here; it is a tolerance, not a
    /// measurement, so it is not tuned against any particular model.
    /// </remarks>
    public const long SeamToleranceMs = 500;

    /// <summary>
    /// "Nothing kept yet", as a last-end timestamp.
    /// </summary>
    /// <remarks>
    /// Half of <see cref="long.MinValue"/>, not <see cref="long.MinValue"/>
    /// itself: the comparison subtracts <see cref="SeamToleranceMs"/> from this
    /// value on every segment, and long.MinValue - 500 wraps round to a large
    /// positive number, which reads as "the previous window already covered
    /// this" and would silently drop every segment of the first window.
    /// </remarks>
    private const long NothingKeptYet = long.MinValue / 2;

    private readonly List<MergedSegment>? segments;

    private readonly bool retain;

    private long lastEndMs = NothingKeptYet;

    /// <summary>
    /// Creates a merger.
    /// </summary>
    /// <param name="retain">
    /// Whether to keep the merged segments for <see cref="Segments"/> and
    /// <see cref="BuildText"/>. Pass false when the caller writes each window's
    /// segments out as it goes: an hour of speech is ~900 segments, and a
    /// front end that streams them to a file has no use for a second copy.
    /// The seam bookkeeping is the same either way.
    /// </param>
    public TranscriptMerger(bool retain = true)
    {
        this.retain = retain;
        if (retain)
        {
            segments = new List<MergedSegment>();
        }
    }

    /// <summary>
    /// Segments kept so far, in order, timed against the whole file. Empty when
    /// the merger was created with <c>retain: false</c>.
    /// </summary>
    public IReadOnlyList<MergedSegment> Segments => segments ?? (IReadOnlyList<MergedSegment>)[];

    /// <summary>
    /// Adds one window's transcript, dropping the segments that the previous
    /// window already covered.
    /// </summary>
    /// <param name="window">The window the transcript was produced from.</param>
    /// <param name="transcript">What the native layer returned for it.</param>
    /// <returns>The segments that were kept, in order.</returns>
    public IReadOnlyList<MergedSegment> Add(AudioWindow window, Transcript transcript)
    {
        List<MergedSegment> kept = new();
        foreach (SegmentResult segment in transcript.Segments)
        {
            long start = (long)segment.Start.TotalMilliseconds + window.StartMs;
            long end = (long)segment.End.TotalMilliseconds + window.StartMs;

            // Inside the seam: the previous window already reported this audio.
            if (start < lastEndMs - SeamToleranceMs)
            {
                continue;
            }

            lastEndMs = end;
            MergedSegment shifted = new(
                TimeSpan.FromMilliseconds(start),
                TimeSpan.FromMilliseconds(end),
                segment.Text,
                segment.SpeakerId);
            kept.Add(shifted);
            segments?.Add(shifted);
        }

        return kept;
    }

    /// <summary>
    /// The merged text: the kept segments joined in order.
    /// </summary>
    /// <remarks>
    /// Built from the segments rather than from the last window's FullText,
    /// which would only cover that window's audio. Native FullText cannot be
    /// concatenated either: each window's ends mid-sentence where the next one
    /// begins, so joining them yields a doubled or truncated sentence.
    /// Empty when the merger was created with <c>retain: false</c>.
    /// </remarks>
    public string BuildText()
    {
        if (segments is not { Count: > 0 })
        {
            return string.Empty;
        }

        return string.Join(" ", segments.Select(s => s.Text.Trim())).Trim();
    }

    /// <summary>
    /// Shifts a word timestamp from window-relative to file-relative.
    /// </summary>
    /// <param name="window">The window the word came from.</param>
    /// <param name="word">The word, as returned for that window.</param>
    /// <returns>The same word, timed against the whole file.</returns>
    public static WordResult Shift(AudioWindow window, WordResult word)
        => new(
            TimeSpan.FromMilliseconds((long)word.Start.TotalMilliseconds + window.StartMs),
            TimeSpan.FromMilliseconds((long)word.End.TotalMilliseconds + window.StartMs),
            word.Text);

    /// <summary>
    /// Shifts a speaker segment timestamp from window-relative to file-relative.
    /// </summary>
    /// <param name="window">The window the row came from.</param>
    /// <param name="speaker">The row, as returned for that window.</param>
    /// <returns>The same row, timed against the whole file.</returns>
    public static SpeakerSegmentResult Shift(AudioWindow window, SpeakerSegmentResult speaker)
        => new(
            TimeSpan.FromMilliseconds((long)speaker.Start.TotalMilliseconds + window.StartMs),
            TimeSpan.FromMilliseconds((long)speaker.End.TotalMilliseconds + window.StartMs),
            speaker.SpeakerId,
            speaker.Probability);
}
