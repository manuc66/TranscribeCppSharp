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

    private static ModelCapabilitySnapshot Snapshot(TranscribeRole roles, bool diarizeFeature = false)
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
            TranslateTargetLanguages: Array.Empty<string>());

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
}
