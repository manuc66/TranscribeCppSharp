using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using TranscribeCppSharp.Models;
using TranscribeCppSharp.UI.Services;
using TranscribeCppSharp.UI.ViewModels;
using TranscribeCppSharp.UI.Views;
using Xunit;

namespace TranscribeCppSharp.UI.Tests;

/// <summary>
/// The progress bar carries its own percentage. It is the same Progress value the
/// bar fills with, so the test sets that value and reads the label back from an
/// arranged view.
/// </summary>
[Collection("UiGlobalState")]
public class ProgressPercentTests
{
    [AvaloniaFact]
    public void TheCountableStageIsShownAboveTheBar()
    {
        string? previous = ModelStore.CacheRootOverride;
        ModelStore.CacheRootOverride = Path.Combine(
            Path.GetTempPath(), "tcsharp-stage-view-" + Guid.NewGuid().ToString("N"));
        using var settings = new UiSettingsScope();
        try
        {
            var viewModel = new TranscriptionViewModel(
                new StubTranscriptionService(), new SettingsViewModel(), new ModelCapabilityCache())
            {
                IsTranscribing = true,
                Progress = 0.25,
                ProgressStage = "Transcribing — window 3 of 12",
            };
            var view = new TranscriptionView { DataContext = viewModel };
            var window = new Window { Content = view, Width = 1100, Height = 900 };
            window.Show();
            window.UpdateLayout();

            var stage = view.GetLogicalDescendants()
                .OfType<TextBlock>()
                .First(t => AutomationProperties.GetAutomationId(t) == "transcription-progress-stage");

            Assert.Equal("Transcribing — window 3 of 12", stage.Text);
            Assert.True(stage.IsEffectivelyVisible, "the progress stage is not visible");
        }
        finally
        {
            ModelStore.CacheRootOverride = previous;
        }
    }

    [AvaloniaFact]
    public void TheBatchNamesTheFileInFlight()
    {
        string? previous = ModelStore.CacheRootOverride;
        ModelStore.CacheRootOverride = Path.Combine(
            Path.GetTempPath(), "tcsharp-batch-stage-view-" + Guid.NewGuid().ToString("N"));
        using var settings = new UiSettingsScope();
        try
        {
            var viewModel = new BatchViewModel(new StubTranscriptionService(), new SettingsViewModel())
            {
                IsProcessing = true,
                Progress = 0.4,
                CurrentFileStep = "File 2 of 5",
                CurrentFile = "two.wav",
                CurrentFileStatus = "Reading the audio and transcribing…",
            };
            var view = new BatchView { DataContext = viewModel };
            var window = new Window { Content = view, Width = 1100, Height = 900 };
            window.Show();
            window.UpdateLayout();

            Control Find(string id) => view.GetLogicalDescendants()
                .OfType<Control>()
                .First(c => AutomationProperties.GetAutomationId(c) == id);

            Assert.Equal("File 2 of 5", ((TextBlock)Find("batch-current-file-step")).Text);
            Assert.Equal("two.wav", ((TextBlock)Find("batch-current-file")).Text);
            Assert.Contains("Reading", ((TextBlock)Find("batch-current-file-status")).Text, StringComparison.Ordinal);
            Assert.True(Find("batch-current-file").IsEffectivelyVisible,
                "the current file is not visible while the batch runs");
        }
        finally
        {
            ModelStore.CacheRootOverride = previous;
        }
    }

    [AvaloniaFact]
    public void ThePercentageIsShownBesideTheBar()
    {
        string? previous = ModelStore.CacheRootOverride;
        ModelStore.CacheRootOverride = Path.Combine(
            Path.GetTempPath(), "tcsharp-percent-" + Guid.NewGuid().ToString("N"));
        using var settings = new UiSettingsScope();
        try
        {
            var viewModel = new TranscriptionViewModel(
                new StubTranscriptionService(), new SettingsViewModel(), new ModelCapabilityCache())
            {
                IsTranscribing = true,
                Progress = 0.42,
            };
            var view = new TranscriptionView { DataContext = viewModel };
            var window = new Window { Content = view, Width = 1100, Height = 900 };
            window.Show();
            window.UpdateLayout();

            var label = view.GetLogicalDescendants()
                .OfType<TextBlock>()
                .First(t => AutomationProperties.GetAutomationId(t) == "transcription-progress-percent");

            // The separator depends on the culture ("42%" or "42 %"), so the
            // assertion is about the number and the sign, not the spacing.
            Assert.Contains("42", label.Text, StringComparison.Ordinal);
            Assert.Contains("%", label.Text, StringComparison.Ordinal);
        }
        finally
        {
            ModelStore.CacheRootOverride = previous;
        }
    }
}
