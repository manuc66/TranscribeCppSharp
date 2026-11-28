namespace TranscribeCppSharp.Voiceprint.Tests;

using System;
using TranscribeCppSharp.Voiceprint;
using Xunit;

public sealed class AudioQualityAnalyzerSafetyTests
{
    [Fact]
    public void CleanFinitePcmPassesBasicQuality()
    {
        float[] pcm = new float[16_000];
        for (int i = 0; i < pcm.Length; i++)
        {
            pcm[i] = 0.2f * MathF.Sin(i * 0.1f);
        }

        AudioQuality quality = AudioQualityAnalyzer.Analyze(pcm, 16_000);

        Assert.True(quality.Finite);
        Assert.True(quality.Passed);
        Assert.Equal(string.Empty, quality.Reason);
        Assert.Equal(16_000, quality.SampleRate);
        Assert.Equal(16_000, quality.SampleCount);
        Assert.Equal(TimeSpan.FromSeconds(1), quality.Duration);
        Assert.True(quality.Rms > 0.0);
        Assert.True(quality.Peak <= 1.0);
        Assert.False(quality.NoiseKnown); // no SNR/noise invented
        Assert.True(double.IsNaN(quality.SnrDb));
        Assert.False(quality.SpeechKnown); // non-silence != VAD speech proof
        Assert.True(quality.SpeechDetected);
    }

    [Fact]
    public void NonFiniteEmptyClippingSilenceAndDurationFail()
    {
        var policy = new AudioQualityPolicy(MinDurationMs: 1000, MaxClippingRatio: 0.0, MaxSilenceRatio: 0.1);

        AudioQuality empty = AudioQualityAnalyzer.Analyze([], 16_000, policy);
        AudioQuality nonFinite = AudioQualityAnalyzer.Analyze([1f, float.NaN, 0f], 16_000, policy);
        AudioQuality silent = AudioQualityAnalyzer.Analyze(new float[16_000], 16_000, policy with { MinDurationMs = 0 });
        AudioQuality tooShort = AudioQualityAnalyzer.Analyze([0.1f, 0.1f], 16_000, policy);

        Assert.False(empty.Passed);
        Assert.Contains("empty", empty.Reason);
        Assert.False(nonFinite.Finite);
        Assert.False(nonFinite.Passed);
        Assert.Contains("non-finite", nonFinite.Reason);
        Assert.False(silent.Passed);
        Assert.Contains("silence", silent.Reason);
        Assert.False(tooShort.Passed);
        Assert.Contains("too-short", tooShort.Reason);
    }

    [Fact]
    public void ClippingPeakRmsSampleRateChannelsAndBitDepthAreChecked()
    {
        float[] clipped = new float[16_000];
        Array.Fill(clipped, 1f);

        AudioQuality quality = AudioQualityAnalyzer.Analyze(
            clipped,
            sampleRate: 8_000,
            new AudioQualityPolicy(MinRms: 0.5, MaxPeak: 0.5, MaxClippingRatio: 0.0, MaxSilenceRatio: 1.0),
            channels: 2,
            bitDepth: 24,
            isPcm: false);

        Assert.False(quality.Passed);
        Assert.Contains("clipping", quality.Reason);
        Assert.Contains("peak", quality.Reason);
        Assert.Contains("not-16k", quality.Reason);
        Assert.Contains("not-mono", quality.Reason);
        Assert.Contains("not-16-bit-pcm", quality.Reason);
    }

    [Fact]
    public void SnrOverlapAndSpeechAreNotInvented()
    {
        float[] pcm = new float[16_000];
        Array.Fill(pcm, 0.1f);

        AudioQuality noPolicy = AudioQualityAnalyzer.Analyze(pcm, 16_000);
        AudioQuality snrPolicy = AudioQualityAnalyzer.Analyze(pcm, 16_000, new AudioQualityPolicy(MinSnrDb: 0.0, MaxSilenceRatio: 1.0));
        AudioQuality overlapPolicy = AudioQualityAnalyzer.Analyze(pcm, 16_000, new AudioQualityPolicy(RejectOverlap: true, MaxSilenceRatio: 1.0), overlap: true);

        Assert.False(noPolicy.OverlapKnown);
        Assert.False(snrPolicy.NoiseKnown);
        Assert.Contains("snr-unavailable", snrPolicy.Reason);
        Assert.True(overlapPolicy.OverlapKnown);
        Assert.True(overlapPolicy.Overlap);
        Assert.Contains("overlap", overlapPolicy.Reason);
    }

    [Fact]
    public void PolicyValidationRejectsInvalidThresholds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AudioQualityPolicy(MinDurationMs: -1).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new AudioQualityPolicy(MaxClippingRatio: -0.1).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new AudioQualityPolicy(MinSnrDb: double.NaN).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new AudioQualityPolicy(MinSpeakerProbability: 1.1).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => AudioQualityAnalyzer.Analyze([], 0));
    }
}
