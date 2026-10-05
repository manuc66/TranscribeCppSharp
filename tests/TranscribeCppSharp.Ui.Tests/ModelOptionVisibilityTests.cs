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
        params Feature[] features)
        => new(
            Architecture: "test",
            Variant: "test",
            SupportsTranslate: translate,
            SupportsLanguageDetect: false,
            SupportsStreaming: false,
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
}
