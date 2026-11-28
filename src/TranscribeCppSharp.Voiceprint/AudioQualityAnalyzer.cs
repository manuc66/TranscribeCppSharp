namespace TranscribeCppSharp.Voiceprint;

using System;
using System.Collections.Generic;

/// <summary>Basic audio gate for speaker snippets. Not noise reduction, VAD, speech recognition, or identity.</summary>
public static class AudioQualityAnalyzer
{
    /// <summary>Computes finite/RMS/peak/clipping/silence + structural checks.</summary>
    public static AudioQuality Analyze(
        ReadOnlySpan<float> pcm,
        int sampleRate,
        AudioQualityPolicy? policy = null,
        int channels = 1,
        int bitDepth = 16,
        bool isPcm = true,
        bool? overlap = null,
        double minTurnMs = 0.0,
        double maxSnippetMs = 0.0)
    {
        policy ??= new AudioQualityPolicy();
        policy.Validate();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);
        ArgumentOutOfRangeException.ThrowIfNegative(bitDepth);

        int sampleCount = pcm.Length;
        double durationMs = sampleCount / ((double)sampleRate * channels) * 1000.0;

        bool finite = true;
        double sumSquares = 0.0;
        double peak = 0.0;
        int clipping = 0;
        int silent = 0;
        const double silenceThreshold = 0.0001;

        foreach (float value in pcm)
        {
            if (!float.IsFinite(value))
            {
                finite = false;
                continue; // do not let NaN poison RMS
            }

            double abs = Math.Abs(value);
            sumSquares += value * value;
            peak = Math.Max(peak, abs);

            if (abs >= 0.999)
            {
                clipping++;
            }

            if (abs <= silenceThreshold)
            {
                silent++;
            }
        }

        int finiteCount = 0;
        if (finite)
        {
            finiteCount = sampleCount;
        }
        else
        {
            foreach (float value in pcm)
            {
                if (float.IsFinite(value))
                {
                    finiteCount++;
                }
            }
        }

        double rms = finiteCount == 0 ? 0.0 : Math.Sqrt(sumSquares / finiteCount);
        double clippingRatio = sampleCount == 0 ? 1.0 : (double)clipping / sampleCount;
        double silenceRatio = sampleCount == 0 ? 1.0 : (double)silent / sampleCount;
        double nonSilenceRatio = 1.0 - silenceRatio;
        TimeSpan duration = TimeSpan.FromMilliseconds(durationMs);

        var reasons = new List<string>();

        if (sampleCount == 0)
        {
            reasons.Add("empty");
        }

        if (policy.RequireFinite && !finite)
        {
            reasons.Add("non-finite");
        }

        if (durationMs + 1e-9 < policy.MinDurationMs)
        {
            reasons.Add("too-short");
        }

        if (policy.MaxDurationMs > 0 && durationMs - 1e-9 > policy.MaxDurationMs)
        {
            reasons.Add("too-long");
        }

        if (durationMs + 1e-9 < minTurnMs)
        {
            reasons.Add("min-turn");
        }

        if (maxSnippetMs > 0 && durationMs - 1e-9 > maxSnippetMs)
        {
            reasons.Add("max-snippet");
        }

        if (sampleRate < policy.MinSampleRate)
        {
            reasons.Add("sample-rate");
        }

        if (policy.RequireResampled16k && sampleRate != policy.RequiredSampleRate)
        {
            reasons.Add("not-16k");
        }

        if (policy.RequireMono && channels != 1)
        {
            reasons.Add("not-mono");
        }

        if (channels < policy.MinChannels)
        {
            reasons.Add("channels");
        }

        if (policy.Require16BitPcm && (!isPcm || bitDepth != 16))
        {
            reasons.Add("not-16-bit-pcm");
        }

        if (bitDepth < policy.MinBitDepth)
        {
            reasons.Add("bit-depth");
        }

        if (rms + 1e-12 < policy.MinRms)
        {
            reasons.Add("low-rms");
        }

        if (peak - 1e-12 > policy.MaxPeak)
        {
            reasons.Add("peak");
        }

        if (clippingRatio - 1e-12 > policy.MaxClippingRatio)
        {
            reasons.Add("clipping");
        }

        if (silenceRatio - 1e-12 > policy.MaxSilenceRatio)
        {
            reasons.Add("silence");
        }

        // Not measured: noise/SNR. Do not invent SnrDb.
        if (!double.IsNegativeInfinity(policy.MinSnrDb))
        {
            reasons.Add("snr-unavailable");
        }

        if (policy.RequireSpeech && nonSilenceRatio <= 0.0)
        {
            reasons.Add("no-speech");
        }

        if (policy.RejectOverlap && overlap == true)
        {
            reasons.Add("overlap");
        }

        return new AudioQuality(
            sampleRate,
            duration,
            sampleCount,
            finite,
            rms,
            peak,
            clippingRatio,
            silenceRatio,
            nonSilenceRatio,
            SnrDb: double.NaN,
            NoiseKnown: false,
            SpeechKnown: false, // no VAD; NonSilenceRatio is not proof of speech
            OverlapKnown: overlap is not null,
            SpeechDetected: nonSilenceRatio > 0.0,
            Overlap: overlap ?? false,
            Passed: reasons.Count == 0,
            Reason: reasons.Count == 0 ? string.Empty : string.Join(',', reasons));
    }
}
