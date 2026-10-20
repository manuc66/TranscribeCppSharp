using TranscribeCppSharp.Interop;
using TranscribeCppSharp.Models;
using TranscribeCppSharp.UI.Services;
using TranscribeCppSharp.UI.ViewModels;
using Xunit;

namespace TranscribeCppSharp.UI.Tests;

/// <summary>
/// The diarizer picker: a downloaded DIARIZE-role model offered to a transcriber
/// that cannot attribute speakers itself.
/// </summary>
[Collection("UiGlobalState")]
public class DiarizerPickerTests
{
    private const string Sortformer = "diar_streaming_sortformer_4spk-v2.1";

    private static ModelCapabilitySnapshot Snapshot(
        TranscribeRole roles,
        bool diarizeFeature = false,
        TimestampKind maxTimestamps = TimestampKind.TimestampsSegment)
        => new(
            Architecture: "test",
            Variant: "test",
            Roles: roles,
            SupportsTranslate: false,
            SupportsLanguageDetect: false,
            SupportsStreaming: false,
            SupportsSpecDecode: false,
            Features: diarizeFeature ? new HashSet<Feature> { Feature.FeatureDiarization } : new HashSet<Feature>(),
            Languages: Array.Empty<string>(),
            TranslateTargetLanguages: Array.Empty<string>(),
            MaxTimestampKind: maxTimestamps);

    private static void Plant(string alias)
    {
        ModelDescriptor descriptor = ModelStore.Catalog.Single(m => m.Alias == alias);
        string path = ModelStore.CachedPath(descriptor);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "placeholder");
    }

    /// <summary>Runs the body with a temporary cache root, then restores it.</summary>
    private static void InCache(Action body)
    {
        string previous = ModelStore.CacheRootOverride ?? string.Empty;
        bool hadOverride = ModelStore.CacheRootOverride is not null;
        ModelStore.CacheRootOverride = Path.Combine(
            Path.GetTempPath(), "tcsharp-diarizer-picker-" + Guid.NewGuid().ToString("N"));
        try
        {
            body();
        }
        finally
        {
            ModelStore.CacheRootOverride = hadOverride ? previous : null;
        }
    }

    [Fact]
    public void ThePickerIsForAModelThatCannotDiarizeItself()
    {
        InCache(() =>
        {
            var viewModel = new TranscriptionViewModel(
                new StubTranscriptionService(), new SettingsViewModel(), new ModelCapabilityCache());

            viewModel.ApplyCapabilities(Snapshot(TranscribeRole.Asr));
            Assert.True(viewModel.ShowDiarizerPicker);

            // The model diarizes through its own feature: no second one needed.
            viewModel.ApplyCapabilities(Snapshot(TranscribeRole.Asr, diarizeFeature: true));
            Assert.False(viewModel.ShowDiarizerPicker);

            // The model reports the DIARIZE role as well.
            viewModel.ApplyCapabilities(Snapshot(TranscribeRole.Asr | TranscribeRole.Diarize));
            Assert.False(viewModel.ShowDiarizerPicker);

            // A model that only diarizes has nothing to pair with.
            viewModel.ApplyCapabilities(Snapshot(TranscribeRole.Diarize));
            Assert.False(viewModel.ShowDiarizerPicker);
        });
    }

    [Fact]
    public void ADownloadedDiarizerIsOfferedAndDefaultsToNone()
    {
        InCache(() =>
        {
            Plant(Sortformer);

            var viewModel = new TranscriptionViewModel(
                new StubTranscriptionService(), new SettingsViewModel(), new ModelCapabilityCache());

            Assert.Contains(viewModel.DiarizerOptions, o => o.Alias == Sortformer);
            Assert.Contains(viewModel.DiarizerOptions, o => o.Alias is null);
            Assert.Null(viewModel.SelectedDiarizer?.Alias);
        });
    }

    [Fact]
    public void WithNothingDownloadedThePickerOffersOnlyNone()
    {
        InCache(() =>
        {
            var viewModel = new TranscriptionViewModel(
                new StubTranscriptionService(), new SettingsViewModel(), new ModelCapabilityCache());

            Assert.Single(viewModel.DiarizerOptions);
            Assert.Null(viewModel.DiarizerOptions[0].Alias);
        });
    }

    [Fact]
    public void ThePickerNeedsSegmentTimingsAndSaysWhyWhenTheyAreMissing()
    {
        // Without timings there is nothing to overlap a speaker turn with, so the
        // join cannot work. Offering the picker anyway would promise an attribution
        // the app cannot produce.
        InCache(() =>
        {
            var viewModel = new TranscriptionViewModel(
                new StubTranscriptionService(), new SettingsViewModel(), new ModelCapabilityCache());

            viewModel.ApplyCapabilities(
                Snapshot(TranscribeRole.Asr, maxTimestamps: TimestampKind.TimestampsNone));
            Assert.False(viewModel.ShowDiarizerPicker);
            Assert.True(viewModel.ShowDiarizerUnavailableHint);
            Assert.Contains("timings", viewModel.DiarizerUnavailableHint, StringComparison.OrdinalIgnoreCase);

            viewModel.ApplyCapabilities(Snapshot(TranscribeRole.Asr));
            Assert.True(viewModel.ShowDiarizerPicker);
            Assert.False(viewModel.ShowDiarizerUnavailableHint);
        });
    }

    [Fact]
    public void ThePickerSaysWhichTwoModelsWillRun()
    {
        InCache(() =>
        {
            Plant(Sortformer);
            var viewModel = new TranscriptionViewModel(
                new StubTranscriptionService(), new SettingsViewModel(), new ModelCapabilityCache());
            // The alias first: changing it starts a probe that clears the snapshot
            // when the model is not on disk, which would undo the one below.
            viewModel.ModelAlias = "whisper-tiny";
            viewModel.ApplyCapabilities(Snapshot(TranscribeRole.Asr));

            // Nothing chained: say what that means rather than leaving the reader to
            // assume the model attributes speakers.
            Assert.Contains("Speaker 0", viewModel.DiarizerPlan);

            viewModel.SelectedDiarizer = viewModel.DiarizerOptions.Single(o => o.Alias == Sortformer);

            Assert.Contains("whisper-tiny", viewModel.DiarizerPlan);
            Assert.Contains(Sortformer, viewModel.DiarizerPlan);
            Assert.Contains("two passes", viewModel.DiarizerPlan, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void ADiarizerThatDeclaresOneLanguageWarnsWhenAnotherIsSelected()
    {
        // Sortformer's catalog record declares English only. Pairing it with a
        // French transcription is possible, and the app must say so instead of
        // letting the reader believe the attribution is language-agnostic.
        InCache(() =>
        {
            Plant(Sortformer);
            var viewModel = new TranscriptionViewModel(
                new StubTranscriptionService(), new SettingsViewModel(), new ModelCapabilityCache());
            viewModel.ApplyCapabilities(Snapshot(TranscribeRole.Asr));
            viewModel.SelectedDiarizer = viewModel.DiarizerOptions.Single(o => o.Alias == Sortformer);

            viewModel.Language = "en";
            Assert.False(viewModel.HasDiarizerLanguageWarning);

            viewModel.Language = "fr";
            Assert.True(viewModel.HasDiarizerLanguageWarning);
            Assert.Contains(Sortformer, viewModel.DiarizerLanguageWarning);
            Assert.Contains("fr", viewModel.DiarizerLanguageWarning);
            Assert.Contains("en", viewModel.DiarizerLanguageWarning);

            // Auto-detection has not chosen a language yet, so there is nothing to
            // contradict.
            viewModel.Language = "auto";
            Assert.False(viewModel.HasDiarizerLanguageWarning);
        });
    }
}
