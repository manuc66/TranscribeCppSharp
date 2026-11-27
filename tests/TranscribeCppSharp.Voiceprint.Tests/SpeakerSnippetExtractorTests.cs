namespace TranscribeCppSharp.Voiceprint.Tests;

using System;
using System.Linq;
using TranscribeCppSharp;
using TranscribeCppSharp.Voiceprint;
using Xunit;

public sealed class SpeakerSnippetExtractorTests
{
    [Fact]
    public void Extracts16kSlicesFromDiarizedTurnsAndSkipsUnattributed()
    {
        float[] pcm = Enumerable.Range(0, 16_000).Select(i => (float)i).ToArray();
        var turns = new[]
        {
            new SpeakerSegmentResult(TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(200), SpeakerId: 1, Probability: 0.9f),
            new SpeakerSegmentResult(TimeSpan.Zero, TimeSpan.FromSeconds(1), SpeakerId: 0, Probability: float.NaN),
        };

        var snippets = SpeakerSnippetExtractor.Extract(pcm, turns, minTurnMs: 50);

        SpeakerSnippet snippet = Assert.Single(snippets);
        Assert.Equal(1, snippet.RunSpeakerId);
        Assert.Equal(1600, snippet.Pcm16kMono.Length); // 100 ms at 16 kHz
        Assert.Equal(1600f, snippet.Pcm16kMono[0]);
    }

    [Fact]
    public void PaddingIsClampedAndNaNProbabilityIsPreserved()
    {
        float[] pcm = new float[100];
        var turns = new[] { new SpeakerSegmentResult(TimeSpan.Zero, TimeSpan.FromMilliseconds(100), SpeakerId: 2, Probability: float.NaN) };

        SpeakerSnippet snippet = Assert.Single(SpeakerSnippetExtractor.Extract(pcm, turns, paddingMs: 50));

        Assert.Equal(100, snippet.Pcm16kMono.Length);
        Assert.True(float.IsNaN(snippet.DiarizationProbability));
    }
}
