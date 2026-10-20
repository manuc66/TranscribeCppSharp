using TranscribeCppSharp.UI.Services;
using Xunit;

namespace TranscribeCppSharp.UI.Tests;

/// <summary>
/// The export options are remembered between launches, in the same file as the
/// last model. A missing or damaged section falls back to the defaults rather
/// than failing the tab.
/// </summary>
[Collection("UiGlobalState")]
public class ExportSettingsPersistenceTests
{
    [Fact]
    public void DefaultsApplyWhenNothingWasSaved()
    {
        using var settings = new UiSettingsScope();

        ExportSettings loaded = UiSettings.LoadExportSettings();

        Assert.False(loaded.MarkPauses);
        Assert.Equal(1, loaded.PauseThresholdSeconds);
        Assert.Equal(60, loaded.TimestampIntervalSeconds);
    }

    [Fact]
    public void TheThreeOptionsRoundTrip()
    {
        using var settings = new UiSettingsScope();

        UiSettings.SaveExportSettings(new ExportSettings(true, 3, 30));

        ExportSettings loaded = UiSettings.LoadExportSettings();
        Assert.True(loaded.MarkPauses);
        Assert.Equal(3, loaded.PauseThresholdSeconds);
        Assert.Equal(30, loaded.TimestampIntervalSeconds);
    }

    [Fact]
    public void OutOfRangeValuesFallBackToTheDefaults()
    {
        using var settings = new UiSettingsScope();
        Directory.CreateDirectory(Path.GetDirectoryName(UiSettings.FilePath)!);
        File.WriteAllText(
            UiSettings.FilePath,
            """{"export":{"markPauses":true,"pauseThresholdSeconds":0,"timestampIntervalSeconds":-5}}""");

        ExportSettings loaded = UiSettings.LoadExportSettings();

        // The flag is kept; the two numbers cannot mean anything at 0 and -5, so
        // they come back as the defaults rather than being passed to the export.
        Assert.True(loaded.MarkPauses);
        Assert.Equal(1, loaded.PauseThresholdSeconds);
        Assert.Equal(60, loaded.TimestampIntervalSeconds);
    }

    [Fact]
    public void SavingTheExportOptionsKeepsTheLastModels()
    {
        // The two features share one file; writing one must not drop the other.
        using var settings = new UiSettingsScope();
        UiSettings.SetLastModel(UiSettings.TranscriptionTab, "whisper-small");

        UiSettings.SaveExportSettings(new ExportSettings(true, 2, 45));

        Assert.Equal("whisper-small", UiSettings.LastModel(UiSettings.TranscriptionTab));
    }

    [Fact]
    public void SavingTheLastModelKeepsTheExportOptions()
    {
        using var settings = new UiSettingsScope();
        UiSettings.SaveExportSettings(new ExportSettings(true, 2, 45));

        UiSettings.SetLastModel(UiSettings.TranscriptionTab, "whisper-small");

        ExportSettings loaded = UiSettings.LoadExportSettings();
        Assert.True(loaded.MarkPauses);
        Assert.Equal(2, loaded.PauseThresholdSeconds);
        Assert.Equal(45, loaded.TimestampIntervalSeconds);
    }
}
