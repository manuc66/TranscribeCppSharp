namespace TranscribeCppSharp.Voiceprint;

using System;
using System.Collections.Generic;
using TranscribeCppSharp;

/// <summary>Slices diarized turns out of local PCM for speaker embedding. No invented attribution.</summary>
public static class SpeakerSnippetExtractor
{
    /// <summary>
    /// Extracts snippets for turns with <c>SpeakerId != 0</c>. Padding is clamped;
    /// too-short turns are skipped. Original diarization probability is preserved (NaN allowed).
    /// </summary>
    public static IReadOnlyList<SpeakerSnippet> Extract(
        ReadOnlySpan<float> pcm16kMono,
        IReadOnlyList<SpeakerSegmentResult> turns,
        int sampleRate = 16_000,
        int minTurnMs = 0,
        int paddingMs = 0,
        int maxSnippetMs = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentNullException.ThrowIfNull(turns);
        ArgumentOutOfRangeException.ThrowIfNegative(minTurnMs);
        ArgumentOutOfRangeException.ThrowIfNegative(paddingMs);
        ArgumentOutOfRangeException.ThrowIfNegative(maxSnippetMs);

        var snippets = new List<SpeakerSnippet>();
        long totalSamples = pcm16kMono.Length;

        foreach (SpeakerSegmentResult turn in turns)
        {
            if (turn is null || turn.SpeakerId == 0 || turn.End <= turn.Start)
            {
                continue;
            }

            double durationMs = (turn.End - turn.Start).TotalMilliseconds;
            if (durationMs < minTurnMs)
            {
                continue;
            }

            long startMs = (long)Math.Round(turn.Start.TotalMilliseconds) - paddingMs;
            long endMs = (long)Math.Round(turn.End.TotalMilliseconds) + paddingMs;
            long startSample = Math.Clamp(MsToSample(startMs, sampleRate), 0, totalSamples);
            long endSample = Math.Clamp(MsToSample(endMs, sampleRate), startSample, totalSamples);

            int maxSamples = maxSnippetMs > 0 ? MsToSample(maxSnippetMs, sampleRate) : int.MaxValue;
            int length = (int)Math.Min(endSample - startSample, maxSamples);
            if (length <= 0)
            {
                continue;
            }

            float[] slice = pcm16kMono.Slice((int)startSample, length).ToArray();
            snippets.Add(new SpeakerSnippet(
                turn.SpeakerId,
                turn.Start,
                turn.End,
                slice,
                turn.Probability));
        }

        return snippets;
    }

    private static int MsToSample(long ms, int sampleRate)
        => checked((int)Math.Clamp((long)Math.Round(ms * (sampleRate / 1000.0)), int.MinValue, int.MaxValue));
}
