// Windowing for audio longer than one transcription pass: the audio is cut into
// windows that overlap by OverlapMs, and the transcript lines of the overlap are
// dropped by the caller (see the deduplication in TranscribeCommand).
//
// The rule that matters here: a window must be LONGER than the overlap,
// otherwise the loop cannot advance and the tool would spin forever on the same
// window. That is reachable from the command line (--chunk 1) and from a model
// whose reported maximum audio length is barely above the overlap, so it is
// checked here and reported as an error instead of hanging.

namespace TranscribeCppSharp.Cli;

/// <summary>One window of audio to transcribe, in samples at <see cref="WindowPlanner.SampleRate"/>.</summary>
internal readonly record struct AudioWindow(int OffsetSamples, int LengthSamples)
{
    /// <summary>Start of the window in the original timeline, in milliseconds.</summary>
    internal long StartMs => (long)OffsetSamples * 1000 / WindowPlanner.SampleRate;
}

internal static class WindowPlanner
{
    /// <summary>Sample rate the native library expects.</summary>
    internal const int SampleRate = 16000;

    /// <summary>Overlap between two consecutive windows, in milliseconds.</summary>
    internal const int OverlapMs = 1000;

    internal const int OverlapSamples = OverlapMs * SampleRate / 1000;

    /// <summary>
    /// Cuts <paramref name="totalSamples"/> into overlapping windows of at most
    /// <paramref name="windowMs"/>. Fails (with a message meant for the user)
    /// when the window leaves no room for the overlap, or when the audio is
    /// empty, instead of looping forever or calling the native layer with 0
    /// samples.
    /// </summary>
    internal static bool TryPlan(int totalSamples, int windowMs, out IReadOnlyList<AudioWindow> windows, out string? error)
    {
        windows = [];
        error = null;

        if (totalSamples <= 0)
        {
            error = "the audio is empty (0 samples): nothing to transcribe.";
            return false;
        }

        long windowSamples = (long)windowMs * SampleRate / 1000;
        if (windowSamples <= OverlapSamples)
        {
            error = $"a {windowMs}ms window leaves no room for the {OverlapMs}ms overlap: "
                + $"use a window longer than {OverlapMs}ms (--chunk {(OverlapMs / 1000) + 1} or more).";
            return false;
        }

        // >= 1 by construction: that is what makes the loop terminate.
        long step = windowSamples - OverlapSamples;
        var plan = new List<AudioWindow>();
        for (long offset = 0; offset < totalSamples; offset += step)
        {
            int length = (int)Math.Min(windowSamples, totalSamples - offset);
            plan.Add(new AudioWindow((int)offset, length));
            if (offset + length >= totalSamples)
            {
                break;
            }
        }

        windows = plan;
        return true;
    }
}
