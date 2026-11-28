namespace TranscribeCppSharp.Voiceprint.Tests;

using System;
using System.Threading.Tasks;
using TranscribeCppSharp.Voiceprint;
using Xunit;

public sealed class VoiceprintSchemaVersionTests
{
    [Fact]
    public void VoiceprintAndUnknownSpeakerCarrySchemaVersionAndOptionalModelSha256()
    {
        var voiceprint = new Voiceprint(
            "p1",
            "Alice",
            "model-v1",
            2,
            [[1f, 0f]],
            DateTimeOffset.UnixEpoch,
            SourceSampleCount: 1,
            SchemaVersion: "1",
            ModelSha256: "abc123");

        var unknown = new UnknownSpeaker(
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
            SourceSampleCount: 1,
            SchemaVersion: "1",
            ModelSha256: "abc123");

        Assert.Equal("1", voiceprint.SchemaVersion);
        Assert.Equal("abc123", voiceprint.ModelSha256);
        Assert.Equal("1", unknown.SchemaVersion);
        Assert.Equal("abc123", unknown.ModelSha256);
        Assert.Equal("abc123", unknown.ToPendingVoiceprint().ModelSha256);
        Assert.Equal("model-v1", voiceprint.EmbeddingModelId); // model id + sha separate
    }

    [Fact]
    public async Task UnknownSpeakerTrackerPropagatesModelSha256()
    {
        var store = new MemoryStore();
        var tracker = new UnknownSpeakerTracker(
            store,
            "model-v1",
            new AmbiguityPolicy(0.75, 0.0),
            modelSha256: "abc123");
        var snippet = new SpeakerSnippet(1, TimeSpan.Zero, TimeSpan.FromSeconds(1), [1f, 0f], float.NaN);

        UnknownSpeaker unknown = await tracker.RecordUnknownAsync("t1", snippet, [1f, 0f]);
        UnknownSpeakerIdentified identified = await tracker.IdentifyLaterAsync(unknown.UnknownId, "p1", "Alice");

        Assert.Equal("abc123", unknown.ModelSha256);
        Assert.Equal("abc123", identified.UnknownSpeaker.ModelSha256);
        Assert.Equal("abc123", identified.Voiceprint.ModelSha256);
        Assert.Equal("1", identified.Voiceprint.SchemaVersion);
    }

    private sealed class MemoryStore : IUnknownSpeakerStore
    {
        private readonly System.Collections.Generic.List<UnknownSpeaker> speakers = [];

        public ValueTask<System.Collections.Generic.IReadOnlyList<UnknownSpeaker>> LoadAsync(System.Threading.CancellationToken cancellationToken = default)
            => ValueTask.FromResult<System.Collections.Generic.IReadOnlyList<UnknownSpeaker>>(speakers.ToArray());

        public ValueTask<UnknownSpeaker> AddOrUpdateAsync(UnknownSpeaker speaker, System.Threading.CancellationToken cancellationToken = default)
        {
            speakers.RemoveAll(s => s.UnknownId == speaker.UnknownId);
            speakers.Add(speaker);
            return ValueTask.FromResult(speaker);
        }

        public ValueTask<bool> RemoveAsync(string unknownId, System.Threading.CancellationToken cancellationToken = default)
            => ValueTask.FromResult(speakers.RemoveAll(s => s.UnknownId == unknownId) > 0);
    }
}
