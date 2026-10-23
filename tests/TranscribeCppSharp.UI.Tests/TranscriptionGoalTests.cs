using TranscribeCppSharp.Interop;
using TranscribeCppSharp.Models;
using TranscribeCppSharp.UI.Models;
using TranscribeCppSharp.UI.Services;
using TranscribeCppSharp.UI.ViewModels;
using Xunit;

namespace TranscribeCppSharp.UI.Tests;

/// <summary>
/// The goal is the outcome, not the native role: it filters the model picker to
/// the models that can produce it and keeps the speaker step in step with it.
/// </summary>
/// <remarks>
/// The models are placeholders in a temporary cache. What is under test is the
/// catalog role and the wiring, not a native load: the probe may fail on the
/// placeholder, which is the same outcome as a model this machine cannot check.
/// </remarks>
[Collection("UiGlobalState")]
public class TranscriptionGoalTests
{
    private const string Asr = "whisper-tiny";
    private const string Diarizer = "diar_streaming_sortformer_4spk-v2.1";
    private const string LangId = "lang-id-voxlingua107-ecapa";
    private const string Vad = "silero-vad-v6.2";

    private static void Plant(string alias)
    {
        ModelDescriptor descriptor = ModelStore.Catalog.Single(m => m.Alias == alias);
        string path = ModelStore.CachedPath(descriptor);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "placeholder");
    }

    private static void InCache(Action<TranscriptionViewModel> test)
    {
        string? previous = ModelStore.CacheRootOverride;
        ModelStore.CacheRootOverride = Path.Combine(
            Path.GetTempPath(), "tcsharp-goal-" + Guid.NewGuid().ToString("N"));
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

    [Fact]
    public void TheGoalFiltersTheModelsToTheRoleItNeeds()
    {
        InCache(viewModel =>
        {
            Plant(Asr);
            Plant(Diarizer);
            Plant(LangId);
            Plant(Vad);
            viewModel.RefreshModels();

            Assert.Equal([Asr], viewModel.AvailableModels);

            viewModel.Goal = TranscriptionGoal.SpeakersOnly;
            Assert.Equal([Diarizer], viewModel.AvailableModels);
            Assert.Equal(Diarizer, viewModel.ModelAlias);
            Assert.Equal("Diarize", viewModel.RunButtonText);

            viewModel.Goal = TranscriptionGoal.IdentifyLanguage;
            Assert.Equal([LangId], viewModel.AvailableModels);
            Assert.Equal("Identify language", viewModel.RunButtonText);

            viewModel.Goal = TranscriptionGoal.DetectSpeech;
            Assert.Equal([Vad], viewModel.AvailableModels);
            Assert.Equal("Detect speech", viewModel.RunButtonText);

            viewModel.Goal = TranscriptionGoal.Transcribe;
            Assert.Equal([Asr], viewModel.AvailableModels);
            Assert.Equal("Transcribe", viewModel.RunButtonText);
        });
    }

    [Fact]
    public void AGoalWithNoModelSaysWhatIsMissingAndBlocksTheRun()
    {
        InCache(viewModel =>
        {
            Plant(Asr);
            viewModel.RefreshModels();

            viewModel.Goal = TranscriptionGoal.IdentifyLanguage;

            Assert.Empty(viewModel.AvailableModels);
            Assert.False(viewModel.HasDownloadedModels);
            Assert.True(viewModel.HasAnyModelOnDisk);
            Assert.Contains("language identification", viewModel.GoalModelHint, StringComparison.Ordinal);
            Assert.Contains("language identification", viewModel.RunBlockedReason, StringComparison.Ordinal);

            // Nothing on this goal can use the speaker, window, tuning or probe
            // controls; they are not left on screen as dead options.
            Assert.False(viewModel.ShowSpeakerSection);
            Assert.False(viewModel.ShowTranscriptSection);
            Assert.False(viewModel.ShowAdvanced);
            Assert.False(viewModel.CanProbeThisRole);
            Assert.False(viewModel.CanTranscribe);

            // The button still names the action the reader asked for, so the
            // disabled state is not a generic "Transcribe" over a language run.
            Assert.Equal("Identify language", viewModel.RunButtonText);
        });
    }

    [Fact]
    public void TheGoalSetsTheSpeakerIntent()
    {
        InCache(viewModel =>
        {
            Plant(Asr);
            Plant(Diarizer);
            viewModel.RefreshModels();

            viewModel.Goal = TranscriptionGoal.TranscribeWithSpeakers;
            Assert.True(viewModel.LabelSpeakers);
            Assert.Equal(Diarizer, viewModel.SelectedDiarizer?.Alias);
            Assert.True(viewModel.SpeakerLabelsSatisfied);
            Assert.True(viewModel.ShowSpeakerSection);
            Assert.Contains("second pass", viewModel.PipelineSummary, StringComparison.OrdinalIgnoreCase);

            viewModel.Goal = TranscriptionGoal.Transcribe;
            Assert.False(viewModel.LabelSpeakers);
            Assert.False(viewModel.ShowSpeakerSection);
            Assert.Contains("→ text.", viewModel.PipelineSummary, StringComparison.Ordinal);

            // The speaker step does not belong to the other roles at all.
            viewModel.Goal = TranscriptionGoal.SpeakersOnly;
            Assert.False(viewModel.LabelSpeakers);
            Assert.False(viewModel.ShowSpeakerSection);
        });
    }

    [Fact]
    public void ThePipelineSaysOnePassOrTwo()
    {
        InCache(viewModel =>
        {
            Plant(Asr);
            Plant(Diarizer);
            viewModel.RefreshModels();
            viewModel.ApplyCapabilities(Snapshot(TranscribeRole.Asr));

            viewModel.Goal = TranscriptionGoal.TranscribeWithSpeakers;
            Assert.Contains("second pass", viewModel.PipelineSummary, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(Diarizer, viewModel.PipelineSummary, StringComparison.Ordinal);

            // A model that attributes speakers itself does it in the same pass.
            viewModel.SelectedDiarizer = null;
            viewModel.ApplyCapabilities(Snapshot(TranscribeRole.Asr, diarizeFeature: true));
            Assert.Contains("same pass", viewModel.PipelineSummary, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Theory]
    [InlineData(TranscriptionGoal.SpeakersOnly, "diarize")]
    [InlineData(TranscriptionGoal.IdentifyLanguage, "langid")]
    [InlineData(TranscriptionGoal.DetectSpeech, "vad")]
    [InlineData(TranscriptionGoal.Transcribe, "asr")]
    public void TheGetModelsButtonOpensTheCatalogueOnTheGoalsRole(
        TranscriptionGoal goal,
        string roleKey)
    {
        InCache(transcription =>
        {
            var root = new MainWindowViewModel(
                transcription,
                new StreamingViewModel(
                    new StubTranscriptionService(), new SettingsViewModel(), new ModelCapabilityCache()),
                new BatchViewModel(new StubTranscriptionService(), new SettingsViewModel()),
                new ModelManagerViewModel(),
                new SettingsViewModel(),
                new HistoryViewModel());

            transcription.Goal = goal;
            transcription.OpenModelsForGoalCommand.Execute(null);

            Assert.Equal(MainWindowViewModel.ModelTabIndex, root.SelectedTabIndex);
            Assert.Equal(roleKey, root.ModelManager.RoleFilter?.Key);
            Assert.Null(root.ModelManager.FeatureFilter);
        });
    }

    private static ModelCapabilitySnapshot Snapshot(
        TranscribeRole roles,
        bool diarizeFeature = false)
        => new(
            Architecture: "test",
            Variant: "test",
            Roles: roles,
            SupportsTranslate: false,
            SupportsLanguageDetect: false,
            SupportsStreaming: false,
            SupportsSpecDecode: false,
            Features: diarizeFeature
                ? new HashSet<Feature> { Feature.FeatureDiarization }
                : new HashSet<Feature>(),
            Languages: Array.Empty<string>(),
            TranslateTargetLanguages: Array.Empty<string>());
}
