// Runs voice activity detection over audio that is longer than one buffer, in
// windows, merging the speech intervals across the seams.
//
// Silero VAD is "chunked-unbounded" upstream (its catalog record says so), which
// is what makes this the intended long-form path: it is the one non-ASR role that
// is not a hard cap, so the CLI and the GUI can bound their memory to one window
// instead of holding the whole file. Speaker diarization is "hard-cap" and is not
// windowed here — speaker ids are per run, so splitting a file would renumber the
// speakers at every seam.
//
// Shared source: compiled into both the CLI and the desktop app (see the csproj
// Compile includes), like UpstreamCatalog.

using TranscribeCppSharp.Audio;

namespace TranscribeCppSharp.SharedSource;

/// <summary>VAD over a whole file, window by window, with the intervals merged.</summary>
public static class VadWindowing
{
    /// <summary>The merged speech segments (file samples) and the total frame count.</summary>
    public sealed record VadRunResult(IReadOnlyList<VadSession.VadSegmentResult> Segments, int FrameCount);

    /// <summary>
    /// Scores <paramref name="source"/> in windows of
    /// <paramref name="windowSeconds"/> (1 s overlap, like the ASR path) and
    /// returns the speech intervals in file-sample coordinates, unioned where two
    /// windows saw the same speech at the seam.
    /// </summary>
    public static VadRunResult Run(
        VadSession session,
        PcmSource source,
        int windowSeconds,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(source);

        long total = source.LengthSamples;
        int capped = (int)Math.Min(total, int.MaxValue);

        if (!WindowPlanner.TryPlan(capped, windowSeconds * 1000, out var windows, out _))
        {
            // Empty audio, or shorter than one window: a single run is correct.
            var result = session.Run(source.ReadWindow(0, capped), ct: cancellationToken);
            progress?.Report(1.0);
            return new VadRunResult(result.Segments, result.Probabilities.Count);
        }

        var intervals = new List<VadSession.VadSegmentResult>();
        int frames = 0;
        int index = 0;
        foreach (var window in windows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = session.Run(source.ReadWindow(window.OffsetSamples, window.LengthSamples), ct: cancellationToken);
            frames += result.Probabilities.Count;

            long offset = window.OffsetSamples;
            foreach (var segment in result.Segments)
            {
                intervals.Add(new VadSession.VadSegmentResult(offset + segment.StartSample, offset + segment.EndSample));
            }

            index++;
            progress?.Report((double)index / windows.Count);
        }

        return new VadRunResult(Merge(intervals), frames);
    }

    /// <summary>
    /// Unions overlapping or touching intervals. They arrive in window order, so
    /// they are already sorted by start; a window's overlap with the next means the
    /// same speech is reported twice at the seam.
    /// </summary>
    /// <remarks>Internal, not private, so the merge rules are unit tested directly.</remarks>
    internal static List<VadSession.VadSegmentResult> Merge(List<VadSession.VadSegmentResult> intervals)
    {
        var merged = new List<VadSession.VadSegmentResult>(intervals.Count);
        foreach (var interval in intervals)
        {
            if (merged.Count > 0 && interval.StartSample <= merged[^1].EndSample)
            {
                if (interval.EndSample > merged[^1].EndSample)
                {
                    merged[^1] = new VadSession.VadSegmentResult(merged[^1].StartSample, interval.EndSample);
                }
            }
            else
            {
                merged.Add(interval);
            }
        }

        return merged;
    }
}
