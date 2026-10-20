using TranscribeCppSharp.Audio;
using TranscribeCppSharp.Models;
using TranscribeCppSharp.UI.Models;
using TranscribeCppSharp.UI.Services;
using TranscribeCppSharp.UI.ViewModels;
using Xunit;

namespace TranscribeCppSharp.UI.Tests;

/// <summary>
/// The History tab and what a finished run leaves behind: a saved entry the tab
/// lists, and an Open that puts it back in the Transcription tab.
/// </summary>
[Collection("UiGlobalState")]
public class HistoryViewModelTests
{
    private static TranscriptionResult Result(string text = "hello")
        => new()
        {
            FullText = text,
            DetectedLanguage = "en",
            Segments =
            [
                new MergedSegment(TimeSpan.Zero, TimeSpan.FromSeconds(2), text, 0),
            ],
        };

    private static void WithEnvironment(Action test)
    {
        string? previous = ModelStore.CacheRootOverride;
        ModelStore.CacheRootOverride = Path.Combine(
            Path.GetTempPath(), "tcsharp-history-cache-" + Guid.NewGuid().ToString("N"));
        using var settings = new UiSettingsScope();
        try
        {
            test();
        }
        finally
        {
            ModelStore.CacheRootOverride = previous;
        }
    }

    [Fact]
    public void TheTabListsWhatIsOnDisk()
    {
        using var directory = new HistoryDirectoryScope();
        WithEnvironment(() =>
        {
            TranscriptionHistory.Save(
                "/audio/a.wav", "whisper-tiny", "en", "on", 300, Result("a"),
                new DateTimeOffset(2026, 10, 20, 10, 0, 0, TimeSpan.Zero));

            var viewModel = new HistoryViewModel();

            HistoryEntry entry = Assert.Single(viewModel.Entries);
            Assert.Equal("a.wav", entry.AudioName);
            Assert.Contains("1", viewModel.StatusMessage);
        });
    }

    [Fact]
    public void AnEmptyHistorySaysSo()
    {
        using var directory = new HistoryDirectoryScope();
        WithEnvironment(() =>
        {
            var viewModel = new HistoryViewModel();

            Assert.Empty(viewModel.Entries);
            Assert.Contains("no saved", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void OpenRaisesTheSelectedEntry()
    {
        using var directory = new HistoryDirectoryScope();
        WithEnvironment(() =>
        {
            TranscriptionHistory.Save(
                "/audio/a.wav", "m", "en", "on", 300, Result(),
                new DateTimeOffset(2026, 10, 20, 10, 0, 0, TimeSpan.Zero));
            var viewModel = new HistoryViewModel();
            HistoryEntry? opened = null;
            viewModel.OpenRequested += entry => opened = entry;
            viewModel.SelectedEntry = viewModel.Entries[0];

            viewModel.OpenCommand.Execute(null);

            Assert.Same(viewModel.Entries[0], opened);
        });
    }

    [Fact]
    public void DeleteRemovesTheSelectedEntry()
    {
        using var directory = new HistoryDirectoryScope();
        WithEnvironment(() =>
        {
            TranscriptionHistory.Save(
                "/audio/a.wav", "m", "en", "on", 300, Result(),
                new DateTimeOffset(2026, 10, 20, 10, 0, 0, TimeSpan.Zero));
            var viewModel = new HistoryViewModel();
            viewModel.SelectedEntry = viewModel.Entries[0];

            viewModel.DeleteCommand.Execute(null);

            Assert.Empty(viewModel.Entries);
            Assert.Empty(TranscriptionHistory.List());
        });
    }

    [Fact]
    public void AFinishedRunIsSavedWithoutBeingAsked()
    {
        using var directory = new HistoryDirectoryScope();
        WithEnvironment(() =>
        {
            var service = new FixedTranscriptionService(Result("saved by the run"));
            var viewModel = new TranscriptionViewModel(
                service, new SettingsViewModel(), new ModelCapabilityCache());
            string audio = Path.Combine(Path.GetTempPath(), "tcsharp-history-" + Guid.NewGuid().ToString("N") + ".wav");
            File.WriteAllText(audio, string.Empty);
            try
            {
                viewModel.AudioPath = audio;
                viewModel.ModelAlias = "whisper-tiny";
                viewModel.TranscribeCommand.ExecuteAsync(null).GetAwaiter().GetResult();

                HistoryEntry entry = Assert.Single(TranscriptionHistory.List());
                Assert.Equal(Path.GetFileName(audio), entry.AudioName);
                Assert.Equal("whisper-tiny", entry.ModelAlias);
                Assert.Equal("saved by the run", TranscriptionHistory.Load(entry)!.FullText);
            }
            finally
            {
                File.Delete(audio);
            }
        });
    }

    [Fact]
    public void RestorePutsAResultBackIntoTheTab()
    {
        WithEnvironment(() =>
        {
            var viewModel = new TranscriptionViewModel(
                new StubTranscriptionService(), new SettingsViewModel(), new ModelCapabilityCache());
            var entry = new HistoryEntry
            {
                FilePath = "/history/entry.json",
                SavedAt = new DateTimeOffset(2026, 10, 20, 10, 0, 0, TimeSpan.Zero),
                AudioPath = "/audio/interview.wav",
                ModelAlias = "whisper-tiny",
                Language = "en",
                Diarize = "on",
                WindowSeconds = 300,
                SegmentCount = 1,
            };

            viewModel.Restore(Result("restored"), entry);

            Assert.True(viewModel.HasResult);
            Assert.Equal("restored", viewModel.FullText);
            Assert.Equal("/audio/interview.wav", viewModel.AudioPath);
            Assert.Single(viewModel.Segments);
            Assert.Contains("history", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void ARestoredRunIsExportedWithItsOwnSettingsNotTheCurrentPickers()
    {
        // Opening an old run and exporting it must name the model and settings that
        // produced it, not whatever the pickers happen to say now.
        WithEnvironment(() =>
        {
            var viewModel = new TranscriptionViewModel(
                new StubTranscriptionService(), new SettingsViewModel(), new ModelCapabilityCache());
            viewModel.ModelAlias = "currently-selected";
            viewModel.WindowSeconds = 300;
            var entry = new HistoryEntry
            {
                FilePath = "/history/entry.json",
                SavedAt = new DateTimeOffset(2026, 10, 20, 10, 0, 0, TimeSpan.Zero),
                AudioPath = "/audio/old.wav",
                ModelAlias = "saved-model",
                Language = "fr",
                Diarize = "off",
                WindowSeconds = 120,
                SegmentCount = 1,
            };

            viewModel.Restore(Result("x"), entry);

            string html = viewModel.BuildHtmlExport();
            Assert.Contains("model saved-model", html);
            Assert.Contains("language fr", html);
            Assert.Contains("diarize off", html);
            Assert.Contains("window 120s", html);
            Assert.DoesNotContain("currently-selected", html);

            string markdown = viewModel.BuildMarkdownExport();
            Assert.Contains("- **Model:** `saved-model`", markdown);
            Assert.Contains("- **Window:** 120 s", markdown);
        });
    }

    [Fact]
    public void OpeningAnEntryFromTheHistoryTabRestoresItInTheTranscriptionTab()
    {
        using var directory = new HistoryDirectoryScope();
        WithEnvironment(() =>
        {
            TranscriptionHistory.Save(
                "/audio/interview.wav", "whisper-tiny", "en", "on", 300, Result("from disk"),
                new DateTimeOffset(2026, 10, 20, 10, 0, 0, TimeSpan.Zero));

            var transcription = new TranscriptionViewModel(
                new StubTranscriptionService(), new SettingsViewModel(), new ModelCapabilityCache());
            var streaming = new StreamingViewModel(
                new StubTranscriptionService(), new SettingsViewModel(), new ModelCapabilityCache());
            var batch = new BatchViewModel(new StubTranscriptionService(), new SettingsViewModel());
            var history = new HistoryViewModel();
            var main = new MainWindowViewModel(
                transcription, streaming, batch, new ModelManagerViewModel(),
                new SettingsViewModel(), history);

            history.SelectedEntry = history.Entries[0];
            history.OpenCommand.Execute(null);

            Assert.Equal(MainWindowViewModel.TranscriptionTabIndex, main.SelectedTabIndex);
            Assert.True(transcription.HasResult);
            Assert.Equal("from disk", transcription.FullText);
            Assert.Equal("/audio/interview.wav", transcription.AudioPath);
        });
    }

    private sealed class FixedTranscriptionService(TranscriptionResult result) : ITranscriptionService
    {
        public Task<TranscriptionResult> TranscribeAsync(
            string audioPath, string modelPath, TranscriptionOptions options,
            IProgress<double>? progress = null, IProgress<TranscriptionPhase>? phase = null, CancellationToken cancellationToken = default)
            => Task.FromResult(result);

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
