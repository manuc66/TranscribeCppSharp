using TranscribeCppSharp.UI.Models;
using TranscribeCppSharp.UI.Services;
using Xunit;

namespace TranscribeCppSharp.UI.Tests;

/// <summary>
/// The measured memory observations live in the same small file as the last models
/// and the export options, one entry per run configuration.
/// </summary>
[Collection("UiGlobalState")]
public class MemoryObservationPersistenceTests
{
    private static readonly DateTimeOffset MeasuredAt =
        new(2026, 10, 21, 0, 8, 0, TimeSpan.Zero);

    [Fact]
    public void AnObservationRoundTrips()
    {
        using var settings = new UiSettingsScope();
        string key = MemoryPreflight.RunKey("m", 300, Interop.BackendRequest.BackendAuto, "Quadro");

        UiSettings.SaveMemoryObservation(
            key, new MemoryObservation("Quadro", 4_294_967_296, 900_000_000, MeasuredAt));

        MemoryObservation? loaded = UiSettings.LoadMemoryObservation(key);
        Assert.NotNull(loaded);
        Assert.Equal("Quadro", loaded.DeviceName);
        Assert.Equal(4_294_967_296UL, loaded.TotalBytes);
        Assert.Equal(900_000_000UL, loaded.MinimumFreeBytes);
        Assert.Equal(MeasuredAt, loaded.MeasuredAt);
    }

    [Fact]
    public void AnUnknownOrEmptyKeyIsNoObservation()
    {
        using var settings = new UiSettingsScope();

        Assert.Null(UiSettings.LoadMemoryObservation("never-run"));
        Assert.Null(UiSettings.LoadMemoryObservation(string.Empty));
    }

    [Fact]
    public void ObservationsDoNotEraseTheLastModelsOrTheExportOptions()
    {
        using var settings = new UiSettingsScope();
        UiSettings.SetLastModel(UiSettings.TranscriptionTab, "whisper-small");
        UiSettings.SaveExportSettings(new ExportSettings(true, 2, 30));

        UiSettings.SaveMemoryObservation(
            "k", new MemoryObservation("d", 1, 2, MeasuredAt));

        Assert.Equal("whisper-small", UiSettings.LastModel(UiSettings.TranscriptionTab));
        ExportSettings export = UiSettings.LoadExportSettings();
        Assert.True(export.MarkPauses);
        Assert.Equal(2, export.PauseThresholdSeconds);
        Assert.Equal(30, export.TimestampIntervalSeconds);
    }

    [Fact]
    public void SavingTheOtherFeaturesKeepsTheObservation()
    {
        using var settings = new UiSettingsScope();
        UiSettings.SaveMemoryObservation(
            "k", new MemoryObservation("d", 1, 2, MeasuredAt));

        UiSettings.SetLastModel(UiSettings.TranscriptionTab, "whisper-small");
        UiSettings.SaveExportSettings(new ExportSettings(false, 1, 60));

        Assert.NotNull(UiSettings.LoadMemoryObservation("k"));
    }
}
