using TranscribeCppSharp.Interop;
using TranscribeCppSharp.Models;
using TranscribeCppSharp.UI.Models;
using TranscribeCppSharp.UI.Services;
using TranscribeCppSharp.UI.ViewModels;
using Xunit;

namespace TranscribeCppSharp.UI.Tests;

/// <summary>
/// The vocabulary field: custom terms the decoder is told to prefer, for the
/// models that declare the feature.
/// </summary>
/// <remarks>
/// The field is shown until a loaded model says it has no vocabulary support;
/// an unknown model is not a no. The effect on a transcript is not measured by
/// this project, so no test asserts one.
/// </remarks>
[Collection("UiGlobalState")]
public class VocabularyOptionTests
{
    private static void WithViewModel(Action<TranscriptionViewModel> test)
    {
        string? previous = ModelStore.CacheRootOverride;
        ModelStore.CacheRootOverride = Path.Combine(
            Path.GetTempPath(), "tcsharp-vocab-" + Guid.NewGuid().ToString("N"));
        using var settings = new UiSettingsScope();
        try
        {
            var viewModel = new TranscriptionViewModel(
                new StubTranscriptionService(), new SettingsViewModel(), new ModelCapabilityCache());
            test(viewModel);
        }
        finally
        {
            ModelStore.CacheRootOverride = previous;
        }
    }

    private static ModelCapabilitySnapshot Snapshot(params Feature[] features)
        => new(
            Architecture: "test",
            Variant: "test",
            Roles: TranscribeRole.Asr,
            SupportsTranslate: false,
            SupportsLanguageDetect: false,
            SupportsStreaming: false,
            SupportsSpecDecode: false,
            Features: features.ToHashSet(),
            Languages: [],
            TranslateTargetLanguages: []);

    [Fact]
    public void TheFieldStartsEmpty()
    {
        WithViewModel(viewModel =>
        {
            Assert.Equal(string.Empty, viewModel.Vocabulary);
            Assert.True(viewModel.ShowVocabulary);
        });
    }

    [Fact]
    public void TheFieldIsHiddenOnlyOnceTheModelSaysItHasNoVocabulary()
    {
        WithViewModel(viewModel =>
        {
            viewModel.ApplyCapabilities(Snapshot());
            Assert.False(viewModel.ShowVocabulary);

            viewModel.ApplyCapabilities(Snapshot(Feature.FeatureVocabulary));
            Assert.True(viewModel.ShowVocabulary);

            // Not checked again: back to visible rather than keeping a "no" that
            // belongs to the previous model.
            viewModel.ApplyCapabilities(null);
            Assert.True(viewModel.ShowVocabulary);
        });
    }

    [Fact]
    public void TheRunSplitsTheVocabularyIntoTrimmedLines()
    {
        WithViewModel(viewModel =>
        {
            viewModel.Vocabulary = "Alpha\n  Bêta  \n\nAlpha\r\n";

            TranscriptionOptions options = viewModel.BuildTranscriptionOptions();

            Assert.NotNull(options.Vocabulary);
            Assert.Equal(["Alpha", "Bêta", "Alpha"], options.Vocabulary);
        });
    }

    [Fact]
    public void AnEmptyVocabularyIsNoVocabulary()
    {
        WithViewModel(viewModel =>
        {
            viewModel.Vocabulary = "   \n\n";

            Assert.Null(viewModel.BuildTranscriptionOptions().Vocabulary);
        });
    }
}
