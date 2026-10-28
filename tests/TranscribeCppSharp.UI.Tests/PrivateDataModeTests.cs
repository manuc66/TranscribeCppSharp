using TranscribeCppSharp.Audio;
using TranscribeCppSharp.UI.Models;
using TranscribeCppSharp.UI.Services;
using Xunit;

namespace TranscribeCppSharp.UI.Tests;

/// <summary>
/// The files the app keeps between runs — saved transcripts, settings, the log —
/// are owner-only on Unix.
/// </summary>
/// <remarks>
/// The default <see cref="File.WriteAllText(string, string)"/> mode is 0644 with
/// the stock umask, which on a shared host made every saved transcript readable
/// by every local account. These tests pin the mode, and the migration: a file or
/// directory an earlier version created 0644/0755 is corrected on the next write.
/// </remarks>
[Collection("UiGlobalState")]
public class PrivateDataModeTests
{
    private static UnixFileMode OwnerDirectory =>
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    private static UnixFileMode OwnerFile => UnixFileMode.UserRead | UnixFileMode.UserWrite;

    /// <summary>0644/0755, what a stock umask produces.</summary>
    private static UnixFileMode WorldReadable => (UnixFileMode)Convert.ToInt32("755", 8);

    private static UnixFileMode WorldReadableFile => (UnixFileMode)Convert.ToInt32("644", 8);

    private static string NewDirectory()
        => Path.Combine(Path.GetTempPath(), "tcsharp-mode-" + Guid.NewGuid().ToString("N"));

    private static void Delete(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (IOException)
        {
            // A test that leaves a handle open should not fail on cleanup.
        }
    }

    [Fact]
    public void EnsureDirectoryCreatesOwnerOnlyAndLeavesAnExistingOneAlone()
    {
        string directory = NewDirectory();
        string existing = Path.Combine(NewDirectory(), "shared");
        try
        {
            PrivateFile.EnsureDirectory(directory);
            Directory.CreateDirectory(existing);
            if (OperatingSystem.IsWindows())
            {
                Assert.True(Directory.Exists(directory));
                return;
            }

            Assert.Equal(OwnerDirectory, File.GetUnixFileMode(directory));

            // A directory that already exists may be a shared, pre-provisioned
            // cache; the class must not cut other accounts off from it. The files
            // it writes inside are 0600, which is what protects the transcript.
            File.SetUnixFileMode(existing, WorldReadable);
            PrivateFile.EnsureDirectory(existing);

            Assert.Equal(WorldReadable, File.GetUnixFileMode(existing));
        }
        finally
        {
            Delete(directory);
            Delete(Path.GetDirectoryName(existing)!);
        }
    }

    [Fact]
    public void WriteAllTextCreatesOwnerOnlyAndFixesAnExistingFile()
    {
        string directory = NewDirectory();
        string path = Path.Combine(directory, "note.json");
        try
        {
            PrivateFile.WriteAllText(path, "{}");
            Assert.Equal("{}", File.ReadAllText(path));
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));

            if (OperatingSystem.IsWindows())
            {
                return;
            }

            Assert.Equal(OwnerFile, File.GetUnixFileMode(path));

            // An install from before the fix: 0644 stays until the next write, and
            // the next write must not keep it.
            File.SetUnixFileMode(path, WorldReadableFile);

            PrivateFile.WriteAllText(path, "{\"a\":1}");

            Assert.Equal(OwnerFile, File.GetUnixFileMode(path));
            Assert.Equal("{\"a\":1}", File.ReadAllText(path));
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public void AppendAllTextCreatesOwnerOnlyAndFixesAnExistingFile()
    {
        string directory = NewDirectory();
        string path = Path.Combine(directory, "logs", "ui.log");
        try
        {
            PrivateFile.AppendAllText(path, "first\n");
            Assert.Equal("first\n", File.ReadAllText(path));

            if (OperatingSystem.IsWindows())
            {
                return;
            }

            Assert.Equal(OwnerFile, File.GetUnixFileMode(path));

            File.SetUnixFileMode(path, WorldReadableFile);

            PrivateFile.AppendAllText(path, "second\n");

            Assert.Equal(OwnerFile, File.GetUnixFileMode(path));
            Assert.Equal("first\nsecond\n", File.ReadAllText(path));
            Assert.Equal(OwnerDirectory, File.GetUnixFileMode(Path.GetDirectoryName(path)!));
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public void ASavedTranscriptIsOwnerOnly()
    {
        using var scope = new HistoryDirectoryScope();

        HistoryEntry entry = TranscriptionHistory.Save(
            "/audio/interview.wav",
            "whisper-tiny",
            "en",
            "on",
            300,
            new TranscriptionResult
            {
                FullText = "the transcript",
                DetectedLanguage = "en",
                Duration = TimeSpan.FromSeconds(1),
                Segments = [new MergedSegment(TimeSpan.Zero, TimeSpan.FromSeconds(1), "the transcript", 0)],
            },
            new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero));

        Assert.True(File.Exists(entry.FilePath));

        if (OperatingSystem.IsWindows())
        {
            return;
        }

        Assert.Equal(OwnerFile, File.GetUnixFileMode(entry.FilePath));
        Assert.Equal(OwnerDirectory, File.GetUnixFileMode(scope.DirectoryPath));
    }

    [Fact]
    public void TheFailureLogIsOwnerOnly()
    {
        string directory = NewDirectory();
        string? previous = UiLog.FilePathOverride;
        try
        {
            UiLog.FilePathOverride = Path.Combine(directory, "logs", "ui.log");

            UiLog.Failure("a test", new InvalidOperationException("boom"));

            Assert.Contains("boom", File.ReadAllText(UiLog.FilePathOverride));
            if (OperatingSystem.IsWindows())
            {
                return;
            }

            Assert.Equal(OwnerFile, File.GetUnixFileMode(UiLog.FilePathOverride));
            Assert.Equal(OwnerDirectory, File.GetUnixFileMode(Path.GetDirectoryName(UiLog.FilePathOverride)!));
        }
        finally
        {
            UiLog.FilePathOverride = previous;
            Delete(directory);
        }
    }

    [Fact]
    public void ASavedTranscriptFromAnOlderVersionIsTightenedOnList()
    {
        // The migration that matters: transcripts already on disk, written before
        // the fix, are 0644 until something corrects them. Listing corrects them.
        using var scope = new HistoryDirectoryScope();
        Directory.CreateDirectory(scope.DirectoryPath);
        string old = Path.Combine(scope.DirectoryPath, "old.json");
        File.WriteAllText(old, "{}");

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(scope.DirectoryPath, WorldReadable);
            File.SetUnixFileMode(old, WorldReadableFile);
        }

        _ = TranscriptionHistory.List();

        if (OperatingSystem.IsWindows())
        {
            return;
        }

        // The transcript is what matters, and it is now owner-only. The directory
        // already existed, so its mode is left as the install had it.
        Assert.Equal(OwnerFile, File.GetUnixFileMode(old));
    }

    [Fact]
    public void ASettingsFileFromAnOlderVersionIsTightenedOnRead()
    {
        using var scope = new UiSettingsScope();
        Directory.CreateDirectory(Path.GetDirectoryName(UiSettings.FilePath)!);
        File.WriteAllText(UiSettings.FilePath, "{}");

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(UiSettings.FilePath, WorldReadableFile);
        }

        _ = UiSettings.LoadAutoAdjustWindow();

        if (OperatingSystem.IsWindows())
        {
            return;
        }

        Assert.Equal(OwnerFile, File.GetUnixFileMode(UiSettings.FilePath));
    }

    [Fact]
    public void TheSettingsFileIsOwnerOnly()
    {
        using var scope = new UiSettingsScope();

        UiSettings.SaveAutoAdjustWindow(true);

        string path = UiSettings.FilePath;
        Assert.True(File.Exists(path));

        if (OperatingSystem.IsWindows())
        {
            return;
        }

        Assert.Equal(OwnerFile, File.GetUnixFileMode(path));
        Assert.Equal(OwnerDirectory, File.GetUnixFileMode(Path.GetDirectoryName(path)!));
    }
}
