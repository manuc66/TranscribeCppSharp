using TranscribeCppSharp;
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
    public void OnlyBackendsThisBuildCanSatisfyAreOffered()
    {
        var viewModel = new SettingsViewModel();

        Assert.All(
            viewModel.BackendOptions.Where(option => option.Value != BackendRequest.BackendAuto),
            option => Assert.True(Backends.BackendAvailable(option.Value)));

        bool hasHostAccelerator = viewModel.Devices.Any(
            device => device.DeviceType == DeviceType.DeviceTypeAccel);
        Assert.Equal(
            hasHostAccelerator && Backends.BackendAvailable(BackendRequest.BackendCpuAccel),
            viewModel.BackendOptions.Any(option => option.Value == BackendRequest.BackendCpuAccel));
    }

    [Fact]
    public void TheDevicePickerOffersOnlyWhatTheBackendAccepts()
    {
        var viewModel = new SettingsViewModel();
        viewModel.Devices.Clear();

        BackendDevice cpu = Device("Fake CPU", "cpu", DeviceType.DeviceTypeCpu);
        BackendDevice vulkan = Device("Fake GPU", "vulkan", DeviceType.DeviceTypeGpu);
        BackendDevice accel = Device("Fake BLAS", "accel", DeviceType.DeviceTypeAccel);
        viewModel.Devices.Add(cpu);
        viewModel.Devices.Add(vulkan);
        viewModel.Devices.Add(accel);

        viewModel.SelectedBackend = BackendRequest.BackendCpu;
        Assert.Single(viewModel.DeviceOptions.Skip(1));
        Assert.Same(cpu, viewModel.DeviceOptions[1].Device);

        viewModel.SelectedBackend = BackendRequest.BackendVulkan;
        Assert.Single(viewModel.DeviceOptions.Skip(1));
        Assert.Same(vulkan, viewModel.DeviceOptions[1].Device);

        viewModel.SelectedBackend = BackendRequest.BackendAuto;
        Assert.Equal(2, viewModel.DeviceOptions.Count - 1);
        Assert.Contains(viewModel.DeviceOptions, option => option.Device == cpu);
        Assert.Contains(viewModel.DeviceOptions, option => option.Device == vulkan);
        Assert.DoesNotContain(viewModel.DeviceOptions, option => option.Device == accel);
    }

    [Fact]
    public void APinnedDeviceThatTheNewBackendRejectsIsCleared()
    {
        var viewModel = new SettingsViewModel();
        viewModel.Devices.Clear();
        BackendDevice cpu = Device("Fake CPU", "cpu", DeviceType.DeviceTypeCpu);
        BackendDevice vulkan = Device("Fake GPU", "vulkan", DeviceType.DeviceTypeGpu);
        viewModel.Devices.Add(cpu);
        viewModel.Devices.Add(vulkan);

        viewModel.SelectedBackend = BackendRequest.BackendAuto;
        viewModel.SelectedDeviceOption =
            viewModel.DeviceOptions.Single(option => option.Device == vulkan);
        Assert.Same(vulkan, viewModel.SelectedDevice);

        viewModel.SelectedBackend = BackendRequest.BackendCpu;
        Assert.Null(viewModel.SelectedDevice);
    }

    private static BackendDevice Device(string name, string kind, DeviceType type)
        => new(name, name, kind, name, 0, 0, type);

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
