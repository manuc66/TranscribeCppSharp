using System.Text.Json;
using TranscribeCppSharp.UI.Services;
using Xunit;

namespace TranscribeCppSharp.UI.Tests;

/// <summary>
/// The settings file is written through a sibling temporary file and a rename.
/// These tests cover what that buys: no partial file, a damaged file being
/// replaced rather than carried forward, and an unwritable location not becoming
/// an exception the pickers have to handle.
/// </summary>
[Collection("UiGlobalState")]
public class SettingsPersistenceRobustnessTests
{
    [Fact]
    public void SavingLeavesNoTemporaryFileBehind()
    {
        using var settings = new UiSettingsScope();

        UiSettings.SetLastModel(UiSettings.TranscriptionTab, "whisper-small");
        UiSettings.SaveExportSettings(new ExportSettings(true, 2, 45));

        string directory = Path.GetDirectoryName(UiSettings.FilePath)!;
        Assert.Empty(Directory.EnumerateFiles(directory, "*.tmp"));
        Assert.True(File.Exists(UiSettings.FilePath));
    }

    [Fact]
    public void ADamagedFileIsReplacedByAValidOne()
    {
        using var settings = new UiSettingsScope();
        Directory.CreateDirectory(Path.GetDirectoryName(UiSettings.FilePath)!);
        File.WriteAllText(UiSettings.FilePath, "{ this is not json");

        UiSettings.SetLastModel(UiSettings.TranscriptionTab, "whisper-small");

        // The previous content was unreadable, but the write must still produce a
        // file the next launch can parse instead of appending to the broken one.
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(UiSettings.FilePath));
        Assert.Equal(
            "whisper-small",
            document.RootElement.GetProperty("lastModels").GetProperty("transcription").GetString());
        Assert.Equal("whisper-small", UiSettings.LastModel(UiSettings.TranscriptionTab));
    }

    [Fact]
    public void AnUnwritableLocationDoesNotThrow()
    {
        string? previous = UiSettings.FilePathOverride;
        string blocker = Path.Combine(
            Path.GetTempPath(), "tcsharp-ui-settings-blocker-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(blocker, "not a directory");
        try
        {
            // The parent of the settings file is a regular file, so both the
            // directory creation and the rename fail. Remembering a preference is
            // best-effort: the caller must not see that failure.
            UiSettings.FilePathOverride = Path.Combine(blocker, "ui-settings.json");

            UiSettings.SetLastModel(UiSettings.TranscriptionTab, "whisper-small");
            UiSettings.SaveExportSettings(new ExportSettings(true, 2, 45));

            Assert.Null(UiSettings.LastModel(UiSettings.TranscriptionTab));
        }
        finally
        {
            UiSettings.FilePathOverride = previous;
            File.Delete(blocker);
        }
    }
}
