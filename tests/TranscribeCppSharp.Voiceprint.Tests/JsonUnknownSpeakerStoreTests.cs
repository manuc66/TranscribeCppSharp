namespace TranscribeCppSharp.Voiceprint.Tests;

using System;
using System.IO;
using System.Threading.Tasks;
using TranscribeCppSharp.Voiceprint;
using Xunit;

public sealed class JsonUnknownSpeakerStoreTests
{
    [Fact]
    public async Task SavesAndLoadsPendingUnknownWithoutAudio()
    {
        string dir = Path.Combine(Path.GetTempPath(), "tcs-unknown-store-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(dir, "unknown.json");
        var store = new JsonUnknownSpeakerStore(path);
        var speaker = new UnknownSpeaker(
            "unknown-1",
            "model-v1",
            Dimension: 2,
            Embeddings: [[1f, 0f]],
            Sources: [],
            FirstSeen: DateTimeOffset.UnixEpoch,
            LastSeen: DateTimeOffset.UnixEpoch,
            UnknownSpeakerStatus.Pending,
            PersonId: null,
            DisplayName: "Unknown unknown-1",
            SourceSampleCount: 1);

        try
        {
            await store.AddOrUpdateAsync(speaker);

            UnknownSpeaker saved = Assert.Single(await store.LoadAsync());
            Assert.Equal(UnknownSpeakerStatus.Pending, saved.Status);
            Assert.Equal(2, saved.Dimension);
            Assert.True(await store.RemoveAsync("unknown-1"));
            Assert.Empty(await store.LoadAsync());
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }
}
