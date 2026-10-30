using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using TranscribeCppSharp.Models;
using TranscribeCppSharp.UI.Models;
using TranscribeCppSharp.UI.Services;
using TranscribeCppSharp.UI.ViewModels;
using TranscribeCppSharp.UI.Views;
using Xunit;

namespace TranscribeCppSharp.UI.Tests;

/// <summary>
/// The decoder-language picker shows a name beside the code, and the code stays
/// the value. A label is a reading aid: the run has to pass exactly what the
/// model reported, so the assertion that matters is that the item is still the
/// code while what is drawn is not.
/// </summary>
[Collection("UiGlobalState")]
public class LanguageNameDisplayTests
{
    [Theory]
    [InlineData("en", "en — English")]
    [InlineData("fr", "fr — French")]
    [InlineData("zh", "zh — Chinese")]
    [InlineData("EN", "EN — English")]
    [InlineData("auto", "auto — detect the language")]
    [InlineData("AUTO", "AUTO — detect the language")]
    public void AKnownCodeKeepsItsCodeAndGainsAName(string code, string expected)
        => Assert.Equal(expected, LanguageNames.Describe(code));

    [Theory]
    [InlineData("xx")]
    [InlineData("en-US")]
    [InlineData("qaa")]
    public void AnUnknownCodeIsShownUnchanged(string code)
        => Assert.Equal(code, LanguageNames.Describe(code));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEmptyCodeHasNoLabel(string code)
        => Assert.Equal(string.Empty, LanguageNames.Describe(code));

    [AvaloniaFact]
    public void ThePickerDrawsTheNameButHoldsTheCode()
    {
        // Keep the constructor's probe from loading a real model: an empty cache
        // means nothing is on disk and no download happens.
        string? previous = ModelStore.CacheRootOverride;
        ModelStore.CacheRootOverride = Path.Combine(
            Path.GetTempPath(), "tcsharp-ui-tests-language-name-cache");

        // The language column is only built for an ASR goal, and the goal is read
        // from the settings file: without this scope the test used whatever the
        // machine running it had saved, and passed or failed on that. Same
        // isolation as the two goal-dependent tests in test(ui).
        using var settings = new UiSettingsScope();
        try
        {
            var viewModel = new TranscriptionViewModel(
                new StubTranscriptionService(), new SettingsViewModel(), new ModelCapabilityCache());
            var view = new TranscriptionView { DataContext = viewModel };
            var window = new Window { Content = view, Width = 1100, Height = 720 };
            window.Show();

            var combo = view.GetLogicalDescendants()
                .OfType<ComboBox>()
                .First(c => AutomationProperties.GetAutomationId(c) == "transcription-language");

            combo.ApplyTemplate();
            window.UpdateLayout();

            // The value the binding reads is still the code.
            Assert.Equal("en", combo.SelectedItem);

            // The name is what the closed box draws. If the item template were
            // ignored for the selection box, this text would not exist and the
            // control would keep showing the bare code.
            Assert.Contains(
                combo.GetVisualDescendants().OfType<TextBlock>(),
                text => text.Text == "en — English");
        }
        finally
        {
            ModelStore.CacheRootOverride = previous;
        }
    }
}
