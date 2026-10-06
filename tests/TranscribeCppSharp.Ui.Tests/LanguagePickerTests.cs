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

// StreamUpdate exists in both TranscribeCppSharp.Interop and
// TranscribeCppSharp.Ui.Models; the interface returns the Ui one. Task does too:
// Interop declares a Task enum, so without this alias every Task<> here is ambiguous.
using StreamUpdate = TranscribeCppSharp.Ui.Models.StreamUpdate;
using Task = System.Threading.Tasks.Task;

namespace TranscribeCppSharp.Ui.Tests;

/// <summary>
/// The language picker has to show the language the view model holds.
/// </summary>
/// <remarks>
/// This is a binding that can fail without failing anywhere else. The picker starts
/// with an empty list, so it cannot match <c>Language</c> and drops its own selection
/// while the view model keeps the value. When a model report then rebuilds the list,
/// <c>Language</c> still equals the value it held, so nothing changes and the
/// short-circuiting binding never writes the control back up: the box rendered blank
/// with <c>Language</c> reading <c>"en"</c>, which a screenshot of this very tab
/// caught. Asserting the two sides agree is the only thing that notices.
/// </remarks>
public class LanguagePickerTests
{
    /// <summary>What the fake model reports it accepts.</summary>
    private static readonly string[] SupportedLanguages = ["en", "zh"];

    [AvaloniaFact]
    public void ThePickerShowsTheLanguageTheViewModelHoldsAfterAModelReports()
    {
        // Keep the constructor's probe from loading a real model: an empty cache
        // means nothing is on disk and no download happens.
        string? previous = ModelStore.CacheRootOverride;
        ModelStore.CacheRootOverride = Path.Combine(
            Path.GetTempPath(), "tcsharp-ui-tests-language-cache");
        try
        {
            var viewModel = new TranscriptionViewModel(
                new StubTranscriptionService(), new SettingsViewModel());
            var view = new TranscriptionView { DataContext = viewModel };
            var window = new Window { Content = view, Width = 1100, Height = 720 };
            window.Show();

            var combo = view.GetLogicalDescendants()
                .OfType<ComboBox>()
                .First(c => AutomationProperties.GetAutomationId(c) == "transcription-language");

            // Before the model answers, the fallback list is what is offered.
            Assert.Equal("en", viewModel.Language);
            Assert.Equal("en", combo.SelectedItem);

            // The model answers: the list is rebuilt under a picker that is already
            // bound, and the current language stays valid for it.
            viewModel.ApplyCapabilities(new ModelCapabilitySnapshot(
                Architecture: "test",
                Variant: "test",
                SupportsTranslate: false,
                SupportsLanguageDetect: false,
                SupportsStreaming: false,
                SupportsSpecDecode: false,
                Features: new HashSet<Feature>(),
                Languages: SupportedLanguages,
                TranslateTargetLanguages: Array.Empty<string>()));

            Assert.Equal(SupportedLanguages, viewModel.AvailableLanguages);
            Assert.Equal("en", viewModel.Language);

            // The assertion that matters: the two sides must agree. Clear() drops the
            // picker's selection, and nothing re-pushes a Language whose value never
            // changed, so without that push the box rendered blank with "en" sitting
            // in the view model — a control showing nothing while a correct value was
            // right there.
            Assert.Equal("en", combo.SelectedItem);
            Assert.Equal(0, combo.SelectedIndex);
        }
        finally
        {
            ModelStore.CacheRootOverride = previous;
        }
    }

    [AvaloniaFact]
    public void ALanguageOutsideTheNewListIsReplacedNotKept()
    {
        string? previous = ModelStore.CacheRootOverride;
        ModelStore.CacheRootOverride = Path.Combine(
            Path.GetTempPath(), "tcsharp-ui-tests-language-cache");
        try
        {
            var viewModel = new TranscriptionViewModel(
                new StubTranscriptionService(), new SettingsViewModel());
            var view = new TranscriptionView { DataContext = viewModel };
            var window = new Window { Content = view, Width = 1100, Height = 720 };
            window.Show();

            var combo = view.GetLogicalDescendants()
                .OfType<ComboBox>()
                .First(c => AutomationProperties.GetAutomationId(c) == "transcription-language");

            // "ko" is in the fallback list, so it can be picked; the model does not
            // offer it. Keeping it would be offering a language the model rejected —
            // the very thing this list was made dynamic to stop.
            viewModel.Language = "ko";
            Assert.Equal("ko", combo.SelectedItem);

            viewModel.ApplyCapabilities(new ModelCapabilitySnapshot(
                Architecture: "test",
                Variant: "test",
                SupportsTranslate: false,
                SupportsLanguageDetect: false,
                SupportsStreaming: false,
                SupportsSpecDecode: false,
                Features: new HashSet<Feature>(),
                Languages: SupportedLanguages,
                TranslateTargetLanguages: Array.Empty<string>()));

            Assert.Equal("en", viewModel.Language);
            Assert.Equal("en", combo.SelectedItem);
        }
        finally
        {
            ModelStore.CacheRootOverride = previous;
        }
    }

    /// <summary>Needs to exist; nothing here transcribes.</summary>
    private sealed class StubTranscriptionService : ITranscriptionService
    {
        public Task<TranscriptionResult> TranscribeAsync(
            string audioPath, string modelPath, TranscriptionOptions options,
            IProgress<double>? progress = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public async IAsyncEnumerable<StreamUpdate> StreamTranscribeAsync(
            string modelPath, StreamOptions options,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task<List<BatchItemResult>> BatchTranscribeAsync(
            IReadOnlyList<string> audioPaths, string modelPath, TranscriptionOptions options,
            IProgress<int>? progress = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}