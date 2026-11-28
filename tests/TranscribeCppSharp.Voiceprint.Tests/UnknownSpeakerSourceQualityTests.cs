namespace TranscribeCppSharp.Voiceprint.Tests;

using System;
using System.Threading.Tasks;
using TranscribeCppSharp.Voiceprint;
using Xunit;

public sealed class UnknownSpeakerSourceQualityTests
{
    [Fact]
    public async Task UnknownSpeakerSourceCopiesAudioQualityWithoutIdentity()
    {
        var store = new UnknownSpeakerLimitsStore();
        var tracker = new UnknownSpeakerTracker(store, "model-v1", new AmbiguityPolicy(0.75, 0.0));
        var quality = new AudioQuality(
            SampleRate: 16_000,
            Duration: TimeSpan.FromSeconds(1),
            SampleCount: 16_000,
            Finite: true,
            Rms: 0.1,
            Peak: 0.2,
            ClippingRatio: 0.0,
            SilenceRatio: 0.0,
            NonSilenceRatio: 1.0,
            SnrDb: double.NaN,
            NoiseKnown: false,
            SpeechKnown: false,
            OverlapKnown: false,
            SpeechDetected: true,
            Overlap: false,
            Passed: true,
            Reason: string.Empty);
        var snippet = new SpeakerSnippet(1, TimeSpan.Zero, TimeSpan.FromSeconds(1), [1f, 0f], float.NaN, quality);

        UnknownSpeaker speaker = await tracker.RecordUnknownAsync("t1", snippet, [1f, 0f]);

        UnknownSpeakerSource source = Assert.Single(speaker.Sources);
        Assert.Same(quality, source.Quality);
        Assert.True(source.Quality!.Passed);
        Assert.True(float.IsNaN(source.DiarizationProbability));
        Assert.Null(speaker.PersonId); // quality != identity
        Assert.Equal(UnknownSpeakerStatus.Pending, speaker.Status);
    }

    private sealed class UnknownSpeakerLimitsStore : IUnknownSpeakerStore
    {
        private readonly System.Collections.Generic.List<UnknownSpeaker> speakers = [];

        public System.Threading.Tasks.ValueTask<System.Collections.Generic.IReadOnlyList<UnknownSpeaker>> LoadAsync(System.Threading.CancellationToken cancellationToken = default)
            => System.Threading.Tasks.ValueTask.FromResult<System.Collections.Generic.IReadOnlyList<UnknownSpeaker>>(speakers.ToArray());

        public System.Threading.Tasks.ValueTask<UnknownSpeaker> AddOrUpdateAsync(UnknownSpeaker speaker, System.Threading.CancellationToken cancellationToken = default)
        {
            speakers.Add(speaker);
            return System.Threading.Tasks.ValueTask.FromResult(speaker);
        }

        public System.Threading.Tasks.ValueTask<bool> RemoveAsync(string unknownId, System.Threading.CancellationToken cancellationToken = default)
            => System.Threading.Tasks.ValueTask.FromResult(false);
    }
}
