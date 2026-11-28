namespace TranscribeCppSharp.MeetingAnalytics.Voiceprint.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TranscribeCppSharp;
using TranscribeCppSharp.MeetingAnalytics;
using TranscribeCppSharp.Voiceprint;
using Xunit;

public sealed class MeetingSpeakerResolverTests
{
    [Fact]
    public async Task KnownSpeakersLabelAnalyticsAndExportsWithoutChangingStatistics()
    {
        var voiceprints = new MemoryVoiceprintStore(
        [
            Print("p1", "Alice", [1f, 0f]),
            Print("p2", "Bob", [0f, 1f]),
        ]);
        var embedder = new SignEmbedder();
        var resolver = new MeetingSpeakerResolver(embedder, voiceprints, new MemoryUnknownSpeakerStore(), Options(maxRechecks: 0));
        float[] pcm = Pcm(10, split: true);
        var turns = new[] { Turn(1, 0, 5), Turn(2, 5, 10) };

        MeetingSpeakerResolution result = await resolver.ResolveAsync(pcm, turns, [], "meeting-1");

        Assert.Equal(2, result.Analytics.Speakers.Count);
        Assert.All(result.Analytics.SpeakerLabels, label => Assert.Equal(MeetingSpeakerIdentityStatus.Known, label.Status));
        Assert.Equal("Alice", result.Analytics.SpeakerLabels.Single(l => l.SpeakerId == 1).DisplayName);
        Assert.Equal("Bob", result.Analytics.SpeakerLabels.Single(l => l.SpeakerId == 2).DisplayName);
        Assert.Equal("Alice", result.SpeakerNames[1]);
        Assert.Empty(result.UnknownSpeakers);
        Assert.Equal(TimeSpan.FromSeconds(10), result.Analytics.Summary.TotalSpeechTime);
        Assert.Contains("Alice", MeetingAnalyticsMarkdown.Write(result.Analytics));
        Assert.Contains("Bob", MeetingAnalyticsCsv.Write(result.Analytics));
        Assert.Contains("\"personId\": \"p1\"", MeetingAnalyticsJson.Write(result.Analytics));
    }

    [Fact]
    public async Task UnknownSpeakerIsPersistedAndLabelledButNotNamed()
    {
        var unknownStore = new MemoryUnknownSpeakerStore();
        var resolver = new MeetingSpeakerResolver(new SignEmbedder(), new MemoryVoiceprintStore([]), unknownStore, Options(maxRechecks: 0));
        float[] pcm = Pcm(5, split: false);

        MeetingSpeakerResolution result = await resolver.ResolveAsync(pcm, [Turn(1, 0, 5)], [], "meeting-unknown");

        UnknownSpeaker unknown = Assert.Single(result.UnknownSpeakers);
        MeetingSpeakerLabel label = Assert.Single(result.Analytics.SpeakerLabels);
        Assert.Equal(MeetingSpeakerIdentityStatus.Unknown, label.Status);
        Assert.Equal(unknown.UnknownId, label.UnknownId);
        Assert.Null(label.PersonId);
        Assert.Empty(result.SpeakerNames);
        Assert.Equal("meeting-unknown", unknown.Sources[0].TranscriptId);
        Assert.Equal(1, unknown.Sources[0].RunSpeakerId);
    }

    [Fact]
    public async Task AmbiguousMatchIsNotNamedAndCanBeRetainedAsPendingUnknown()
    {
        var voiceprints = new MemoryVoiceprintStore(
        [
            Print("p1", "Alice", [1f, 0f]),
            Print("p2", "Bob", [1f, 0f]),
        ]);
        var unknownStore = new MemoryUnknownSpeakerStore();
        var resolver = new MeetingSpeakerResolver(new SignEmbedder(), voiceprints, unknownStore, Options(maxRechecks: 0));

        MeetingSpeakerResolution result = await resolver.ResolveAsync(Pcm(5, split: false), [Turn(1, 0, 5)], [], "meeting-ambiguous");

        MeetingSpeakerLabel label = Assert.Single(result.Analytics.SpeakerLabels);
        Assert.Equal(MeetingSpeakerIdentityStatus.Ambiguous, label.Status);
        Assert.Empty(result.SpeakerNames);
        Assert.Single(result.UnknownSpeakers);
    }

    [Fact]
    public async Task NoSnippetMeansNoEmbeddingAndNoInventedUnknownCluster()
    {
        var embedder = new SignEmbedder();
        var unknownStore = new MemoryUnknownSpeakerStore();
        var resolver = new MeetingSpeakerResolver(embedder, new MemoryVoiceprintStore([]), unknownStore, Options(maxRechecks: 0));

        MeetingSpeakerResolution result = await resolver.ResolveAsync(ReadOnlyMemory<float>.Empty, [Turn(1, 0, 5)], [], "meeting-empty");

        Assert.Equal(0, embedder.Calls);
        Assert.Empty(result.UnknownSpeakers);
        MeetingSpeakerLabel label = Assert.Single(result.Analytics.SpeakerLabels);
        Assert.Equal(MeetingSpeakerIdentityStatus.Unknown, label.Status);
        Assert.Null(label.UnknownId);
    }

    [Fact]
    public async Task AudioQualityCanRejectSnippetBeforeIdentity()
    {
        var embedder = new SignEmbedder();
        var unknownStore = new MemoryUnknownSpeakerStore();
        var options = Options(maxRechecks: 0) with { AudioQuality = new AudioQualityPolicy(MinRms: 0.01, MaxSilenceRatio: 0.1) };
        var resolver = new MeetingSpeakerResolver(embedder, new MemoryVoiceprintStore([]), unknownStore, options);

        MeetingSpeakerResolution result = await resolver.ResolveAsync(new float[80_000], [Turn(1, 0, 5)], [], "meeting-silence");

        Assert.Equal(0, embedder.Calls);
        Assert.Empty(result.UnknownSpeakers);
        Assert.Equal(MeetingSpeakerIdentityStatus.Unknown, Assert.Single(result.Analytics.SpeakerLabels).Status);
    }

    [Fact]
    public async Task RechecksAreBoundedByPolicy()
    {
        var embedder = new SignEmbedder();
        var options = Options(maxRechecks: 1) with { MaxSnippetsPerSpeaker = 3 };
        var resolver = new MeetingSpeakerResolver(embedder, new MemoryVoiceprintStore([]), new MemoryUnknownSpeakerStore(), options);
        var turns = new[] { Turn(1, 0, 2), Turn(1, 2, 4), Turn(1, 4, 6) };

        await resolver.ResolveAsync(Pcm(6, split: false), turns, [], "meeting-rechecks");

        Assert.Equal(2, embedder.Calls); // initial + one recheck, not all three
    }

    private static MeetingSpeakerResolutionOptions Options(int maxRechecks) => new(
        IdentityPolicy: new AmbiguityPolicy(0.75, 0.10, maxRechecks),
        PersistUnknownSpeakers: true,
        MaxSnippetsPerSpeaker: 3,
        MinTurnMs: 0,
        PaddingMs: 0,
        MaxSnippetMs: 0);

    private static SpeakerSegmentResult Turn(int speakerId, double start, double end)
        => new(TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end), speakerId, float.NaN);

    private static Voiceprint Print(string id, string name, float[] embedding) => new(
        id,
        name,
        "model-v1",
        embedding.Length,
        [embedding],
        DateTimeOffset.UnixEpoch,
        SourceSampleCount: 1,
        SchemaVersion: "1",
        ModelSha256: null);

    private static float[] Pcm(int seconds, bool split)
    {
        float[] pcm = new float[seconds * 16_000];
        int midpoint = split ? pcm.Length / 2 : 0;
        for (int i = 0; i < pcm.Length; i++)
        {
            pcm[i] = i < midpoint || !split ? 0.2f : -0.2f;
        }

        return pcm;
    }

    private sealed class SignEmbedder : ISpeakerEmbedder
    {
        public string ModelId => "model-v1";

        public int SampleRate => 16_000;

        public int Dimension => 2;

        public int Calls { get; private set; }

        public ValueTask<float[]> EmbedAsync(ReadOnlyMemory<float> pcm16kMono, CancellationToken cancellationToken = default)
        {
            Calls++;
            double sum = 0.0;
            for (int i = 0; i < pcm16kMono.Length; i++)
            {
                sum += pcm16kMono.Span[i];
            }

            float[] embedding = sum >= 0 ? [1f, 0f] : [0f, 1f];
            return ValueTask.FromResult(embedding);
        }
    }

    private sealed class MemoryVoiceprintStore(IReadOnlyList<Voiceprint> initial) : IVoiceprintStore
    {
        private readonly List<Voiceprint> voiceprints = initial.ToList();

        public ValueTask<IReadOnlyList<Voiceprint>> LoadAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyList<Voiceprint>>(voiceprints.ToArray());

        public ValueTask<Voiceprint> AddOrUpdateAsync(Voiceprint voiceprint, CancellationToken cancellationToken = default)
        {
            voiceprints.RemoveAll(v => v.PersonId == voiceprint.PersonId);
            voiceprints.Add(voiceprint);
            return ValueTask.FromResult(voiceprint);
        }

        public ValueTask<bool> RemoveAsync(string personId, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(voiceprints.RemoveAll(v => v.PersonId == personId) > 0);
    }

    private sealed class MemoryUnknownSpeakerStore : IUnknownSpeakerStore
    {
        private readonly List<UnknownSpeaker> speakers = [];

        public ValueTask<IReadOnlyList<UnknownSpeaker>> LoadAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyList<UnknownSpeaker>>(speakers.ToArray());

        public ValueTask<UnknownSpeaker> AddOrUpdateAsync(UnknownSpeaker speaker, CancellationToken cancellationToken = default)
        {
            speakers.RemoveAll(s => s.UnknownId == speaker.UnknownId);
            speakers.Add(speaker);
            return ValueTask.FromResult(speaker);
        }

        public ValueTask<bool> RemoveAsync(string unknownId, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(speakers.RemoveAll(s => s.UnknownId == unknownId) > 0);
    }
}
