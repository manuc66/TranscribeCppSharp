namespace TranscribeCppSharp.Voiceprint.Tests;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TranscribeCppSharp.Voiceprint;
using Xunit;

public sealed class UnknownSpeakerLimitsTests
{
    private static readonly AmbiguityPolicy Policy = new(0.75, 0.0, 1);

    [Fact]
    public async Task MaxEmbeddingsPerUnknownKeepsLatestBoundedSet()
    {
        var store = new MemoryUnknownSpeakerStore();
        var tracker = new UnknownSpeakerTracker(
            store,
            "model-v1",
            Policy,
            voiceprintStore: null,
            options: new UnknownSpeakerTrackerOptions(MaxEmbeddingsPerUnknown: 2));

        await tracker.RecordUnknownAsync("t1", Snippet(1), [1f, 0f]);
        await tracker.RecordUnknownAsync("t2", Snippet(1), [1f, 0.1f]);
        UnknownSpeaker speaker = await tracker.RecordUnknownAsync("t3", Snippet(1), [1f, 0.2f]);

        Assert.Equal(2, speaker.Embeddings.Count);
        Assert.Equal(2, speaker.SourceSampleCount);
        Assert.Equal(3, speaker.Sources.Count); // source history separate unless capped
        Assert.Equal([1f, 0.2f], speaker.Embeddings[^1]);
    }

    [Fact]
    public async Task MaxSourcesAndMaxSourcesPerTranscriptBoundProvenance()
    {
        var store = new MemoryUnknownSpeakerStore();
        var tracker = new UnknownSpeakerTracker(
            store,
            "model-v1",
            Policy,
            voiceprintStore: null,
            options: new UnknownSpeakerTrackerOptions(MaxSources: 2, MaxSourcesPerTranscript: 1));

        await tracker.RecordUnknownAsync("t1", Snippet(1), [1f, 0f]);
        await tracker.RecordUnknownAsync("t1", Snippet(1), [1f, 0f]);
        UnknownSpeaker speaker = await tracker.RecordUnknownAsync("t2", Snippet(1), [1f, 0f]);

        Assert.Equal(2, speaker.Sources.Count);
        Assert.Single(speaker.Sources, s => s.TranscriptId == "t1");
        Assert.Contains(speaker.Sources, s => s.TranscriptId == "t2");
    }

    [Fact]
    public void MatchPendingWithRechecksIsBoundedAndDoesNotInventIdentity()
    {
        UnknownSpeaker pending = new(
            "unknown-1",
            "model-v1",
            2,
            [[1f, 0f]],
            [],
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            UnknownSpeakerStatus.Pending,
            PersonId: null,
            DisplayName: "Unknown",
            SourceSampleCount: 1);

        UnknownSpeakerMatch match = UnknownSpeakerMatcher.MatchPendingWithRechecks(
            [0.5f, 0.5f],
            "model-v1",
            [pending],
            Policy with { MinScore = 0.99 },
            recheckEmbeddings: [[1f, 0f], [1f, 0f], [1f, 0f]]);

        Assert.True(match.Matched); // anonymous cluster, not `Known`, not `PersonId`
        Assert.Equal(1, match.ReverifiedCount);
        Assert.Null(pending.PersonId);
    }

    private static SpeakerSnippet Snippet(int speakerId) => new(
        speakerId,
        TimeSpan.Zero,
        TimeSpan.FromSeconds(1),
        [1f, 0f],
        DiarizationProbability: float.NaN);

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
