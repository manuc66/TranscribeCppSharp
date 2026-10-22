// Turns VAD output into transcription windows: the speech intervals, padded,
// merged and split to the model's window size.
//
// Why this exists: the ASR path normally cuts the file every N seconds, silence
// included. With a VAD pass first, only speech regions are handed to the model.
// That is a behaviour change, not a free optimisation — the model sees isolated
// regions with no context across a pause, so the transcript can differ. Nothing
// here measures either speed or accuracy; the CLI flag that uses this is opt-in
// and its documentation says "unmeasured".
//
// Shared source: compiled into the CLI (and available to the GUI and the server
// the same way), like VadWindowing.

using TranscribeCppSharp.Audio;

namespace TranscribeCppSharp.SharedSource;

/// <summary>A stretch of audio the VAD model classified as speech.</summary>
/// <param name="StartSample">Inclusive start, at <see cref="WindowPlanner.SampleRate"/>.</param>
/// <param name="EndSample">Exclusive end, at <see cref="WindowPlanner.SampleRate"/>.</param>
public readonly record struct SpeechInterval(long StartSample, long EndSample);

/// <summary>Plans transcription windows from speech intervals.</summary>
public static class SpeechWindows
{
    /// <summary>
    /// Pads each interval, merges intervals separated by less than
    /// <paramref name="mergeGapMs"/>, drops what is shorter than
    /// <paramref name="minSpeechMs"/>, then splits the result into windows of at
    /// most <paramref name="maxWindowMs"/> with the ASR overlap, so a word at a
    /// split is not cut in half.
    /// </summary>
    /// <param name="speech">Intervals from the VAD model, in any order.</param>
    /// <param name="totalSamples">Length of the file; intervals are clamped to it.</param>
    /// <param name="maxWindowMs">Window size the ASR model accepts.</param>
    /// <param name="paddingMs">Silence kept on each side of a speech interval.</param>
    /// <param name="mergeGapMs">Intervals closer than this are treated as one.</param>
    /// <param name="minSpeechMs">Shorter intervals are dropped as noise.</param>
    /// <returns>Windows in file order; empty when there is no usable speech.</returns>
    public static IReadOnlyList<AudioWindow> Plan(
        IReadOnlyList<SpeechInterval> speech,
        long totalSamples,
        int maxWindowMs,
        int paddingMs = 300,
        int mergeGapMs = 1000,
        int minSpeechMs = 250)
    {
        ArgumentNullException.ThrowIfNull(speech);

        int maxWindowSamples = maxWindowMs * WindowPlanner.SampleRate / 1000;
        int overlap = WindowPlanner.OverlapSamples;
        if (maxWindowSamples <= overlap)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxWindowMs),
                maxWindowMs,
                $"the window must be longer than the {WindowPlanner.OverlapMs} ms overlap");
        }

        long padding = Math.Max(0, paddingMs) * (long)WindowPlanner.SampleRate / 1000;
        long mergeGap = Math.Max(0, mergeGapMs) * (long)WindowPlanner.SampleRate / 1000;
        long minimum = Math.Max(0, minSpeechMs) * (long)WindowPlanner.SampleRate / 1000;

        var padded = new List<(long Start, long End)>(speech.Count);
        foreach (SpeechInterval interval in speech)
        {
            long rawStart = Math.Min(interval.StartSample, interval.EndSample);
            long rawEnd = Math.Max(interval.StartSample, interval.EndSample);

            // The minimum applies to what the VAD model reported, before padding:
            // padding is context we add, not speech the model found.
            if (rawEnd - rawStart < minimum)
            {
                continue;
            }

            long start = Math.Max(0, rawStart - padding);
            long end = Math.Min(totalSamples, rawEnd + padding);
            if (end - start <= 0)
            {
                continue;
            }

            padded.Add((start, end));
        }

        padded.Sort((a, b) => a.Start.CompareTo(b.Start));

        var merged = new List<(long Start, long End)>(padded.Count);
        foreach ((long start, long end) in padded)
        {
            if (merged.Count > 0 && start <= merged[^1].End + mergeGap)
            {
                merged[^1] = (merged[^1].Start, Math.Max(merged[^1].End, end));
            }
            else
            {
                merged.Add((start, end));
            }
        }

        int step = maxWindowSamples - overlap;
        var windows = new List<AudioWindow>();
        foreach ((long start, long end) in merged)
        {
            long cursor = start;
            while (cursor < end)
            {
                long length = Math.Min(maxWindowSamples, end - cursor);
                windows.Add(new AudioWindow((int)cursor, (int)length));
                if (cursor + length >= end)
                {
                    break;
                }

                cursor += step;
            }
        }

        return windows;
    }
}
