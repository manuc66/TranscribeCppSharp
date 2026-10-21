using System.Text.Json;
using TranscribeCppSharp.UI.Services;
using Xunit;

namespace TranscribeCppSharp.UI.Tests;

/// <summary>
/// The app remembers the folder the last picker was used in, and opens the next
/// picker there.
/// </summary>
/// <remarks>
/// The file format and the fallback rules are asserted here. The pickers
/// themselves are not driven: a headless test has no native file dialog, so what
/// can be checked is the note they write and read, not the dialog they open with
/// it.
/// </remarks>
[Collection("UiGlobalState")]
public class LastUsedDirectoryTests
{
    private static string MakeFolder()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "tcsharp-last-folder-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public void NothingWasRememberedYet()
    {
        using var settings = new UiSettingsScope();

        Assert.Null(UiSettings.LastDirectory());
    }

    [Fact]
    public void TheFolderRoundTrips()
    {
        using var settings = new UiSettingsScope();
        string folder = MakeFolder();
        try
        {
            UiSettings.SetLastDirectory(folder);

            Assert.Equal(folder, UiSettings.LastDirectory());
        }
        finally
        {
            Directory.Delete(folder);
        }
    }

    [Fact]
    public void TheFileNamesTheEntry()
    {
        using var settings = new UiSettingsScope();
        string folder = MakeFolder();
        try
        {
            UiSettings.SetLastDirectory(folder);

            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(UiSettings.FilePath));
            Assert.Equal(folder, document.RootElement.GetProperty("lastDirectory").GetString());
        }
        finally
        {
            Directory.Delete(folder);
        }
    }

    [Fact]
    public void AnEmptyPathIsNotRecorded()
    {
        using var settings = new UiSettingsScope();
        string folder = MakeFolder();
        try
        {
            UiSettings.SetLastDirectory(folder);
            UiSettings.SetLastDirectory(string.Empty);
            UiSettings.SetLastDirectory("   ");

            Assert.Equal(folder, UiSettings.LastDirectory());
        }
        finally
        {
            Directory.Delete(folder);
        }
    }

    [Fact]
    public void AFolderThatIsGoneIsNoAnswerForThisLaunch()
    {
        using var settings = new UiSettingsScope();
        string folder = MakeFolder();

        UiSettings.SetLastDirectory(folder);
        Directory.Delete(folder);

        Assert.Null(UiSettings.LastDirectory());

        // The path is still in the file, so a folder that comes back is used
        // again instead of being forgotten over one missing drive.
        Directory.CreateDirectory(folder);
        try
        {
            Assert.Equal(folder, UiSettings.LastDirectory());
        }
        finally
        {
            Directory.Delete(folder);
        }
    }

    [Fact]
    public void PickingAFileRemembersItsFolder()
    {
        using var settings = new UiSettingsScope();
        string folder = MakeFolder();
        string file = Path.Combine(folder, "talk.wav");
        try
        {
            File.WriteAllText(file, "placeholder");

            PickerFolder.Remember(file);

            Assert.Equal(folder, UiSettings.LastDirectory());
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void APathWithoutAFolderIsNotRecorded()
    {
        using var settings = new UiSettingsScope();

        PickerFolder.Remember(null);
        PickerFolder.Remember(string.Empty);
        PickerFolder.Remember("   ");

        Assert.Null(UiSettings.LastDirectory());
    }

    [Fact]
    public void TheFolderNoteSharesTheFileWithTheOtherSettings()
    {
        using var settings = new UiSettingsScope();
        string folder = MakeFolder();
        try
        {
            UiSettings.SetLastModel(UiSettings.TranscriptionTab, "whisper-small");
            UiSettings.SetLastDirectory(folder);
            UiSettings.SaveExportSettings(new ExportSettings(true, 2, 45));

            Assert.Equal("whisper-small", UiSettings.LastModel(UiSettings.TranscriptionTab));
            Assert.Equal(folder, UiSettings.LastDirectory());
            Assert.Equal(new ExportSettings(true, 2, 45), UiSettings.LoadExportSettings());
        }
        finally
        {
            Directory.Delete(folder);
        }
    }
}
