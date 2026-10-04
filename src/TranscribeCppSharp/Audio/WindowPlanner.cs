using System;
using System.Collections.Generic;

namespace TranscribeCppSharp.Audio;

/// <summary>
/// Cuts audio into overlapping windows for transcription of long audio.
/// </summary>
public static class WindowPlanner
{
    /// <summary>Sample rate the native library expects.</summary>
    public const int SampleRate = 16000;

    /// <summary>Overlap between two consecutive windows, in milliseconds.</summary>
    public const int OverlapMs = 1000;

    /// <summary>Overlap between two consecutive windows, in samples.</summary>
    public const int OverlapSamples = OverlapMs * SampleRate / 1000;

    /// <summary>
    /// Cuts <paramref name="totalSamples"/> into overlapping windows of at most
    /// <paramref name="windowMs"/>. Fails (with a message meant for the user)
    /// when the window leaves no room for the overlap, or when the audio is
    /// empty, instead of looping forever or calling the native layer with 0
    /// samples.
    /// </summary>
    public static bool TryPlan(int totalSamples, int windowMs, out IReadOnlyList<AudioWindow> windows, out string? error)
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
                + $"use a window longer than {OverlapMs}ms.";
            return false;
        }

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
