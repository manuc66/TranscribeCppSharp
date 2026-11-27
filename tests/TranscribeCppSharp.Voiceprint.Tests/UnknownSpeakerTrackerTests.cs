namespace TranscribeCppSharp.Voiceprint.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TranscribeCppSharp.Audio;
using TranscribeCppSharp.Voiceprint;
using Xunit;

public sealed class UnknownSpeakerTrackerTests
{
    [Fact]
    public async Task RetainsUnknownAcrossTranscriptsThenIdentifiesLater()
    {
        var unknownStore = new InMemoryUnknownSpeakerStore();
        string voiceprintPath = Path.Combine(Path.GetTempPath(), "tcs-unknown-voiceprint-" + Guid.NewGuid().ToString("N") + ".json");
        var voiceprintStore = new JsonVoiceprintStore(voiceprintPath);
        var tracker = new UnknownSpeakerTracker(
            unknownStore,
            "model-v1",
            new AmbiguityPolicy(MinScore: 0.75, MinMargin: 0.0),
            voiceprintStore);

        try
        {
            UnknownSpeaker first = await tracker.RecordUnknownAsync(
                "transcript-1",
                Snippet(1, "a"),
                [1f, 0f],
                language: "fr",
                condition: "clean");

            UnknownSpeaker second = await tracker.RecordUnknownAsync(
                "transcript-2",
                Snippet(1, "b"),
                [1f, 0f],
                language: "en",
                condition: "noisy");

            Assert.Equal(first.UnknownId, second.UnknownId);
            Assert.Equal(UnknownSpeakerStatus.Pending, second.Status);
            Assert.Null(second.PersonId);
            Assert.Equal(2, second.Sources.Count);
            Assert.Equal(2, second.Embeddings.Count);
            Assert.Equal(2, second.SourceSampleCount);
            Assert.Equal("transcript-1", second.Sources[0].TranscriptId);
            Assert.Equal("transcript-2", second.Sources[1].TranscriptId);
            Assert.Equal(1, second.Sources[0].RunSpeakerId); // per-run numeric id; UnknownId is separate
            Assert.NotEqual(string.Empty, second.Sources[0].SnippetSha256);

            UnknownSpeakerIdentified identified = await tracker.IdentifyLaterAsync(second.UnknownId, "person-1", "Alice");

            Assert.Equal(UnknownSpeakerStatus.Identified, identified.UnknownSpeaker.Status);
            Assert.Equal("person-1", identified.UnknownSpeaker.PersonId);
            Assert.Equal("Alice", identified.UnknownSpeaker.DisplayName);
            Assert.Equal("person-1", identified.Voiceprint.PersonId);
            Assert.Equal("model-v1", identified.Voiceprint.EmbeddingModelId);
            Assert.Equal(2, identified.Voiceprint.Embeddings.Count);

            Voiceprint known = Assert.Single(await voiceprintStore.LoadAsync());
            Assert.Equal("Alice", known.DisplayName);

            var segments = new[] { new MergedSegment(TimeSpan.Zero, TimeSpan.FromSeconds(1), "hello", SpeakerId: 1) };
            Dictionary<int, string> names = RetroactiveSpeakerRelabeler.NamesForTranscript(
                "transcript-1",
                segments,
                [identified.UnknownSpeaker]);

            Assert.Equal("Alice", names[1]); // retroactive `speakerNames`; original segments not rewritten
            Assert.Equal(1, segments[0].SpeakerId);
            Assert.Equal("hello", segments[0].Text);
        }
        finally
        {
            if (File.Exists(voiceprintPath))
            {
                File.Delete(voiceprintPath);
            }
        }
    }

    [Fact]
    public async Task RejectedUnknownIsNotNamedOrMatchedPending()
    {
        var unknownStore = new InMemoryUnknownSpeakerStore();
        var tracker = new UnknownSpeakerTracker(unknownStore, "model-v1", new AmbiguityPolicy(0.75, 0.0));

        UnknownSpeaker pending = await tracker.RecordUnknownAsync("t", Snippet(1, "a"), [1f, 0f]);
        UnknownSpeaker rejected = await tracker.RejectAsync(pending.UnknownId);

        Assert.Equal(UnknownSpeakerStatus.Rejected, rejected.Status);
        UnknownSpeakerMatch match = UnknownSpeakerMatcher.MatchPending(
            [1f, 0f],
            "model-v1",
            [rejected],
            new AmbiguityPolicy(0.75, 0.0));
        Assert.False(match.Matched);

        Dictionary<int, string> names = RetroactiveSpeakerRelabeler.NamesForTranscript(
            "t",
            [new MergedSegment(TimeSpan.Zero, TimeSpan.FromSeconds(1), "x", SpeakerId: 1)],
            [rejected]);
        Assert.Empty(names);
    }

    private static SpeakerSnippet Snippet(int speakerId, string marker) => new(
        speakerId,
        TimeSpan.Zero,
        TimeSpan.FromSeconds(1),
        [1f, 0f, marker.Length],
        DiarizationProbability: float.NaN);

    private sealed class InMemoryUnknownSpeakerStore : IUnknownSpeakerStore
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
