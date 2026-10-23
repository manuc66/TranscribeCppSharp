using TranscribeCppSharp.Models;
using TranscribeCppSharp.UI.Models;
using TranscribeCppSharp.UI.Services;
using TranscribeCppSharp.UI.ViewModels;
using Xunit;

namespace TranscribeCppSharp.UI.Tests;

/// <summary>
/// The opt-in VAD trim: off by default, one downloaded VAD model selected, and a
/// run refused with a reason when the option is on but no VAD model is available.
/// </summary>
[Collection("UiGlobalState")]
public class TranscriptionVadTests
{
    private const string Asr = "whisper-tiny";
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
            Path.GetTempPath(), "tcsharp-vad-" + Guid.NewGuid().ToString("N"));
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
    public void TheTrimIsOffByDefaultAndPassesTheModelOnlyWhenOn()
    {
        InCache(viewModel =>
        {
            Plant(Asr);
            Plant(Vad);
            viewModel.RefreshModels();

            Assert.False(viewModel.UseVad);
            Assert.Contains(viewModel.VadOptions, option => option.Alias == Vad);
            Assert.Equal(Vad, viewModel.SelectedVadModel?.Alias);

            // Off: the picker may remember the model, but the run must not use it.
            Assert.Null(viewModel.BuildTranscriptionOptions().VadModel);

            viewModel.UseVad = true;

            Assert.Equal(Vad, viewModel.BuildTranscriptionOptions().VadModel);
            Assert.True(viewModel.VadSatisfied);
            Assert.Contains("detect speech (VAD)", viewModel.PipelineSummary, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void WithoutAVadModelTheRunSaysWhatIsMissing()
    {
        InCache(viewModel =>
        {
            Plant(Asr);
            viewModel.RefreshModels();

            viewModel.UseVad = true;

            Assert.Empty(viewModel.VadOptions);
            Assert.False(viewModel.VadSatisfied);
            Assert.True(viewModel.NeedsVadModelDownload);

            string audio = Path.Combine(
                Path.GetTempPath(), "tcsharp-vad-" + Guid.NewGuid().ToString("N") + ".wav");
            File.WriteAllText(audio, string.Empty);
            try
            {
                viewModel.AudioPath = audio;
                // The startup probe is asynchronous; the VAD gate is what this test
                // is about, so clear the flag rather than race it.
                viewModel.IsCheckingCapabilities = false;

                Assert.False(viewModel.CanTranscribe);
                Assert.Contains("VAD", viewModel.RunBlockedReason, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("Models", viewModel.RunBlockedReason, StringComparison.Ordinal);
            }
            finally
            {
                File.Delete(audio);
            }
        });
    }

    [Fact]
    public void TheGetAVadModelButtonOpensTheCatalogueOnTheVadRole()
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

            transcription.OpenVadModelsCommand.Execute(null);

            Assert.Equal(MainWindowViewModel.ModelTabIndex, root.SelectedTabIndex);
            Assert.Equal("vad", root.ModelManager.RoleFilter?.Key);
        });
    }
}
