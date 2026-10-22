using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using TranscribeCppSharp.Audio;
using TranscribeCppSharp.Interop;
using TranscribeCppSharp.Models;
using TranscribeCppSharp.UI.Models;
using TranscribeCppSharp.UI.Services;
using TranscribeCppSharp.UI.ViewModels;
using TranscribeCppSharp.UI.Views;
using Xunit;

namespace TranscribeCppSharp.UI.Tests;

/// <summary>
/// Lays the Transcription tab out with a result and a model that declares a
/// vocabulary, and reads the new controls back from the arranged view.
/// </summary>
/// <remarks>
/// The automation-id test proves an id exists, not that its control is reachable:
/// a row put on top of another row keeps both ids and still puts one control out
/// of sight. This asserts what the reader would see — the vocabulary field and the
/// export options are visible with finite bounds, and neither lands on the row
/// below it.
/// </remarks>
[Collection("UiGlobalState")]
public class TranscriptionLayoutTests
{
    private static void WithViewModel(Action<TranscriptionViewModel> test)
    {
        string? previous = ModelStore.CacheRootOverride;
        ModelStore.CacheRootOverride = Path.Combine(
            Path.GetTempPath(), "tcsharp-layout-" + Guid.NewGuid().ToString("N"));
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

    private static ModelCapabilitySnapshot Snapshot(params Feature[] features)
        => new(
            Architecture: "test",
            Variant: "test",
            Roles: TranscribeRole.Asr,
            SupportsTranslate: false,
            SupportsLanguageDetect: false,
            SupportsStreaming: false,
            SupportsSpecDecode: false,
            Features: features.ToHashSet(),
            Languages: [],
            TranslateTargetLanguages: []);

    [AvaloniaFact]
    public void TheVocabularyAndExportControlsAreVisibleAndDoNotOverlapTheirNeighbours()
    {
        WithViewModel(viewModel =>
        {
            // The alias first: changing it starts a probe that clears the snapshot
            // when the model is not on disk, which would undo the one below.
            viewModel.ModelAlias = "whisper-tiny";
            viewModel.ApplyCapabilities(Snapshot(Feature.FeatureVocabulary));
            viewModel.AudioPath = "/audio/interview.wav";

            var result = new TranscriptionResult
            {
                FullText = "first second",
                DetectedLanguage = "en",
                Segments =
                [
                    new MergedSegment(TimeSpan.Zero, TimeSpan.FromSeconds(5), "first", 0),
                    new MergedSegment(TimeSpan.FromSeconds(9), TimeSpan.FromSeconds(10), "second", 2),
                ],
            };
            viewModel.Result = result;
            viewModel.Segments = new System.Collections.ObjectModel.ObservableCollection<MergedSegment>(result.Segments);
            // The run sets this after a result arrives; the layout is what is under test.
            viewModel.HasResult = true;

            var view = new TranscriptionView { DataContext = viewModel };
            var window = new Window { Content = view, Width = 1100, Height = 900 };
            window.Show();
            window.UpdateLayout();

            Control Find(string id) => view.GetLogicalDescendants()
                .OfType<Control>()
                .First(c => AutomationProperties.GetAutomationId(c) == id);

            // The tuning controls live behind the disclosure so the form stays
            // short. Opening it is what the reader does before using them, so this
            // is the state in which they must be laid out without overlapping.
            var advanced = (Expander)Find("transcription-advanced");
            advanced.IsExpanded = true;
            window.UpdateLayout();

            Control vocabulary = Find("transcription-vocabulary");
            Control windowSeconds = Find("transcription-window-seconds");
            Control exportOptions = Find("transcription-export-options");
            Control results = Find("transcription-results");

            Assert.True(vocabulary.IsEffectivelyVisible, "the vocabulary field is not visible");
            Assert.True(vocabulary.Bounds.Width > 0 && vocabulary.Bounds.Height > 0);
            Assert.True(exportOptions.IsEffectivelyVisible, "the export options are not visible");

            Rect BoundsIn(Control control)
                => control.TranslatePoint(new Point(0, 0), window) is { } origin
                    ? new Rect(origin, control.Bounds.Size)
                    : default;

            Assert.False(
                BoundsIn(vocabulary).Intersects(BoundsIn(windowSeconds)),
                "the vocabulary row overlaps the row below it");
            Assert.False(
                BoundsIn(exportOptions).Intersects(BoundsIn(results)),
                "the export options overlap the results");
        });
    }
}
