namespace TranscribeCppSharp.Voiceprint.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TranscribeCppSharp.Audio;
using TranscribeCppSharp.Voiceprint;
using Xunit;

/// <summary>Blind safety tests for anonymous speaker persistence; public API only.</summary>
public sealed class UnknownSpeakerSafetyTests
{
    private static readonly AmbiguityPolicy Policy = new(MinScore: 0.75, MinMargin: 0.10, MaxRechecks: 0);

    [Fact]
    public async Task PendingClusterMatchIsNotRealPersonIdentity()
    {
        var store = new SingleUnknownSpeakerStore();
        var tracker = new UnknownSpeakerTracker(store, "model-v1", Policy);

        UnknownSpeaker first = await tracker.RecordUnknownAsync("t1", Snippet(1), [1f, 0f]);
        UnknownSpeaker second = await tracker.RecordUnknownAsync("t2", Snippet(1), [1f, 0f]);

        UnknownSpeakerMatch match = UnknownSpeakerMatcher.MatchPending([1f, 0f], "model-v1", [second], Policy);

        Assert.True(match.Matched); // anonymous cluster retained
        Assert.Equal(first.UnknownId, match.UnknownId);
        Assert.Equal(UnknownSpeakerStatus.Pending, second.Status);
        Assert.Null(second.PersonId);
        Assert.NotEqual("person-1", match.UnknownId);
        Assert.Equal("t1", second.Sources[0].TranscriptId);
        Assert.Equal("t2", second.Sources[1].TranscriptId);
        Assert.Equal(1, second.Sources[0].RunSpeakerId); // per-run id; UnknownId separate
    }

    [Fact]
    public async Task IdentifyLaterDoesNotInventPersonCorrectOrChangeRawTranscript()
    {
        var store = new SingleUnknownSpeakerStore();
        var tracker = new UnknownSpeakerTracker(store, "model-v1", Policy);

        UnknownSpeaker pending = await tracker.RecordUnknownAsync("t1", Snippet(1), [1f, 0f]);
        UnknownSpeakerIdentified identified = await tracker.IdentifyLaterAsync(pending.UnknownId, "person-1", "Alice");

        var original = new[] { new MergedSegment(TimeSpan.Zero, TimeSpan.FromSeconds(1), "hello", SpeakerId: 1) };
        string originalText = original[0].Text;
        int originalSpeakerId = original[0].SpeakerId;

        Dictionary<int, string> names = RetroactiveSpeakerRelabeler.NamesForTranscript("t1", original, [identified.UnknownSpeaker]);

        Assert.Equal(UnknownSpeakerStatus.Identified, identified.UnknownSpeaker.Status);
        Assert.Equal("person-1", identified.UnknownSpeaker.PersonId); // user assertion, not `PersonCorrect`
        Assert.Equal("Alice", identified.UnknownSpeaker.DisplayName);
        Assert.Equal("person-1", identified.Voiceprint.PersonId);
        Assert.Equal("Alice", names[1]);
        Assert.Equal(originalSpeakerId, original[0].SpeakerId); // no PersonId remap
        Assert.Equal(originalText, original[0].Text); // raw transcript unchanged
        Assert.DoesNotContain("PersonCorrect", typeof(UnknownSpeaker).GetProperties().Select(p => p.Name));
    }

    [Fact]
    public void OnlyPendingSameModelSameDimensionCanMatchUnknownCluster()
    {
        UnknownSpeaker pending = Unknown("unknown-1", UnknownSpeakerStatus.Pending, "model-v1", 2, [1f, 0f]);
        UnknownSpeaker identified = pending with { Status = UnknownSpeakerStatus.Identified, PersonId = "person-1" };
        UnknownSpeaker rejected = pending with { UnknownId = "unknown-2", Status = UnknownSpeakerStatus.Rejected };
        UnknownSpeaker otherModel = pending with { UnknownId = "unknown-3", EmbeddingModelId = "model-v2" };
        UnknownSpeaker otherDimension = pending with { UnknownId = "unknown-4", Dimension = 3, Embeddings = [[1f, 0f, 0f]] };

        Assert.True(UnknownSpeakerMatcher.MatchPending([1f, 0f], "model-v1", [pending], Policy).Matched);
        Assert.False(UnknownSpeakerMatcher.MatchPending([1f, 0f], "model-v1", [identified], Policy).Matched);
        Assert.False(UnknownSpeakerMatcher.MatchPending([1f, 0f], "model-v1", [rejected], Policy).Matched);
        Assert.False(UnknownSpeakerMatcher.MatchPending([1f, 0f], "model-v1", [otherModel], Policy).Matched);
        Assert.False(UnknownSpeakerMatcher.MatchPending([1f, 0f], "model-v2", [pending], Policy).Matched);
        Assert.False(UnknownSpeakerMatcher.MatchPending([1f, 0f], "model-v1", [otherDimension], Policy).Matched);
        Assert.False(UnknownSpeakerMatcher.MatchPending([1f, 0f], "model-v1", [], Policy).Matched);
    }

    [Fact]
    public void CloseUnknownDoesNotBecomeMatchedWithoutMargin()
    {
        UnknownSpeaker p1 = Unknown("unknown-1", UnknownSpeakerStatus.Pending, "model-v1", 2, [1f, 0f]);
        UnknownSpeaker p2 = Unknown("unknown-2", UnknownSpeakerStatus.Pending, "model-v1", 2, [1f, 0f]);

        UnknownSpeakerMatch match = UnknownSpeakerMatcher.MatchPending([1f, 0f], "model-v1", [p1, p2], Policy);

        Assert.False(match.Matched); // no false merge on ambiguous close voice
        Assert.Equal(0.0, match.Margin);
    }

    [Fact]
    public void RetroactiveNamingRequiresIdentifiedStatusTranscriptAndNonZeroSpeaker()
    {
        UnknownSpeaker pending = Unknown("unknown-1", UnknownSpeakerStatus.Pending, "model-v1", 2, [1f, 0f]);
        UnknownSpeaker identified = pending with { Status = UnknownSpeakerStatus.Identified, PersonId = "person-1", DisplayName = "Alice" };
        UnknownSpeaker rejected = pending with { UnknownId = "unknown-2", Status = UnknownSpeakerStatus.Rejected, DisplayName = "Rejected" };
        UnknownSpeaker otherTranscript = identified with { UnknownId = "unknown-3", Sources = [Source("other", 1)] };
        UnknownSpeaker speakerZero = identified with { UnknownId = "unknown-4", Sources = [Source("t1", 0)] };

        var segments = new[] { new MergedSegment(TimeSpan.Zero, TimeSpan.FromSeconds(1), "hello", SpeakerId: 1) };

        Assert.Empty(RetroactiveSpeakerRelabeler.NamesForTranscript("t1", segments, [pending]));
        Assert.Empty(RetroactiveSpeakerRelabeler.NamesForTranscript("t1", segments, [rejected]));
        Assert.Empty(RetroactiveSpeakerRelabeler.NamesForTranscript("t1", segments, [otherTranscript]));
        Assert.Empty(RetroactiveSpeakerRelabeler.NamesForTranscript("t1", segments, [speakerZero]));
        Assert.Equal("Alice", RetroactiveSpeakerRelabeler.NamesForTranscript("t1", segments, [identified])[1]);
    }

    [Fact]
    public async Task NonFiniteEmbeddingsAreRejectedAndNotPersisted()
    {
        var store = new SingleUnknownSpeakerStore();
        var tracker = new UnknownSpeakerTracker(store, "model-v1", Policy);

        await Assert.ThrowsAsync<ArgumentException>(async () => await tracker.RecordUnknownAsync("t1", Snippet(1), [float.NaN, 0f]));
        await Assert.ThrowsAsync<ArgumentException>(async () => await tracker.RecordUnknownAsync("t1", Snippet(1), [float.PositiveInfinity, 0f]));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await tracker.RecordUnknownAsync("t1", Snippet(1), []));
        Assert.Empty(await store.LoadAsync());
    }

    [Fact]
    public void NonFiniteEmbeddingCannotMatchPendingCluster()
    {
        UnknownSpeaker pending = Unknown("unknown-1", UnknownSpeakerStatus.Pending, "model-v1", 2, [1f, 0f]);

        Assert.Throws<ArgumentException>(() => UnknownSpeakerMatcher.MatchPending([float.NaN, 0f], "model-v1", [pending], Policy));
        Assert.Throws<ArgumentException>(() => UnknownSpeakerMatcher.MatchPending([1f, float.PositiveInfinity], "model-v1", [pending], Policy));
        Assert.Throws<ArgumentException>(() => UnknownSpeakerMatcher.MatchPending([float.NaN, 0f], "model-v1", [], Policy)); // no early Unknown
        Assert.Throws<ArgumentException>(() => UnknownSpeakerMatcher.MatchPending([1f, float.PositiveInfinity], "model-v1", [], Policy));
    }

    [Fact]
    public async Task RejectRemoveAndIdentifyLifecycleCannotDegenerateSilently()
    {
        var store = new SingleUnknownSpeakerStore();
        var tracker = new UnknownSpeakerTracker(store, "model-v1", Policy);

        UnknownSpeaker pending = await tracker.RecordUnknownAsync("t1", Snippet(1), [1f, 0f]);
        UnknownSpeaker rejected = await tracker.RejectAsync(pending.UnknownId);

        Assert.Equal(UnknownSpeakerStatus.Rejected, rejected.Status);
        Assert.False(UnknownSpeakerMatcher.MatchPending([1f, 0f], "model-v1", [rejected], Policy).Matched);

        UnknownSpeakerIdentified identified = await tracker.IdentifyLaterAsync(pending.UnknownId, "person-1", "Alice");
        Assert.Equal(UnknownSpeakerStatus.Identified, identified.UnknownSpeaker.Status);
        Assert.False(UnknownSpeakerMatcher.MatchPending([1f, 0f], "model-v1", [identified.UnknownSpeaker], Policy).Matched);
    }

    private static UnknownSpeaker Unknown(string id, UnknownSpeakerStatus status, string modelId, int dimension, float[] embedding) => new(
        id,
        modelId,
        dimension,
        [embedding],
        [Source("t1", 1)],
        DateTimeOffset.UnixEpoch,
        DateTimeOffset.UnixEpoch,
        status,
        PersonId: null,
        DisplayName: id,
        SourceSampleCount: 1);

    private static UnknownSpeakerSource Source(string transcriptId, int speakerId) => new(
        transcriptId,
        speakerId,
        TimeSpan.Zero,
        TimeSpan.FromSeconds(1),
        float.NaN,
        "sha",
        string.Empty,
        string.Empty);

    private static SpeakerSnippet Snippet(int speakerId) => new(
        speakerId,
        TimeSpan.Zero,
        TimeSpan.FromSeconds(1),
        [1f, 0f],
        DiarizationProbability: float.NaN);

    private sealed class SingleUnknownSpeakerStore : IUnknownSpeakerStore
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
