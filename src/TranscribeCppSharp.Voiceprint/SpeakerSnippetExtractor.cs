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
        int maxSnippetMs = 0,
        AudioQualityPolicy? audioQualityPolicy = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentNullException.ThrowIfNull(turns);
        ArgumentOutOfRangeException.ThrowIfNegative(minTurnMs);
        ArgumentOutOfRangeException.ThrowIfNegative(paddingMs);
        ArgumentOutOfRangeException.ThrowIfNegative(maxSnippetMs);
        audioQualityPolicy?.Validate();

        int effectiveMinTurnMs = Math.Max(minTurnMs, audioQualityPolicy?.MinTurnMs ?? 0);
        int effectiveMaxSnippetMs = maxSnippetMs > 0
            ? (audioQualityPolicy?.MaxSnippetMs > 0 ? Math.Min(maxSnippetMs, audioQualityPolicy.MaxSnippetMs) : maxSnippetMs)
            : audioQualityPolicy?.MaxSnippetMs ?? 0;

        var snippets = new List<SpeakerSnippet>();
        long totalSamples = pcm16kMono.Length;

        foreach (SpeakerSegmentResult turn in turns)
        {
            if (turn is null || turn.SpeakerId == 0 || turn.End <= turn.Start)
            {
                continue;
            }

            double durationMs = (turn.End - turn.Start).TotalMilliseconds;
            if (durationMs < effectiveMinTurnMs)
            {
                continue;
            }

            long startMs = (long)Math.Round(turn.Start.TotalMilliseconds) - paddingMs;
            long endMs = (long)Math.Round(turn.End.TotalMilliseconds) + paddingMs;
            long startSample = Math.Clamp(MsToSample(startMs, sampleRate), 0, totalSamples);
            long endSample = Math.Clamp(MsToSample(endMs, sampleRate), startSample, totalSamples);

            int maxSamples = effectiveMaxSnippetMs > 0 ? MsToSample(effectiveMaxSnippetMs, sampleRate) : int.MaxValue;
            int length = (int)Math.Min(endSample - startSample, maxSamples);
            if (length <= 0)
            {
                continue;
            }

            if (audioQualityPolicy is { MinSpeakerProbability: { } minSpeakerProbability }
                && !double.IsNaN(minSpeakerProbability)
                && (!float.IsFinite(turn.Probability) || turn.Probability < minSpeakerProbability))
            {
                continue;
            }

            float[] slice = pcm16kMono.Slice((int)startSample, length).ToArray();
            AudioQuality? quality = audioQualityPolicy is null
                ? null
                : AudioQualityAnalyzer.Analyze(
                    slice,
                    sampleRate,
                    audioQualityPolicy,
                    channels: 1,
                    bitDepth: 16,
                    isPcm: true,
                    minTurnMs: effectiveMinTurnMs,
                    maxSnippetMs: effectiveMaxSnippetMs);

            if (quality is { Passed: false })
            {
                continue;
            }

            snippets.Add(new SpeakerSnippet(
                turn.SpeakerId,
                turn.Start,
                turn.End,
                slice,
                turn.Probability,
                quality));
        }

        return snippets;
    }

    private static int MsToSample(long ms, int sampleRate)
        => checked((int)Math.Clamp((long)Math.Round(ms * (sampleRate / 1000.0)), int.MinValue, int.MaxValue));
}
