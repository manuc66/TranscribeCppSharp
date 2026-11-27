namespace TranscribeCppSharp.Voiceprint.Tests;

using System;
using System.IO;
using System.Threading.Tasks;
using TranscribeCppSharp.Voiceprint;
using Xunit;

public sealed class JsonVoiceprintStoreTests
{
    [Fact]
    public async Task SavesAndLoadsLocalVoiceprintsWithoutAudio()
    {
        string dir = Path.Combine(Path.GetTempPath(), "tcs-voiceprint-tests-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(dir, "voiceprints.json");
        var store = new JsonVoiceprintStore(path);
        var voiceprint = new Voiceprint(
            "p1",
            "Alice",
            "model-v1",
            Dimension: 2,
            Embeddings: [[1f, 0f]],
            CreatedAt: DateTimeOffset.UnixEpoch,
            SourceSampleCount: 1);

        await store.AddOrUpdateAsync(voiceprint);
        var loaded = await store.LoadAsync();

        Voiceprint saved = Assert.Single(loaded);
        Assert.Equal("Alice", saved.DisplayName);
        Assert.Equal(2, saved.Dimension);
        Assert.True(await store.RemoveAsync("p1"));
        Assert.Empty(await store.LoadAsync());

        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
