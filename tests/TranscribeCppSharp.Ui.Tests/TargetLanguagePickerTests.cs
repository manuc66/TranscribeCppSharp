using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using TranscribeCppSharp.Interop;
using TranscribeCppSharp.Models;
using TranscribeCppSharp.Ui.Models;
using TranscribeCppSharp.Ui.Services;
using TranscribeCppSharp.Ui.ViewModels;
using TranscribeCppSharp.Ui.Views;
using Xunit;

namespace TranscribeCppSharp.Ui.Tests;

/// <summary>
/// The target-language picker shows a choice that matches what would be sent.
/// </summary>
/// <remarks>
/// This was a free-text box: the reader typed a code and the native library
/// rejected anything outside the model's list, which is the worst possible place to
/// find out you were wrong. The list now comes from the model, and the two states
/// that matter are that "(model default)" is always labelled — never a blank box —
/// and that a choice the model no longer declares is dropped rather than passed on.
/// </remarks>
public class TargetLanguagePickerTests
{
    /// <summary>Expected labels, hoisted so the analyser does not see it rebuilt per call.</summary>
    private static readonly string[] ExpectedLabels = ["(model default)", "en", "fr"];

    private static ModelCapabilitySnapshot Snapshot(params string[] targetLanguages)
        => new(
            Architecture: "test",
            Variant: "test",
            SupportsTranslate: true,
            SupportsLanguageDetect: false,
            SupportsStreaming: false,
            SupportsSpecDecode: false,
            Features: new HashSet<Feature>(),
            Languages: Array.Empty<string>(),
            TranslateTargetLanguages: targetLanguages);

    [AvaloniaFact]
    public void BeforeAModelAnswersOnlyTheDefaultIsOffered()
    {
        var (viewModel, combo) = Bound();

        Assert.Equal(new TargetOption[] { new("(model default)", null) }, viewModel.TargetOptions);
        Assert.Equal("(model default)", combo.SelectedItem is TargetOption o ? o.Display : null);

        // Nothing to pass through: the model's own default is what it falls back to.
        Assert.Null(viewModel.TargetLanguage);
    }

    [AvaloniaFact]
    public void TheModelDeclaresTheChoices()
    {
        var (viewModel, combo) = Bound();

        viewModel.ApplyCapabilities(Snapshot("en", "fr"));

        Assert.Equal(ExpectedLabels, viewModel.TargetOptions.Select(o => o.Display));
        Assert.Equal("(model default)", combo.SelectedItem is TargetOption o ? o.Display : null);
        Assert.Null(viewModel.TargetLanguage);
    }

    [AvaloniaFact]
    public void PickingOnePassesItsCodeThrough()
    {
        var (viewModel, combo) = Bound();
        viewModel.ApplyCapabilities(Snapshot("en", "fr"));

        TargetOption french = viewModel.TargetOptions.First(o => o.Code == "fr");
        viewModel.SelectedTargetOption = french;

        Assert.Equal("fr", viewModel.TargetLanguage);
        Assert.Same(french, combo.SelectedItem);
    }

    /// <summary>
    /// Rebuilding the list must not leave a code the model will refuse, and must not
    /// leave the box blank while it does it.
    /// </summary>
    [AvaloniaFact]
    public void AModelThatNoLongerDeclaresTheChoiceLosesIt()
    {
        var (viewModel, combo) = Bound();
        viewModel.ApplyCapabilities(Snapshot("en", "fr"));
        viewModel.SelectedTargetOption = viewModel.TargetOptions.First(o => o.Code == "fr");
        Assert.Equal("fr", viewModel.TargetLanguage);

        viewModel.ApplyCapabilities(Snapshot("en"));

        // The assertion that catches the blank box: the view model and the control
        // must agree, and the control must show a label either way.
        Assert.Null(viewModel.TargetLanguage);
        Assert.IsType<TargetOption>(combo.SelectedItem);
        Assert.Equal("(model default)", ((TargetOption)combo.SelectedItem!).Display);
    }

    private static (TranscriptionViewModel ViewModel, ComboBox Combo) Bound()
    {
        // An empty cache: no model to load, so nothing is reported and the probe
        // cannot run away with the test.
        // Deliberately not restored: every test that cares about the cache sets
        // its own root first, so leaving one behind cannot make a test read another
        // test's model as its own.
        ModelStore.CacheRootOverride = Path.Combine(
            Path.GetTempPath(), "tcsharp-ui-tests-target-language");

        var viewModel = new TranscriptionViewModel(
            new StubTranscriptionService(), new SettingsViewModel());
        var view = new TranscriptionView { DataContext = viewModel };
        var window = new Window { Content = view, Width = 1100, Height = 720 };
        window.Show();

        ComboBox combo = view.GetLogicalDescendants()
            .OfType<ComboBox>()
            .First(c => AutomationProperties.GetAutomationId(c) == "transcription-target-language");

        return (viewModel, combo);
    }
}