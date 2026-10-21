using TranscribeCppSharp.Interop;
using TranscribeCppSharp.UI.Models;
using TranscribeCppSharp.UI.ViewModels;
using Xunit;

namespace TranscribeCppSharp.UI.Tests;

/// <summary>
/// The Settings tab's compute panel is optional: Auto is the default and resolves
/// a backend by itself. The tab has to say that, because a list of seven backends
/// otherwise reads as a choice that must be made.
/// </summary>
public class SettingsPanelTests
{
    [Fact]
    public void AutoIsSelectedAndThePanelSaysItIsOptional()
    {
        var viewModel = new SettingsViewModel();

        Assert.Equal(BackendRequest.BackendAuto, viewModel.SelectedBackend);
        Assert.Null(viewModel.SelectedDevice);
        Assert.Contains("Auto", viewModel.BackendAvailabilityHint);
        Assert.Contains("default", viewModel.BackendAvailabilityHint, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("force", viewModel.BackendAvailabilityHint, StringComparison.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrWhiteSpace(viewModel.BackendTargetHint));
    }

    [Fact]
    public void TheBackendPickerShowsPlainNames()
    {
        var viewModel = new SettingsViewModel();

        Assert.Equal("Auto", viewModel.SelectedBackendOption.Label);
        Assert.All(viewModel.BackendOptions, option =>
            Assert.DoesNotContain("Backend", option.Label, StringComparison.Ordinal));
    }

    [Fact]
    public void AutoIsAnItemTheReaderCanPickAgain()
    {
        var viewModel = new SettingsViewModel();

        Assert.NotEmpty(viewModel.DeviceOptions);
        Assert.Same(DeviceOption.Automatic, viewModel.DeviceOptions[0]);
        Assert.Same(DeviceOption.Automatic, viewModel.SelectedDeviceOption);
        Assert.Null(viewModel.SelectedDevice);
    }

    [Fact]
    public void ADeviceCanBePinnedAndAutoChosenAgain()
    {
        var viewModel = new SettingsViewModel();
        var device = new BackendDevice(
            "Fake GPU", "Fake GPU", "vulkan", "fake", 4_294_967_296, 2_000_000_000,
            DeviceType.DeviceTypeGpu);

        viewModel.Devices.Add(device);

        DeviceOption pinned = viewModel.DeviceOptions.Single(option => option.Device == device);
        viewModel.SelectedDeviceOption = pinned;
        Assert.Same(device, viewModel.SelectedDevice);

        viewModel.SelectedDeviceOption = viewModel.DeviceOptions[0];
        Assert.Null(viewModel.SelectedDevice);
    }

    [Fact]
    public void ClearingTheDevicesLeavesOnlyAuto()
    {
        var viewModel = new SettingsViewModel();
        var device = new BackendDevice(
            "Fake GPU", "Fake GPU", "vulkan", "fake", 4_294_967_296, 2_000_000_000,
            DeviceType.DeviceTypeGpu);

        viewModel.Devices.Add(device);
        viewModel.SelectedDeviceOption =
            viewModel.DeviceOptions.Single(option => option.Device == device);

        viewModel.Devices.Clear();

        Assert.Single(viewModel.DeviceOptions);
        Assert.Same(DeviceOption.Automatic, viewModel.DeviceOptions[0]);
        Assert.Null(viewModel.SelectedDevice);
    }
}
