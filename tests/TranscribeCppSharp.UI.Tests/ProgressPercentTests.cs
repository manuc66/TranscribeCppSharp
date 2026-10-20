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
