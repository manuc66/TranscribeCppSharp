using TranscribeCppSharp.Interop;
using TranscribeCppSharp.UI.Services;
using Xunit;

namespace TranscribeCppSharp.UI.Tests;

public class ModelOptionVisibilityTests
{
    private static ModelCapabilitySnapshot Snapshot(
        bool translate = false,
        bool specDecode = false,
        string architecture = "test",
        bool streaming = false,
        bool languageDetect = false,
        TranscribeRole roles = TranscribeRole.Asr,
        params Feature[] features)
        => new(
            Architecture: architecture,
            Variant: "test",
            Roles: roles,
            SupportsTranslate: translate,
            SupportsLanguageDetect: languageDetect,
            SupportsStreaming: streaming,
            SupportsSpecDecode: specDecode,
            Features: features.ToHashSet(),
            Languages: Array.Empty<string>(),
            TranslateTargetLanguages: Array.Empty<string>());

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
    public void VocabularyShowsUnlessTheModelSaysItHasNone()
    {
        // Unknown is not a no: an unloaded model keeps the field visible.
        Assert.True(ModelOptionVisibility.ShowVocabulary(null));
        Assert.False(ModelOptionVisibility.ShowVocabulary(Snapshot()));
        Assert.True(ModelOptionVisibility.ShowVocabulary(
            Snapshot(features: new[] { Feature.FeatureVocabulary })));
    }

    /// <summary>
    /// The diarization picker is for a model that transcribes *and* can diarize. A
    /// model whose whole job is speaker turns runs the DIARIZE role, where there is
    /// nothing to ask.
    /// </summary>
    [Fact]
    public void DiarizationShowsOnlyWhereThereIsSomethingToAsk()
    {
        // Not checked yet: show it, like every other group.
        Assert.True(ModelOptionVisibility.ShowDiarize(null));

        // ASR with the diarize feature (MOSS, granite-speech-4.1-2b-plus).
        Assert.True(ModelOptionVisibility.ShowDiarize(
            Snapshot(features: new[] { Feature.FeatureDiarization })));

        // ASR that reports the DIARIZE role as well.
        Assert.True(ModelOptionVisibility.ShowDiarize(
            Snapshot(roles: TranscribeRole.Asr | TranscribeRole.Diarize)));

        // Plain ASR: nothing to ask.
        Assert.False(ModelOptionVisibility.ShowDiarize(Snapshot()));

        // A model that only diarizes (Sortformer): the role does it, no option.
        Assert.False(ModelOptionVisibility.ShowDiarize(
            Snapshot(roles: TranscribeRole.Diarize)));
    }

    [Fact]
    public void UnknownStreamingCapabilitiesHideNothing()
    {
        Assert.True(ModelOptionVisibility.ShowStreamingExtensions(null));
        Assert.True(ModelOptionVisibility.ShowMoonshineExtensions(null));
        Assert.True(ModelOptionVisibility.ShowParakeetExtensions(null));
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

        // Sortformer is a diarizer in transcribe.cpp 0.4, not a streaming family:
        // its architecture no longer turns on the streaming extension group.
        ModelCapabilitySnapshot sortformer = Snapshot(architecture: "sortformer", streaming: true);
        Assert.False(ModelOptionVisibility.ShowStreamingExtensions(sortformer));

        // Non-realtime Voxtral has a different architecture and does not stream.
        ModelCapabilitySnapshot voxtralRealtime = Snapshot(architecture: "voxtral_realtime", streaming: true);
        Assert.True(ModelOptionVisibility.ShowVoxtralExtensions(voxtralRealtime));
        Assert.False(ModelOptionVisibility.ShowVoxtralExtensions(Snapshot(architecture: "voxtral")));
    }
}
