using System.Collections.ObjectModel;
using TranscribeCppSharp.Audio;
using TranscribeCppSharp.Models;
using TranscribeCppSharp.UI.Models;
using TranscribeCppSharp.UI.Services;
using TranscribeCppSharp.UI.ViewModels;
using Xunit;

namespace TranscribeCppSharp.UI.Tests;

/// <summary>
/// The small review features: play a segment, find in the transcript, copy with
/// timestamps.
/// </summary>
[Collection("UiGlobalState")]
public class ReviewFeaturesTests
{
    private static async Task WithViewModel(
        ISegmentPlayer player,
        Func<TranscriptionViewModel, string, Task> test)
    {
        string? previous = ModelStore.CacheRootOverride;
        ModelStore.CacheRootOverride = Path.Combine(
            Path.GetTempPath(), "tcsharp-review-" + Guid.NewGuid().ToString("N"));
        using var settings = new UiSettingsScope();
        string audio = Path.Combine(
            Path.GetTempPath(), "tcsharp-review-" + Guid.NewGuid().ToString("N") + ".wav");
        File.WriteAllText(audio, string.Empty);
        try
        {
            var viewModel = new TranscriptionViewModel(
                new StubTranscriptionService(), new SettingsViewModel(), new ModelCapabilityCache(), player);
            viewModel.AudioPath = audio;
            await test(viewModel, audio);
        }
        finally
        {
            File.Delete(audio);
            ModelStore.CacheRootOverride = previous;
        }
    }

    private static MergedSegment Segment(double start, double end, int speaker, string text)
        => new(TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end), text, speaker);

    [Fact]
    public async Task PlayingASegmentAsksThePlayerForItsExactInterval()
    {
        var player = new RecordingPlayer(true, null);
        await WithViewModel(player, (viewModel, audio) =>
        {
            MergedSegment segment = Segment(4, 9, 0, "text");

            viewModel.PlaySegment(segment);

            Assert.NotNull(player.Last);
            Assert.Equal(audio, player.Last.Value.AudioPath);
            Assert.Equal(TimeSpan.FromSeconds(4), player.Last.Value.Start);
            Assert.Equal(TimeSpan.FromSeconds(9), player.Last.Value.End);
            Assert.Contains("Playing", viewModel.StatusMessage);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task PlayingAMissingFileDoesNotCallThePlayer()
    {
        var player = new RecordingPlayer(true, null);
        await WithViewModel(player, (viewModel, audio) =>
        {
            File.Delete(audio);

            viewModel.PlaySegment(Segment(0, 2, 0, "text"));

            Assert.Null(player.Last);
            Assert.Contains("no longer", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task APlayerThatRefusesSaysWhy()
    {
        var player = new RecordingPlayer(false, "ffplay was not found on PATH");
        await WithViewModel(player, (viewModel, _) =>
        {
            viewModel.PlaySegment(Segment(0, 2, 0, "text"));

            Assert.Contains("Cannot play", viewModel.StatusMessage);
            Assert.Contains("ffplay", viewModel.StatusMessage);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task FindNextMovesThroughTheMatchesAndWraps()
    {
        await WithViewModel(new RecordingPlayer(true, null), (viewModel, _) =>
        {
            viewModel.FullText = "one two one";
            viewModel.SearchQuery = "one";

            viewModel.FindNext();
            Assert.Equal((0, 3), (viewModel.SearchSelectionStart, viewModel.SearchSelectionEnd));

            viewModel.FindNext();
            Assert.Equal((8, 11), (viewModel.SearchSelectionStart, viewModel.SearchSelectionEnd));

            viewModel.FindNext();
            Assert.Equal((0, 3), (viewModel.SearchSelectionStart, viewModel.SearchSelectionEnd));
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task FindNextIsCaseInsensitiveAndSaysWhenThereIsNoMatch()
    {
        await WithViewModel(new RecordingPlayer(true, null), (viewModel, _) =>
        {
            viewModel.FullText = "The quick brown fox";
            viewModel.SearchQuery = "QUICK";
            viewModel.FindNext();
            Assert.Equal((4, 9), (viewModel.SearchSelectionStart, viewModel.SearchSelectionEnd));

            viewModel.SearchQuery = "zebra";
            viewModel.FindNext();
            Assert.Contains("No match", viewModel.StatusMessage);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task TheTimestampedCopyUsesNamesAndClocks()
    {
        await WithViewModel(new RecordingPlayer(true, null), (viewModel, _) =>
        {
            viewModel.Segments = new ObservableCollection<MergedSegment>
            {
                Segment(65, 70, 0, "first"),
                Segment(70, 72, 2, "second"),
            };
            viewModel.SpeakerNames.Add(new SpeakerNameEntry(0, "Alice"));

            string text = viewModel.BuildTimestampedTranscript();

            Assert.Equal(
                "[00:01:05] Alice: first\n[00:01:10] Speaker 2: second\n",
                text);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void TheFfplayArgumentsAreAListAndUseInvariantSeconds()
    {
        IReadOnlyList<string> arguments = FfplaySegmentPlayer.BuildArguments(
            "/audio/a b.wav", TimeSpan.FromSeconds(4.5), TimeSpan.FromSeconds(9.25));

        Assert.Equal(
            ["-nodisp", "-autoexit", "-loglevel", "error", "-ss", "4.5", "-t", "4.75", "/audio/a b.wav"],
            arguments);
    }

    private sealed class RecordingPlayer(bool result, string? error) : ISegmentPlayer
    {
        public (string AudioPath, TimeSpan Start, TimeSpan End)? Last { get; private set; }

        public bool TryPlay(string audioPath, TimeSpan start, TimeSpan endTime, out string? failure)
        {
            Last = (audioPath, start, endTime);
            failure = error;
            return result;
        }
    }
}
