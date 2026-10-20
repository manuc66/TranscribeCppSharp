using TranscribeCppSharp.Interop;
using TranscribeCppSharp.Models;
using TranscribeCppSharp.UI.Models;
using TranscribeCppSharp.UI.Services;
using TranscribeCppSharp.UI.ViewModels;
using Xunit;

namespace TranscribeCppSharp.UI.Tests;

/// <summary>
/// What the Transcription tab can say before a run: the weights do not fit, or a
/// previous run of the same configuration went lower than the free memory now.
/// </summary>
/// <remarks>
/// The numbers are seeded: a fake device and a stored observation. The point is
/// the rule and the wording, not this machine's hardware.
/// </remarks>
[Collection("UiGlobalState")]
public class MemoryWarningTests
{
    private const string Alias = "parakeet-tdt-0.6b-v3";
    private const string DeviceName = "Fake GPU";

    private static void WithViewModel(
        ulong freeBytes,
        Action<SettingsViewModel>? before = null,
        Action<TranscriptionViewModel>? test = null)
    {
        string? previousCache = ModelStore.CacheRootOverride;
        ModelStore.CacheRootOverride = Path.Combine(
            Path.GetTempPath(), "tcsharp-memory-" + Guid.NewGuid().ToString("N"));
        using var settingsFile = new UiSettingsScope();
        try
        {
            var settings = new SettingsViewModel();
            settings.Devices.Clear();
            settings.Devices.Add(new BackendDevice(
                DeviceName, DeviceName, "vulkan", "fake", 4_294_967_296, freeBytes,
                DeviceType.DeviceTypeGpu));
            settings.SelectedBackend = BackendRequest.BackendAuto;

            before?.Invoke(settings);

            var viewModel = new TranscriptionViewModel(
                new StubTranscriptionService(), settings, new ModelCapabilityCache());
            viewModel.ModelAlias = Alias;
            test!(viewModel);
        }
        finally
        {
            ModelStore.CacheRootOverride = previousCache;
        }
    }

    private static void SaveObservation(ulong minimumFreeBytes)
        => UiSettings.SaveMemoryObservation(
            MemoryPreflight.RunKey(Alias, 300, BackendRequest.BackendAuto, DeviceName),
            new MemoryObservation(
                DeviceName, 4_294_967_296, minimumFreeBytes,
                new DateTimeOffset(2026, 10, 21, 0, 8, 0, TimeSpan.Zero)));

    [Fact]
    public void APreviousLowPointWarnsWhenLessIsFreeNow()
    {
        WithViewModel(
            freeBytes: 1_000_000_000,
            before: _ => SaveObservation(2_000_000_000),
            test: viewModel =>
            {
                Assert.True(viewModel.HasMemoryWarning);
                Assert.Contains("previous run", viewModel.MemoryWarning, StringComparison.OrdinalIgnoreCase);
                Assert.Contains(DeviceName, viewModel.MemoryWarning);
            });
    }

    [Fact]
    public void NoWarningWhenThereIsMoreFreeThanThePreviousLowPoint()
    {
        WithViewModel(
            freeBytes: 3_000_000_000,
            before: _ => SaveObservation(2_000_000_000),
            test: viewModel => Assert.False(viewModel.HasMemoryWarning));
    }

    [Fact]
    public void NoWarningWithoutAMeasurement()
    {
        WithViewModel(
            freeBytes: 1_000_000_000,
            test: viewModel => Assert.False(viewModel.HasMemoryWarning));
    }

    [Fact]
    public void TheWeightsAloneAreNamedWhenTheyCannotFit()
    {
        // The catalog says this model's pinned file is ~524 MB; 100 MB free.
        WithViewModel(
            freeBytes: 100_000_000,
            test: viewModel =>
            {
                Assert.True(viewModel.HasMemoryWarning);
                Assert.Contains("weights alone", viewModel.MemoryWarning);
                Assert.Contains(DeviceName, viewModel.MemoryWarning);
            });
    }

    [Fact]
    public void ADeviceThatReportsNoFreeFigureIsNotAWarning()
    {
        WithViewModel(
            freeBytes: 0,
            before: _ => SaveObservation(2_000_000_000),
            test: viewModel => Assert.False(viewModel.HasMemoryWarning));
    }
}
