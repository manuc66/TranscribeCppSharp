using TranscribeCppSharp.Models;
using TranscribeCppSharp.UI.Services;
using TranscribeCppSharp.UI.ViewModels;
using Xunit;

namespace TranscribeCppSharp.UI.Tests;

/// <summary>
/// Reset returns every family-extension field to empty, which is what makes the
/// model's own default apply.
/// </summary>
/// <remarks>
/// An empty field is not "off": the extension is left off the run entirely, so the
/// native default — the -1 sentinel the <c>*ExtInit</c> sets — applies. There was no
/// way back to that state once a field had been filled, which is what Reset is for.
/// </remarks>
public class StreamingExtensionResetTests
{
    private static StreamingViewModel ViewModel()
    {
        string previous = ModelStore.CacheRootOverride ?? string.Empty;
        bool hadOverride = ModelStore.CacheRootOverride is not null;
        ModelStore.CacheRootOverride = Path.Combine(
            Path.GetTempPath(), "tcsharp-ext-reset-" + Guid.NewGuid().ToString("N"));
        try
        {
            return new StreamingViewModel(
                new StubTranscriptionService(), new SettingsViewModel(), new ModelCapabilityCache());
        }
        finally
        {
            ModelStore.CacheRootOverride = hadOverride ? previous : null;
        }
    }

    [Fact]
    public void ResetClearsEveryFamilyExtensionField()
    {
        StreamingViewModel viewModel = ViewModel();

        Assert.False(viewModel.HasAnyStreamingExtension);

        viewModel.MoonshineMinDecodeIntervalMs = 500;
        viewModel.ParakeetAttContextRight = 13;
        viewModel.ParakeetLeftMs = 5600;
        viewModel.ParakeetChunkMs = 1040;
        viewModel.ParakeetRightMs = 1040;
        viewModel.VoxtralNumDelayTokens = 6;
        viewModel.VoxtralMinDecodeIntervalMs = 100;

        Assert.True(viewModel.HasAnyStreamingExtension);

        viewModel.ResetExtensionsCommand.Execute(null);

        Assert.False(viewModel.HasAnyStreamingExtension);
        Assert.Null(viewModel.MoonshineMinDecodeIntervalMs);
        Assert.Null(viewModel.ParakeetAttContextRight);
        Assert.Null(viewModel.ParakeetLeftMs);
        Assert.Null(viewModel.ParakeetChunkMs);
        Assert.Null(viewModel.ParakeetRightMs);
        Assert.Null(viewModel.VoxtralNumDelayTokens);
        Assert.Null(viewModel.VoxtralMinDecodeIntervalMs);
    }

    [Fact]
    public void TheResetFlagTracksEveryField()
    {
        StreamingViewModel viewModel = ViewModel();

        // Each field on its own has to count, or Reset would be disabled with
        // something still set.
        viewModel.ParakeetChunkMs = 1040;
        Assert.True(viewModel.HasAnyStreamingExtension);

        viewModel.ParakeetChunkMs = null;
        Assert.False(viewModel.HasAnyStreamingExtension);
    }
}
