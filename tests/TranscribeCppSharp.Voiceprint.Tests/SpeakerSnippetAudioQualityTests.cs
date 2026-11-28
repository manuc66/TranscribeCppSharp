namespace TranscribeCppSharp.Voiceprint.Tests;

using System;
using System.Collections.Generic;
using TranscribeCppSharp;
using TranscribeCppSharp.Voiceprint;
using Xunit;

public sealed class SpeakerSnippetAudioQualityTests
{
    [Fact]
    public void QualityFilteredSnippetsAreOnlyReturnedWhenPolicyPasses()
    {
        float[] pcm = new float[32_000]; // 2 s at 16 kHz
        for (int i = 0; i < pcm.Length; i++)
        {
            pcm[i] = 0.2f * MathF.Sin(i * 0.1f);
        }

        var turn = new SpeakerSegmentResult(TimeSpan.Zero, TimeSpan.FromSeconds(1), SpeakerId: 1, Probability: 0.9f);

        SpeakerSnippet withQuality = Assert.Single(SpeakerSnippetExtractor.Extract(
            pcm,
            [turn],
            audioQualityPolicy: new AudioQualityPolicy(MinDurationMs: 500, MinRms: 0.01, MaxSilenceRatio: 0.9, MinSpeakerProbability: 0.5)));

        Assert.NotNull(withQuality.Quality);
        Assert.True(withQuality.Quality.Passed);
        Assert.True(withQuality.DiarizationProbability == 0.9f);
    }

    [Fact]
    public void SilentShortOrWeakSpeakerProbabilityTurnsAreFiltered()
    {
        float[] silent = new float[32_000];
        float[] noisy = new float[32_000];
        Array.Fill(noisy, 1f);

        var silentTurn = new SpeakerSegmentResult(TimeSpan.Zero, TimeSpan.FromSeconds(1), SpeakerId: 1, Probability: 0.9f);
        var weakTurn = new SpeakerSegmentResult(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), SpeakerId: 1, Probability: 0.1f);
        var policy = new AudioQualityPolicy(MinRms: 0.01, MaxSilenceRatio: 0.1, MaxClippingRatio: 1.0, MinSpeakerProbability: 0.5);

        List<SpeakerSnippet> snippets = [.. SpeakerSnippetExtractor.Extract(silent, [silentTurn], audioQualityPolicy: policy)];

        Assert.Empty(snippets);
        Assert.Empty(SpeakerSnippetExtractor.Extract(noisy, [weakTurn], audioQualityPolicy: policy with { MaxClippingRatio = 1.0 }));
        Assert.Empty(SpeakerSnippetExtractor.Extract(silent, [silentTurn with { Probability = float.NaN }], audioQualityPolicy: policy));
    }
}
