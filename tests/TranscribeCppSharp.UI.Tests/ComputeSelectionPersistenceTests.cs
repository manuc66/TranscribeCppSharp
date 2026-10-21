using TranscribeCppSharp.Interop;
using TranscribeCppSharp.UI.Models;
using TranscribeCppSharp.UI.Services;
using TranscribeCppSharp.UI.ViewModels;
using Xunit;

namespace TranscribeCppSharp.UI.Tests;

/// <summary>
/// The Settings tab's backend and pinned device are remembered between launches.
/// </summary>
/// <remarks>
/// The device is stored by its stable id, not by the runtime handle, which is
/// valid only in the process that enumerated it. A saved device that is no longer
/// present falls back to Auto for that launch without erasing the id.
/// </remarks>
[Collection("UiGlobalState")]
public class ComputeSelectionPersistenceTests
{
    [Fact]
    public void NothingSavedMeansAutoWithNoDevice()
    {
        using var settings = new UiSettingsScope();

        ComputeSelection loaded = UiSettings.LoadComputeSelection();

        Assert.Equal(BackendRequest.BackendAuto, loaded.Backend);
        Assert.Null(loaded.DeviceId);
    }

    [Fact]
    public void TheBackendAndDeviceIdRoundTrip()
    {
        using var settings = new UiSettingsScope();

        UiSettings.SaveComputeSelection(BackendRequest.BackendVulkan, "gpu-0");

        ComputeSelection loaded = UiSettings.LoadComputeSelection();
        Assert.Equal(BackendRequest.BackendVulkan, loaded.Backend);
        Assert.Equal("gpu-0", loaded.DeviceId);
    }

    [Fact]
    public void AnEmptyDeviceIdMeansAuto()
    {
        using var settings = new UiSettingsScope();

        UiSettings.SaveComputeSelection(BackendRequest.BackendCpu, "   ");

        Assert.Null(UiSettings.LoadComputeSelection().DeviceId);
    }

    [Fact]
    public void AnUnknownBackendNameFallsBackToAuto()
    {
        using var settings = new UiSettingsScope();
        Directory.CreateDirectory(Path.GetDirectoryName(UiSettings.FilePath)!);
        File.WriteAllText(
            UiSettings.FilePath,
            """{ "compute": { "backend": "something-else", "deviceId": "gpu-0" } }""");

        ComputeSelection loaded = UiSettings.LoadComputeSelection();

        Assert.Equal(BackendRequest.BackendAuto, loaded.Backend);
        Assert.Equal("gpu-0", loaded.DeviceId);
    }

    [Fact]
    public void TheAppInstanceWritesAChange()
    {
        using var settings = new UiSettingsScope();
        var viewModel = new SettingsViewModel(rememberSelection: true);
        var device = new BackendDevice(
            "Fake GPU", "Fake GPU", "vulkan", "fake", 4_294_967_296, 2_000_000_000,
            DeviceType.DeviceTypeGpu);

        viewModel.Devices.Add(device);
        viewModel.SelectedBackend = BackendRequest.BackendCpu;
        viewModel.SelectedDeviceOption =
            viewModel.DeviceOptions.Single(option => option.Device == device);

        ComputeSelection saved = UiSettings.LoadComputeSelection();
        Assert.Equal(BackendRequest.BackendCpu, saved.Backend);
        Assert.Equal("fake", saved.DeviceId);
    }

    [Fact]
    public void AnUnrememberingInstanceWritesNothing()
    {
        using var settings = new UiSettingsScope();
        var viewModel = new SettingsViewModel();

        viewModel.SelectedBackend = BackendRequest.BackendCpu;

        Assert.False(File.Exists(UiSettings.FilePath));
    }

    [Fact]
    public void ASavedDeviceIsSelectedAgainWhenItIsStillPresent()
    {
        using var settings = new UiSettingsScope();

        // What this machine enumerates is the input; the test only checks that the
        // saved id finds the same device again.
        var probe = new SettingsViewModel();
        BackendDevice? device = probe.Devices.FirstOrDefault(
            candidate => !string.IsNullOrEmpty(candidate.DeviceId));
        if (device is null)
        {
            Assert.Skip("this machine reports no device with a stable id");
            return;
        }

        UiSettings.SaveComputeSelection(BackendRequest.BackendAuto, device.DeviceId);

        var restored = new SettingsViewModel(rememberSelection: true);

        Assert.NotNull(restored.SelectedDevice);
        Assert.Equal(device.DeviceId, restored.SelectedDevice!.DeviceId);
    }

    [Fact]
    public void AMissingDeviceLeavesAutoAndIsNotErased()
    {
        using var settings = new UiSettingsScope();
        UiSettings.SaveComputeSelection(BackendRequest.BackendVulkan, "not-on-this-machine");

        var restored = new SettingsViewModel(rememberSelection: true);

        Assert.Null(restored.SelectedDevice);
        Assert.Equal("not-on-this-machine", UiSettings.LoadComputeSelection().DeviceId);
    }
}
