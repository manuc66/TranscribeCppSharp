using System.Collections.ObjectModel;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using TranscribeCppSharp.Audio;
using TranscribeCppSharp.Models;
using TranscribeCppSharp.SharedSource;
using TranscribeCppSharp.UI.Models;
using TranscribeCppSharp.UI.Services;
using TranscribeCppSharp.UI.ViewModels;
using TranscribeCppSharp.UI.Views;
using Xunit;

namespace TranscribeCppSharp.UI.Tests;

/// <summary>
/// The Transcription tab's export options: speaker names, pause markers, and how
/// often a timestamp is written.
/// </summary>
/// <remarks>
/// These change only what the exported document says. The transcript the model
/// produced is untouched, and the Text tab still shows it as it came back — the
/// pause markers exist in the export, not in the text.
/// </remarks>
[Collection("UiGlobalState")]
public class ExportOptionsTests
{
    private static void WithViewModel(Action<TranscriptionViewModel> test, Action? setup = null)
    {
        // Keep the constructor's capability probe from loading a real model, and
        // point the settings file at a temporary one: the view model now restores
        // and saves the export options there.
        string? previous = ModelStore.CacheRootOverride;
        ModelStore.CacheRootOverride = Path.Combine(
            Path.GetTempPath(), "tcsharp-export-" + Guid.NewGuid().ToString("N"));
        using var settings = new UiSettingsScope();
        try
        {
            setup?.Invoke();
            var viewModel = new TranscriptionViewModel(
                new StubTranscriptionService(), new SettingsViewModel(), new ModelCapabilityCache());
            test(viewModel);
        }
        finally
        {
            ModelStore.CacheRootOverride = previous;
        }
    }

    private static TranscriptionResult Result(params (double Start, double End, int Speaker, string Text)[] segments)
        => new()
        {
            FullText = string.Join(' ', segments.Select(s => s.Text)),
            Segments = segments
                .Select(s => new MergedSegment(
                    TimeSpan.FromSeconds(s.Start), TimeSpan.FromSeconds(s.End), s.Text, s.Speaker))
                .ToList(),
        };

    private static void Load(TranscriptionViewModel viewModel, TranscriptionResult result)
    {
        // The real run sets both: Result, then the collection the grid shows.
        viewModel.Result = result;
        viewModel.Segments = new ObservableCollection<MergedSegment>(result.Segments);
    }

    [Fact]
    public void ExportOptions_StartAtTheirDocumentedDefaults()
    {
        WithViewModel(viewModel =>
        {
            Assert.False(viewModel.MarkPauses);
            Assert.Equal(1, viewModel.PauseThresholdSeconds);
            Assert.Equal(60, viewModel.TimestampIntervalSeconds);
            Assert.Empty(viewModel.SpeakerNames);
            Assert.False(viewModel.HasSpeakerNames);
        });
    }

    [Fact]
    public void SpeakerNames_ListEverySpeakerInTheResult()
    {
        WithViewModel(viewModel =>
        {
            Load(viewModel, Result((0, 1, 0, "a"), (1, 2, 2, "b")));

            Assert.True(viewModel.HasSpeakerNames);
            Assert.Equal([0, 2], viewModel.SpeakerNames.Select(e => e.SpeakerId));
            Assert.Equal("Speaker 0", viewModel.SpeakerNames[0].Label);
            Assert.All(viewModel.SpeakerNames, e => Assert.Equal(string.Empty, e.Name));
        });
    }

    [Fact]
    public void SpeakerNames_AreNotShownForASingleSpeakerResult()
    {
        WithViewModel(viewModel =>
        {
            Load(viewModel, Result((0, 1, 0, "a"), (1, 2, 0, "b")));

            Assert.False(viewModel.HasSpeakerNames);
            Assert.Empty(viewModel.SpeakerNames);
        });
    }

    [Fact]
    public void SpeakerNames_SurviveARerunWithTheSameSpeakers()
    {
        WithViewModel(viewModel =>
        {
            Load(viewModel, Result((0, 1, 0, "a"), (1, 2, 2, "b")));
            viewModel.SpeakerNames[0].Name = "Alice";

            Load(viewModel, Result((0, 1, 0, "a"), (1, 2, 2, "b")));

            Assert.Equal("Alice", viewModel.SpeakerNames.Single(e => e.SpeakerId == 0).Name);
            Assert.Equal(string.Empty, viewModel.SpeakerNames.Single(e => e.SpeakerId == 2).Name);
        });
    }

    [Fact]
    public void BuildHtmlExport_UsesTheNamesPausesAndInterval()
    {
        WithViewModel(viewModel =>
        {
            viewModel.AudioPath = "/audio/interview.wav";
            Load(viewModel, Result((0, 5, 0, "first"), (9, 10, 2, "second")));
            viewModel.SpeakerNames.Single(e => e.SpeakerId == 0).Name = "Alice";
            viewModel.MarkPauses = true;
            viewModel.PauseThresholdSeconds = 1;
            viewModel.TimestampIntervalSeconds = 30;

            string html = viewModel.BuildHtmlExport();

            Assert.Contains("source: /audio/interview.wav", html);
            Assert.Contains("audio: unknown", html);
            Assert.Contains("<span class=\"speaker\">Alice:</span>", html);
            Assert.Contains("<span class=\"speaker\">Speaker 2:</span>", html);
            // 4 s of silence between the two segments: four dots.
            Assert.Contains("<span class=\"pause\">[....]</span>", html);
            // A separate test covers the interval itself; this one checks it is
            // read from the view model's setting and not left at 60 s.
            Assert.Contains("[00:00:09]", html);
        });
    }

    [Fact]
    public void BuildHtmlExport_LeavesPauseMarkersOutWhenTheBoxIsUnchecked()
    {
        WithViewModel(viewModel =>
        {
            Load(viewModel, Result((0, 5, 0, "first"), (9, 10, 0, "second")));
            viewModel.MarkPauses = false;
            viewModel.PauseThresholdSeconds = 1;

            Assert.DoesNotContain("class=\"pause\"", viewModel.BuildHtmlExport());
        });
    }

    [Fact]
    public void BuildVttExport_UsesTheSpeakerNames()
    {
        WithViewModel(viewModel =>
        {
            Load(viewModel, Result((0, 1, 0, "a"), (1, 2, 2, "b")));
            viewModel.SpeakerNames.Single(e => e.SpeakerId == 0).Name = "Alice";

            string vtt = viewModel.BuildVttExport();

            Assert.Contains("<v Alice>a</v>", vtt);
            Assert.Contains("<v Speaker 2>b</v>", vtt);
            Assert.DoesNotContain("<v Speaker 0>", vtt);
        });
    }

    [Fact]
    public void BuildSubtitleExport_WritesSrtWithNamesAndCommaMilliseconds()
    {
        WithViewModel(viewModel =>
        {
            Load(viewModel, Result((0, 1, 0, "a"), (1, 2, 2, "b")));
            viewModel.SpeakerNames.Single(e => e.SpeakerId == 0).Name = "Alice";

            string srt = viewModel.BuildSubtitleExport(SubtitleFormat.Srt);

            Assert.StartsWith("1\n00:00:00,000 --> 00:00:01,000\nAlice: a\n\n", srt, StringComparison.Ordinal);
            Assert.Contains("2\n00:00:01,000 --> 00:00:02,000\nSpeaker 2: b\n", srt, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void BuildSubtitleExport_WritesAnAssScript()
    {
        WithViewModel(viewModel =>
        {
            Load(viewModel, Result((0, 1, 0, "a")));

            string ass = viewModel.BuildSubtitleExport(SubtitleFormat.Ass);

            Assert.Contains("[Script Info]", ass, StringComparison.Ordinal);
            Assert.Contains("[V4+ Styles]", ass, StringComparison.Ordinal);
            Assert.Contains(
                "Dialogue: 0,0:00:00.00,0:00:01.00,Default,,0,0,0,,Speaker 0: a",
                ass,
                StringComparison.Ordinal);
        });
    }

    [AvaloniaFact]
    public void ExportOptionControls_WriteIntoTheViewModel()
    {
        // The bindings are the one thing a view-model test cannot see: a control
        // bound to nothing fails silently. This realizes the view and drives the
        // two controls the export reads.
        WithViewModel(viewModel =>
        {
            var view = new TranscriptionView { DataContext = viewModel };
            var window = new Window { Content = view, Width = 1100, Height = 720 };
            window.Show();

            var markPauses = view.GetLogicalDescendants()
                .OfType<CheckBox>()
                .First(c => AutomationProperties.GetAutomationId(c) == "transcription-mark-pauses");
            markPauses.IsChecked = true;
            Assert.True(viewModel.MarkPauses);

            var interval = view.GetLogicalDescendants()
                .OfType<NumericUpDown>()
                .First(c => AutomationProperties.GetAutomationId(c) == "transcription-timestamp-interval");
            interval.Value = 30;
            Assert.Equal(30, viewModel.TimestampIntervalSeconds);

            var threshold = view.GetLogicalDescendants()
                .OfType<NumericUpDown>()
                .First(c => AutomationProperties.GetAutomationId(c) == "transcription-pause-threshold");
            threshold.Value = 3;
            Assert.Equal(3, viewModel.PauseThresholdSeconds);
        });
    }

    [Fact]
    public void ExportOptions_AreRestoredOnTheNextLaunch()
    {
        WithViewModel(
            viewModel =>
            {
                Assert.True(viewModel.MarkPauses);
                Assert.Equal(2, viewModel.PauseThresholdSeconds);
                Assert.Equal(30, viewModel.TimestampIntervalSeconds);
            },
            setup: () => UiSettings.SaveExportSettings(new ExportSettings(true, 2, 30)));
    }

    [Fact]
    public void ChangingAnExportOptionSavesItImmediately()
    {
        WithViewModel(viewModel =>
        {
            viewModel.MarkPauses = true;
            viewModel.PauseThresholdSeconds = 3;
            viewModel.TimestampIntervalSeconds = 15;

            ExportSettings saved = UiSettings.LoadExportSettings();
            Assert.True(saved.MarkPauses);
            Assert.Equal(3, saved.PauseThresholdSeconds);
            Assert.Equal(15, saved.TimestampIntervalSeconds);
        });
    }

    [Fact]
    public void ADroppedAudioFileBecomesTheSelectedAudio()
    {
        WithViewModel(viewModel =>
        {
            string path = Path.Combine(Path.GetTempPath(), "tcsharp-drop-" + Guid.NewGuid().ToString("N") + ".wav");
            File.WriteAllText(path, string.Empty);
            try
            {
                Assert.True(viewModel.TryAcceptDroppedFile(path));
                Assert.Equal(path, viewModel.AudioPath);
            }
            finally
            {
                File.Delete(path);
            }
        });
    }

    [Fact]
    public void ADroppedFileWithAnotherExtensionIsRefused()
    {
        WithViewModel(viewModel =>
        {
            string path = Path.Combine(Path.GetTempPath(), "tcsharp-drop-" + Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllText(path, "not audio");
            try
            {
                Assert.False(viewModel.TryAcceptDroppedFile(path));
                Assert.Equal(string.Empty, viewModel.AudioPath);
            }
            finally
            {
                File.Delete(path);
            }
        });
    }

    [Fact]
    public void AMissingDroppedFileIsRefused()
    {
        WithViewModel(viewModel =>
            Assert.False(viewModel.TryAcceptDroppedFile(
                Path.Combine(Path.GetTempPath(), "tcsharp-drop-missing.wav"))));
    }

    [Fact]
    public void AnEmptyDroppedPathIsRefused()
    {
        WithViewModel(viewModel =>
        {
            Assert.False(viewModel.TryAcceptDroppedFile(null));
            Assert.False(viewModel.TryAcceptDroppedFile("   "));
        });
    }

    [Fact]
    public void BuildHtmlExport_UsesTheRecordedDuration()
    {
        WithViewModel(viewModel =>
        {
            viewModel.AudioPath = "/audio/interview.wav";
            TranscriptionResult result = Result((0, 5, 0, "first"));
            result.Duration = TimeSpan.FromSeconds(65);
            Load(viewModel, result);

            Assert.Contains("audio: 01:05", viewModel.BuildHtmlExport());
        });
    }

    [Fact]
    public void BuildMarkdownExport_WritesTheHeaderSpeakersAndTimestamps()
    {
        WithViewModel(viewModel =>
        {
            viewModel.AudioPath = "/audio/interview.wav";
            viewModel.ModelAlias = "whisper-tiny";
            Load(viewModel, Result((0, 5, 0, "first"), (9, 10, 2, "second")));
            viewModel.SpeakerNames.Single(e => e.SpeakerId == 0).Name = "Alice";
            viewModel.TimestampIntervalSeconds = 30;

            string markdown = viewModel.BuildMarkdownExport();

            Assert.StartsWith("# interview.wav\n", markdown);
            Assert.Contains("- **Source:** `/audio/interview.wav`", markdown);
            Assert.Contains("- **Model:** `whisper-tiny`", markdown);
            Assert.Contains("**Alice** `[00:00:00]` — first", markdown);
            Assert.Contains("**Speaker 2** `[00:00:09]` — second", markdown);
        });
    }

    [Fact]
    public void BuildMarkdownExport_MarksPausesWhenEnabled()
    {
        WithViewModel(viewModel =>
        {
            Load(viewModel, Result((0, 5, 0, "first"), (9, 10, 0, "second")));
            viewModel.MarkPauses = true;
            viewModel.PauseThresholdSeconds = 1;

            // 4 s of silence: four dots, on a line of their own.
            Assert.Contains("*[....]*", viewModel.BuildMarkdownExport());
        });
    }

    [Fact]
    public void BuildMarkdownExport_StampsOnlyTheFirstSegmentAndSpeakerChangesWhenTheIntervalIsZero()
    {
        WithViewModel(viewModel =>
        {
            Load(viewModel, Result(
                (0, 1, 0, "a"),
                (30, 31, 0, "b"),
                (60, 61, 1, "c")));
            viewModel.TimestampIntervalSeconds = 0;

            string markdown = viewModel.BuildMarkdownExport();

            Assert.Contains("`[00:00:00]`", markdown);
            Assert.DoesNotContain("`[00:00:30]`", markdown);
            Assert.Contains("`[00:01:00]`", markdown);
        });
    }

    [Fact]
    public void BuildMarkdownExport_EscapesMarkdownCharactersInTheText()
    {
        WithViewModel(viewModel =>
        {
            Load(viewModel, Result((0, 1, 0, "he said *hi* and <left> [note]")));

            string markdown = viewModel.BuildMarkdownExport();

            Assert.Contains(@"he said \*hi\* and \<left\> \[note\]", markdown);
        });
    }
}
