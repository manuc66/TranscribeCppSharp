using System.Collections.Generic;
using System.Linq;
using TranscribeCppSharp.Interop;
using TranscribeCppSharp.Ui.Services;
using Xunit;

namespace TranscribeCppSharp.Ui.Tests;

public class ModelOptionVisibilityTests
{
    private static ModelCapabilitySnapshot Snapshot(
        bool translate = false,
        bool specDecode = false,
        string architecture = "test",
        bool streaming = false,
        params Feature[] features)
        => new(
            Architecture: architecture,
            Variant: "test",
            SupportsTranslate: translate,
            SupportsLanguageDetect: false,
            SupportsStreaming: streaming,
            SupportsSpecDecode: specDecode,
            Features: features.ToHashSet());

    [Fact]
    public void AnUnloadedModelHidesNothing()
    {
        // Null is "not checked": hiding on an unloaded model would be a guess,
        // and a wrong guess hides an option that would have worked.
        Assert.True(ModelOptionVisibility.ShowWhisperExtensions(null));
        Assert.True(ModelOptionVisibility.ShowTranslate(null));
        Assert.True(ModelOptionVisibility.ShowSpeculativeDecoding(null));
    }

    [Fact]
    public void WhisperExtensionsShowOnEitherFeature()
    {
        Assert.False(ModelOptionVisibility.ShowWhisperExtensions(Snapshot()));

        Assert.True(ModelOptionVisibility.ShowWhisperExtensions(
            Snapshot(features: new[] { Feature.FeatureInitialPrompt })));
        Assert.True(ModelOptionVisibility.ShowWhisperExtensions(
            Snapshot(features: new[] { Feature.FeatureTemperatureFallback })));
    }

    [Fact]
    public void TranslateAndSpecDecodeFollowTheirOwnFlags()
    {
        ModelCapabilitySnapshot no = Snapshot();
        Assert.False(ModelOptionVisibility.ShowTranslate(no));
        Assert.False(ModelOptionVisibility.ShowSpeculativeDecoding(no));

        ModelCapabilitySnapshot yes = Snapshot(translate: true, specDecode: true);
        Assert.True(ModelOptionVisibility.ShowTranslate(yes));
        Assert.True(ModelOptionVisibility.ShowSpeculativeDecoding(yes));
    }

    [Fact]
    public void SupportsReadsTheFeatureSet()
    {
        ModelCapabilitySnapshot snapshot = Snapshot(features: new[] { Feature.FeatureDiarization });

        Assert.True(snapshot.Supports(Feature.FeatureDiarization));
        Assert.False(snapshot.Supports(Feature.FeatureInitialPrompt));
    }

    [Fact]
    public void UnknownStreamingCapabilitiesHideNothing()
    {
        Assert.True(ModelOptionVisibility.ShowStreamingExtensions(null));
        Assert.True(ModelOptionVisibility.ShowMoonshineExtensions(null));
        Assert.True(ModelOptionVisibility.ShowParakeetExtensions(null));
        Assert.True(ModelOptionVisibility.ShowSortformerExtensions(null));
        Assert.True(ModelOptionVisibility.ShowVoxtralExtensions(null));
    }

    [Fact]
    public void StreamingExtensionsFollowArchitectureAndStreaming()
    {
        ModelCapabilitySnapshot moonshine = Snapshot(architecture: "moonshine_streaming", streaming: true);
        Assert.True(ModelOptionVisibility.ShowStreamingExtensions(moonshine));
        Assert.True(ModelOptionVisibility.ShowMoonshineExtensions(moonshine));
        Assert.False(ModelOptionVisibility.ShowParakeetExtensions(moonshine));

        // Offline and streaming Parakeet share an architecture; only the
        // streaming one has the buffered-streaming extension.
        ModelCapabilitySnapshot offlineParakeet = Snapshot(architecture: "parakeet");
        Assert.False(ModelOptionVisibility.ShowStreamingExtensions(offlineParakeet));
        Assert.False(ModelOptionVisibility.ShowParakeetExtensions(offlineParakeet));

        ModelCapabilitySnapshot streamingParakeet = Snapshot(architecture: "parakeet", streaming: true);
        Assert.True(ModelOptionVisibility.ShowParakeetExtensions(streamingParakeet));
        Assert.False(ModelOptionVisibility.ShowMoonshineExtensions(streamingParakeet));

        ModelCapabilitySnapshot sortformer = Snapshot(architecture: "sortformer", streaming: true);
        Assert.True(ModelOptionVisibility.ShowSortformerExtensions(sortformer));

        // Non-realtime Voxtral has a different architecture and does not stream.
        ModelCapabilitySnapshot voxtralRealtime = Snapshot(architecture: "voxtral_realtime", streaming: true);
        Assert.True(ModelOptionVisibility.ShowVoxtralExtensions(voxtralRealtime));
        Assert.False(ModelOptionVisibility.ShowVoxtralExtensions(Snapshot(architecture: "voxtral")));
    }
}
