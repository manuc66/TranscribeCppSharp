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
            Duration = TimeSpan.FromSeconds(11),
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
        Assert.Equal(TimeSpan.FromSeconds(11), entry.Duration);
        Assert.Equal("00:11", entry.DurationDisplay);
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

    [Fact]
    public void TwoRunsInTheSameMillisecondGetDistinctFiles()
    {
        using var directory = new HistoryDirectoryScope();
        var stamp = new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

        HistoryEntry first = TranscriptionHistory.Save(
            "/audio/one.wav", "m", "en", "on", 300, Result("one"), stamp);
        HistoryEntry second = TranscriptionHistory.Save(
            "/audio/two.wav", "m", "en", "on", 300, Result("two"), stamp);

        Assert.NotEqual(first.FilePath, second.FilePath);
        Assert.Equal(2, TranscriptionHistory.List().Count);
    }

    [Fact]
    public void SavingLeavesNoTemporaryFileBehind()
    {
        using var directory = new HistoryDirectoryScope();

        TranscriptionHistory.Save(
            "/audio/a.wav", "m", "en", "on", 300, Result(),
            new DateTimeOffset(2026, 10, 20, 10, 0, 0, TimeSpan.Zero));

        // The entry is written to a .tmp and renamed; the reader must see only
        // the finished file, even when the write succeeded.
        Assert.Empty(Directory.EnumerateFiles(directory.DirectoryPath, "*.tmp"));
        Assert.Single(Directory.EnumerateFiles(directory.DirectoryPath, "*.json"));
    }

    [Fact]
    public void AFileWithTheWrongShapeIsSkipped()
    {
        using var directory = new HistoryDirectoryScope();
        TranscriptionHistory.Save(
            "/audio/a.wav", "m", "en", "on", 300, Result(),
            new DateTimeOffset(2026, 10, 20, 10, 0, 0, TimeSpan.Zero));

        // Valid JSON whose values do not have the documented shape, and a document
        // with no result at all. Both must be left out, not passed to the tab.
        File.WriteAllText(
            Path.Combine(directory.DirectoryPath, "wrong-type.json"),
            """{"savedAt":"not-a-date"}""");
        File.WriteAllText(
            Path.Combine(directory.DirectoryPath, "no-result.json"),
            """{"savedAt":"2026-10-20T10:00:00+00:00","audioPath":"/b.wav","result":null}""");

        HistoryEntry entry = Assert.Single(TranscriptionHistory.List());
        Assert.Equal("a.wav", entry.AudioName);
    }

    [Fact]
    public void DeletingAnEntryWhoseFileIsAlreadyGoneIsNotAnError()
    {
        using var directory = new HistoryDirectoryScope();
        TranscriptionHistory.Save(
            "/audio/a.wav", "m", "en", "on", 300, Result(),
            new DateTimeOffset(2026, 10, 20, 10, 0, 0, TimeSpan.Zero));
        HistoryEntry entry = Assert.Single(TranscriptionHistory.List());
        File.Delete(entry.FilePath);

        TranscriptionHistory.Delete(entry);

        Assert.Empty(TranscriptionHistory.List());
    }

    [Fact]
    public void AHistoryDirectoryThatIsActuallyAFileIsAnEmptyHistory()
    {
        string path = Path.Combine(
            Path.GetTempPath(), "tcsharp-history-file-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(path, "not a directory");
        string? previous = TranscriptionHistory.DirectoryOverride;
        try
        {
            TranscriptionHistory.DirectoryOverride = path;
            Assert.Empty(TranscriptionHistory.List());
        }
        finally
        {
            TranscriptionHistory.DirectoryOverride = previous;
            File.Delete(path);
        }
    }
}
