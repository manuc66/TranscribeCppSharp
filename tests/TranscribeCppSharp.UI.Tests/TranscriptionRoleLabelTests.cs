using TranscribeCppSharp.Interop;
using TranscribeCppSharp.Models;
using TranscribeCppSharp.UI.Services;
using TranscribeCppSharp.UI.ViewModels;
using Xunit;

namespace TranscribeCppSharp.UI.Tests;

/// <summary>
/// The Transcription tab's run button is named for what the selected model does.
/// </summary>
/// <remarks>
/// The button said "Transcribe" over a run that returns speaker turns, which is part
/// of why a diarization-only model looked unsupported. The label now follows the
/// model's role: transcribe, diarize, identify the language, or detect speech.
/// </remarks>
public class TranscriptionRoleLabelTests
{
    private static ModelCapabilitySnapshot Snapshot(TranscribeRole roles)
        => new(
            Architecture: "test",
            Variant: "test",
            Roles: roles,
            SupportsTranslate: false,
            SupportsLanguageDetect: false,
            SupportsStreaming: false,
            SupportsSpecDecode: false,
            Features: new HashSet<Feature>(),
            Languages: Array.Empty<string>(),
            TranslateTargetLanguages: Array.Empty<string>());

    private static TranscriptionViewModel ViewModel()
    {
        string previous = ModelStore.CacheRootOverride ?? string.Empty;
        bool hadOverride = ModelStore.CacheRootOverride is not null;
        ModelStore.CacheRootOverride = Path.Combine(
            Path.GetTempPath(), "tcsharp-role-label-" + Guid.NewGuid().ToString("N"));
        try
        {
            return new TranscriptionViewModel(
                new StubTranscriptionService(), new SettingsViewModel(), new ModelCapabilityCache());
        }
        finally
        {
            ModelStore.CacheRootOverride = hadOverride ? previous : null;
        }
    }

    [Fact]
    public void TheRunButtonNamesTheModelsRole()
    {
        TranscriptionViewModel viewModel = ViewModel();

        // Nothing checked yet: the ordinary label.
        Assert.Equal("Transcribe", viewModel.RunButtonText);

        viewModel.ApplyCapabilities(Snapshot(TranscribeRole.Asr));
        Assert.Equal("Transcribe", viewModel.RunButtonText);

        viewModel.ApplyCapabilities(Snapshot(TranscribeRole.Diarize));
        Assert.Equal("Diarize", viewModel.RunButtonText);

        viewModel.ApplyCapabilities(Snapshot(TranscribeRole.Langid));
        Assert.Equal("Identify language", viewModel.RunButtonText);

        viewModel.ApplyCapabilities(Snapshot(TranscribeRole.Vad));
        Assert.Equal("Detect speech", viewModel.RunButtonText);
    }
}
