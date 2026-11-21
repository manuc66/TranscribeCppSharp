using TranscribeCppSharp.Models;
using TranscribeCppSharp.UI.Models;
using TranscribeCppSharp.UI.Services;
using TranscribeCppSharp.UI.ViewModels;
using Xunit;

namespace TranscribeCppSharp.UI.Tests;

/// <summary>
/// The Transcription tab remembers the goal and the VAD choice between launches:
/// they are choices, not results, and reopening the app should not reset them.
/// </summary>
[Collection("UiGlobalState")]
public class TranscriptionPersistenceTests
{
    private const string Vad = "silero-vad-v6.2";

    private static void Plant(string alias)
    {
        ModelDescriptor descriptor = ModelStore.Catalog.Single(m => m.Alias == alias);
        string path = ModelStore.CachedPath(descriptor);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "placeholder");
    }

    private static TranscriptionViewModel NewViewModel()
        => new(new StubTranscriptionService(), new SettingsViewModel(), new ModelCapabilityCache());

    [Fact]
    public void TheSettingsRoundTrip()
    {
        using var settings = new UiSettingsScope();

        UiSettings.SaveTranscriptionGoal(TranscriptionGoal.SpeakersOnly);
        UiSettings.SaveTranscriptionUseVad(true);
        UiSettings.SaveTranscriptionVadModel(Vad);
        UiSettings.SetGoalModel(TranscriptionGoal.Transcribe, "whisper-base");
        UiSettings.SetGoalModel(TranscriptionGoal.SpeakersOnly, "diar_streaming_sortformer_4spk-v2.1");

        Assert.Equal(TranscriptionGoal.SpeakersOnly, UiSettings.LoadTranscriptionGoal());
        Assert.True(UiSettings.LoadTranscriptionUseVad());
        Assert.Equal(Vad, UiSettings.LoadTranscriptionVadModel());
        Assert.Equal("whisper-base", UiSettings.GoalModel(TranscriptionGoal.Transcribe));
        Assert.Equal(
            "diar_streaming_sortformer_4spk-v2.1",
            UiSettings.GoalModel(TranscriptionGoal.SpeakersOnly));
        Assert.Null(UiSettings.GoalModel(TranscriptionGoal.DetectSpeech));
    }

    [Fact]
    public void EachGoalKeepsItsOwnModel()
    {
        string? previous = ModelStore.CacheRootOverride;
        ModelStore.CacheRootOverride = Path.Combine(
            Path.GetTempPath(), "tcsharp-goal-model-" + Guid.NewGuid().ToString("N"));
        using var settings = new UiSettingsScope();
        try
        {
            Plant("whisper-tiny");
            Plant("whisper-base");
            Plant("diar_streaming_sortformer_4spk-v2.1");
            UiSettings.SetGoalModel(TranscriptionGoal.Transcribe, "whisper-base");
            UiSettings.SetGoalModel(
                TranscriptionGoal.SpeakersOnly, "diar_streaming_sortformer_4spk-v2.1");

            TranscriptionViewModel viewModel = NewViewModel();
            Assert.Equal(TranscriptionGoal.Transcribe, viewModel.Goal);
            Assert.Equal("whisper-base", viewModel.ModelAlias);

            viewModel.Goal = TranscriptionGoal.SpeakersOnly;
            Assert.Equal("diar_streaming_sortformer_4spk-v2.1", viewModel.ModelAlias);

            // Returning to the text goal lands on the text model, not on the
            // diarizer that happened to be selected a moment ago.
            viewModel.Goal = TranscriptionGoal.Transcribe;
            Assert.Equal("whisper-base", viewModel.ModelAlias);
        }
        finally
        {
            ModelStore.CacheRootOverride = previous;
        }
    }

    [Fact]
    public void AnUnknownGoalFallsBackToTheTextGoal()
    {
        using var settings = new UiSettingsScope();
        Directory.CreateDirectory(Path.GetDirectoryName(UiSettings.FilePath)!);
        File.WriteAllText(UiSettings.FilePath, "{\"transcriptionGoal\":\"not-a-goal\"}");

        Assert.Equal(TranscriptionGoal.Transcribe, UiSettings.LoadTranscriptionGoal());
        Assert.False(UiSettings.LoadTranscriptionUseVad());
        Assert.Null(UiSettings.LoadTranscriptionVadModel());
    }

    [Fact]
    public void TheViewModelRestoresTheGoalAndTheVadChoice()
    {
        string? previous = ModelStore.CacheRootOverride;
        ModelStore.CacheRootOverride = Path.Combine(
            Path.GetTempPath(), "tcsharp-persist-" + Guid.NewGuid().ToString("N"));
        using var settings = new UiSettingsScope();
        try
        {
            Plant("whisper-tiny");
            Plant(Vad);

            TranscriptionViewModel first = NewViewModel();
            first.UseVad = true;
            first.Goal = TranscriptionGoal.IdentifyLanguage;

            // A second view model reads the same file, as the next launch would.
            TranscriptionViewModel second = NewViewModel();

            Assert.Equal(TranscriptionGoal.IdentifyLanguage, second.Goal);
            Assert.True(second.UseVad);
            Assert.Equal(Vad, second.SelectedVadModel?.Alias);

            // The VAD option is only a run setting for the ASR goals; the saved
            // choice is ignored by a language-identification run.
            Assert.Null(second.BuildTranscriptionOptions().VadModel);
        }
        finally
        {
            ModelStore.CacheRootOverride = previous;
        }
    }
}
