namespace TranscribeCppSharp.Voiceprint.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TranscribeCppSharp.Voiceprint;
using Xunit;

public sealed class AmbiguityRecheckerTests
{
    [Fact]
    public async Task DoesNotRecheckWhenKnown()
    {
        var embedder = new StubSpeakerEmbedder([1f, 0f]);
        var rechecker = new AmbiguityRechecker(embedder);
        var snippet = Snippet([1f, 0f, 1f, 0f]);

        SpeakerMatch match = await rechecker.RecheckAsync(snippet, "model", [Print("p1", [1f, 0f])], Policy(maxRechecks: 1));

        Assert.Equal(SpeakerIdentityDecision.Known, match.Decision);
        Assert.Equal(1, embedder.Calls); // no alternative crops when known
    }

    [Fact]
    public async Task RechecksAmbiguousButIsBounded()
    {
        var embedder = new StubSpeakerEmbedder([1f, 0f]);
        var rechecker = new AmbiguityRechecker(embedder);
        var snippet = Snippet(Enumerable.Range(0, 32).Select(i => (float)i).ToArray());
        var voiceprints = new[] { Print("p1", [1f, 0f]), Print("p2", [1f, 0f]) };

        SpeakerMatch match = await rechecker.RecheckAsync(snippet, "model", voiceprints, Policy(minMargin: 0.5, maxRechecks: 1));

        Assert.True(embedder.Calls >= 2); // initial + at most one recheck
        Assert.Equal(1, match.ReverifiedCount);
    }

    [Fact]
    public void AlternativeCropsRespectMaxRechecks()
    {
        float[] pcm = Enumerable.Range(0, 100).Select(i => (float)i).ToArray();

        List<float[]> crops = AmbiguityRechecker.AlternativeCrops(pcm, maxRechecks: 2).ToList();

        Assert.Equal(2, crops.Count);
        Assert.All(crops, crop => Assert.NotEmpty(crop));
    }

    private static SpeakerSnippet Snippet(float[] pcm) => new(
        RunSpeakerId: 1,
        Start: TimeSpan.Zero,
        End: TimeSpan.FromSeconds(pcm.Length / 16000.0),
        pcm,
        DiarizationProbability: float.NaN);

    private static AmbiguityPolicy Policy(double minScore = 0.75, double minMargin = 0.0, int maxRechecks = 1)
        => new(minScore, minMargin, maxRechecks);

    private static Voiceprint Print(string id, float[] embedding) => new(
        id,
        id,
        "model",
        embedding.Length,
        [embedding],
        DateTimeOffset.UnixEpoch,
        SourceSampleCount: 1);

    private sealed class StubSpeakerEmbedder(float[] embedding) : ISpeakerEmbedder
    {
        public string ModelId => "model";

        public int SampleRate => 16_000;

        public int Dimension => embedding.Length;

        public int Calls { get; private set; }

        public ValueTask<float[]> EmbedAsync(ReadOnlyMemory<float> pcm16kMono, CancellationToken cancellationToken = default)
        {
            Calls++;
            return ValueTask.FromResult(embedding.ToArray());
        }
    }
}
