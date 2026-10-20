using TranscribeCppSharp.Audio;
using TranscribeCppSharp.UI.Models;
using TranscribeCppSharp.UI.Services;
using Xunit;

namespace TranscribeCppSharp.UI.Tests;

/// <summary>
/// The local history: one JSON file per finished run, listed newest first and
/// reloadable without the audio file.
/// </summary>
[Collection("UiGlobalState")]
public class HistoryStoreTests
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

    [Fact]
    public void SaveThenListAndLoadRoundTrips()
    {
        using var directory = new HistoryDirectoryScope();

        HistoryEntry saved = TranscriptionHistory.Save(
            "/audio/interview.wav", "whisper-tiny", "en", "on", 300, Result(),
            new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero));

        IReadOnlyList<HistoryEntry> entries = TranscriptionHistory.List();
        HistoryEntry entry = Assert.Single(entries);
        Assert.Equal(saved.FilePath, entry.FilePath);
        Assert.Equal("interview.wav", entry.AudioName);
        Assert.Equal("whisper-tiny", entry.ModelAlias);
        Assert.Equal("en", entry.Language);
        Assert.Equal("on", entry.Diarize);
        Assert.Equal(300, entry.WindowSeconds);
        Assert.Equal(1, entry.SegmentCount);
        Assert.Equal(new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero), entry.SavedAt);

        TranscriptionResult? loaded = TranscriptionHistory.Load(entry);
        Assert.NotNull(loaded);
        Assert.Equal("hello", loaded.FullText);
        Assert.Single(loaded.Segments);
    }

    [Fact]
    public void TheListIsNewestFirst()
    {
        using var directory = new HistoryDirectoryScope();

        TranscriptionHistory.Save(
            "/audio/old.wav", "m", "en", "on", 300, Result("old"),
            new DateTimeOffset(2026, 10, 20, 10, 0, 0, TimeSpan.Zero));
        TranscriptionHistory.Save(
            "/audio/new.wav", "m", "en", "on", 300, Result("new"),
            new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero));

        Assert.Equal(["new.wav", "old.wav"], TranscriptionHistory.List().Select(e => e.AudioName));
    }

    [Fact]
    public void ACorruptFileIsSkippedRatherThanFailingTheList()
    {
        using var directory = new HistoryDirectoryScope();
        TranscriptionHistory.Save(
            "/audio/a.wav", "m", "en", "on", 300, Result(),
            new DateTimeOffset(2026, 10, 20, 10, 0, 0, TimeSpan.Zero));
        File.WriteAllText(Path.Combine(directory.DirectoryPath, "broken.json"), "{ not json");

        HistoryEntry entry = Assert.Single(TranscriptionHistory.List());
        Assert.Equal("a.wav", entry.AudioName);
    }

    [Fact]
    public void DeleteRemovesTheFileAndTheEntry()
    {
        using var directory = new HistoryDirectoryScope();
        TranscriptionHistory.Save(
            "/audio/a.wav", "m", "en", "on", 300, Result(),
            new DateTimeOffset(2026, 10, 20, 10, 0, 0, TimeSpan.Zero));
        HistoryEntry entry = Assert.Single(TranscriptionHistory.List());

        TranscriptionHistory.Delete(entry);

        Assert.Empty(TranscriptionHistory.List());
        Assert.False(File.Exists(entry.FilePath));
    }

    [Fact]
    public void LoadingAnEntryWhoseFileIsGoneReturnsNull()
    {
        using var directory = new HistoryDirectoryScope();
        TranscriptionHistory.Save(
            "/audio/a.wav", "m", "en", "on", 300, Result(),
            new DateTimeOffset(2026, 10, 20, 10, 0, 0, TimeSpan.Zero));
        HistoryEntry entry = Assert.Single(TranscriptionHistory.List());
        File.Delete(entry.FilePath);

        Assert.Null(TranscriptionHistory.Load(entry));
    }

    [Fact]
    public void TheHistoryDirectoryIsNotSharedBetweenTwoScopes()
    {
        using var first = new HistoryDirectoryScope();
        using var second = new HistoryDirectoryScope();

        Assert.NotEqual(first.DirectoryPath, second.DirectoryPath);
        Assert.Equal(second.DirectoryPath, TranscriptionHistory.DirectoryPath);
    }
}
