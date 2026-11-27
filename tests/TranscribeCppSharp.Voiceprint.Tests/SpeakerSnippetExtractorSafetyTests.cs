namespace TranscribeCppSharp.Voiceprint.Tests;

using System;
using System.Collections.Generic;
using TranscribeCppSharp;
using TranscribeCppSharp.Voiceprint;
using Xunit;

public sealed class SpeakerSnippetExtractorSafetyTests
{
    [Fact]
    public void UnattributedInvalidShortAndEmptyTurnsAreSkipped()
    {
        float[] pcm = new float[16_000];
        var turns = new[]
        {
            new SpeakerSegmentResult(TimeSpan.Zero, TimeSpan.FromSeconds(1), SpeakerId: 0, Probability: 1f),
            new SpeakerSegmentResult(TimeSpan.FromSeconds(1), TimeSpan.Zero, SpeakerId: 1, Probability: 1f),
            new SpeakerSegmentResult(TimeSpan.Zero, TimeSpan.FromSeconds(1), SpeakerId: 1, Probability: 1f),
            new SpeakerSegmentResult(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2).Add(TimeSpan.FromMilliseconds(1)), SpeakerId: 1, Probability: 1f),
        };

        Assert.Single(SpeakerSnippetExtractor.Extract(pcm, turns, minTurnMs: 0)); // invalid + speaker0 removed
        Assert.Single(SpeakerSnippetExtractor.Extract(pcm, turns, minTurnMs: 500)); // 1 s valid turn
        Assert.Empty(SpeakerSnippetExtractor.Extract(pcm, turns, minTurnMs: 2000));
    }

    [Fact]
    public void PaddingClampsMaxSnippetAndProbabilityIsPreserved()
    {
        float[] pcm = new float[100];
        var turn = new SpeakerSegmentResult(TimeSpan.Zero, TimeSpan.FromMilliseconds(100), SpeakerId: 2, Probability: float.NaN);

        SpeakerSnippet snippet = Assert.Single(SpeakerSnippetExtractor.Extract(pcm, [turn], paddingMs: 50, maxSnippetMs: 50));

        Assert.Equal(100, snippet.Pcm16kMono.Length); // clamped at boundaries; total only 100 samples
        Assert.True(float.IsNaN(snippet.DiarizationProbability));
        Assert.Equal(2, snippet.RunSpeakerId);
    }

    [Fact]
    public void SampleRateAndPcmShapeAreCallerResponsibility()
    {
        float[] pcm = new float[16_000];
        var turn = new SpeakerSegmentResult(TimeSpan.Zero, TimeSpan.FromSeconds(1), SpeakerId: 1, Probability: 1f);

        Assert.Throws<ArgumentOutOfRangeException>(() => SpeakerSnippetExtractor.Extract(pcm, [turn], sampleRate: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => SpeakerSnippetExtractor.Extract(pcm, [turn], minTurnMs: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => SpeakerSnippetExtractor.Extract(pcm, [turn], paddingMs: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => SpeakerSnippetExtractor.Extract(pcm, [turn], maxSnippetMs: -1));

        // No resampling here; caller must provide 16 kHz mono finite PCM for the embedder.
        SpeakerSnippet snippet = Assert.Single(SpeakerSnippetExtractor.Extract(pcm, [turn], sampleRate: 8_000));
        Assert.Equal(8_000, snippet.Pcm16kMono.Length);
    }

    [Fact]
    public void NoNoiseOverlapOrSpeakerAssignmentFilteringInside()
    {
        float[] noisyPcm = new float[16_000];
        for (int i = 0; i < noisyPcm.Length; i++)
        {
            noisyPcm[i] = i % 2 == 0 ? 1f : -1f; // noisy still extracted
        }

        var overlapping = new[]
        {
            new SpeakerSegmentResult(TimeSpan.Zero, TimeSpan.FromSeconds(1), SpeakerId: 1, Probability: 1f),
            new SpeakerSegmentResult(TimeSpan.Zero, TimeSpan.FromSeconds(1), SpeakerId: 2, Probability: 1f),
        };

        List<SpeakerSnippet> snippets = [.. SpeakerSnippetExtractor.Extract(noisyPcm, overlapping)];

        Assert.Equal(2, snippets.Count); // no overlap filtering / no `SpeakerAssignment`
        Assert.All(snippets, s => Assert.Equal(16_000, s.Pcm16kMono.Length));
    }
}
