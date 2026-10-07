using TranscribeCppSharp.Cli;
using Xunit;

namespace TranscribeCppSharp.Interop.Tests;

/// <summary>
/// Tests for the CLI's backend/device policy. Pure policy, no native calls, so
/// the GPU selection rules are verified on any machine — including CI, which
/// has no GPU. The semantics asserted here come from the upstream header
/// (transcribe_model_load_params / transcribe_backend_request).
/// </summary>
public class DeviceSelectionTests
{
    [Theory]
    [InlineData(null, BackendRequest.BackendAuto)]
    [InlineData("", BackendRequest.BackendAuto)]
    [InlineData("auto", BackendRequest.BackendAuto)]
    [InlineData("cpu", BackendRequest.BackendCpu)]
    [InlineData("cpu-accel", BackendRequest.BackendCpuAccel)]
    [InlineData("metal", BackendRequest.BackendMetal)]
    [InlineData("vulkan", BackendRequest.BackendVulkan)]
    [InlineData("cuda", BackendRequest.BackendCuda)]
    [InlineData("rocm", BackendRequest.BackendRocm)]
    public void TryParseBackend_AcceptsDocumentedNames(string? name, BackendRequest expected)
    {
        Assert.True(DeviceSelection.TryParseBackend(name, out var request, out var error));
        Assert.Equal(expected, request);
        Assert.Null(error);
    }

    [Theory]
    [InlineData("CUDA")]
    [InlineData(" Vulkan ")]
    [InlineData("cpu_accel")]
    public void TryParseBackend_IsCaseAndWhitespaceInsensitive(string name)
    {
        Assert.True(DeviceSelection.TryParseBackend(name, out var request, out _));
        Assert.Equal(BackendRequestFor(name), request);
    }

    [Theory]
    [InlineData("gpu")]
    [InlineData("sycl")]
    [InlineData("cpus")]
    public void TryParseBackend_RejectsUnknownNames(string name)
    {
        Assert.False(DeviceSelection.TryParseBackend(name, out var request, out var error));
        Assert.Equal(BackendRequest.BackendAuto, request);
        Assert.Contains(name, error);
        Assert.Contains("auto, cpu, cpu-accel, metal, vulkan, cuda, rocm", error);
    }

    [Fact]
    public void Resolve_WithoutIndex_LeavesTheDeviceToTheBackend()
    {
        var choice = DeviceSelection.Resolve(Devices(), BackendRequest.BackendAuto, null, out var error);

        Assert.NotNull(choice);
        Assert.Null(error);
        Assert.Equal(BackendRequest.BackendAuto, choice.Backend);
        Assert.Null(choice.Device);
        Assert.Equal("auto", choice.Request);
    }

    [Fact]
    public void Resolve_WithIndex_SelectsThatExactDevice()
    {
        var choice = DeviceSelection.Resolve(Devices(), BackendRequest.BackendAuto, 0, out var error);

        Assert.NotNull(choice);
        Assert.Null(error);
        Assert.Equal(BackendRequest.BackendAuto, choice.Backend);
        Assert.NotNull(choice.Device);
        Assert.Equal("vulkan", choice.Device.Kind);
        Assert.Contains("device 0", choice.Request);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(99)]
    public void Resolve_RejectsAnIndexOutsideTheDeviceList(int index)
    {
        Assert.Null(DeviceSelection.Resolve(Devices(), BackendRequest.BackendAuto, index, out var error));
        Assert.Contains($"index {index}", error);
        Assert.Contains("--list-devices", error);
    }

    [Fact]
    public void Resolve_AcceptsAMatchingBackendAndDevice()
    {
        var choice = DeviceSelection.Resolve(Devices(), BackendRequest.BackendVulkan, 1, out var error);

        Assert.NotNull(choice);
        Assert.Null(error);
        Assert.Equal(BackendRequest.BackendVulkan, choice.Backend);
        Assert.Equal("Example dGPU", choice.Device!.Name);
    }

    [Fact]
    public void Resolve_RejectsADeviceThatDoesNotMatchAForcedBackend()
    {
        // An exact device with an explicit backend must match, and the upstream
        // never falls back: a silent CPU run here would misreport what ran.
        Assert.Null(DeviceSelection.Resolve(Devices(), BackendRequest.BackendCuda, 0, out var error));
        Assert.Contains("'vulkan' device", error);
        Assert.Contains("cuda", error);
        Assert.Contains("--backend auto", error);
    }

    [Fact]
    public void RequiredKind_MapsCpuAndAccelOntoTheCpuDevice()
    {
        Assert.Equal("cpu", DeviceSelection.RequiredKind(BackendRequest.BackendCpu));
        Assert.Equal("cpu", DeviceSelection.RequiredKind(BackendRequest.BackendCpuAccel));
        Assert.Null(DeviceSelection.RequiredKind(BackendRequest.BackendAuto));
    }

    [Fact]
    public void Name_UsesTheCliSpelling()
    {
        Assert.Equal("auto", DeviceSelection.Name(BackendRequest.BackendAuto));
        Assert.Equal("cpu-accel", DeviceSelection.Name(BackendRequest.BackendCpuAccel));
        Assert.Equal("vulkan", DeviceSelection.Name(BackendRequest.BackendVulkan));
    }

    [Fact]
    public void FormatDevices_ListsEveryDeviceWithItsIndex()
    {
        string table = DeviceSelection.FormatDevices(Devices());

        Assert.Contains("index", table);
        Assert.Contains("vulkan", table);
        Assert.Contains("GPU", table);
        Assert.Contains("CPU", table);
        Assert.Contains("Example discrete GPU", table);
        // 8_000_000_000 bytes rendered in GB, one decimal.
        Assert.Contains("7.5 GB", table);
        // Lines are one per device, plus the header.
        Assert.Equal(4, table.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public void FormatDevices_WithoutDevices_PrintsOnlyTheHeader()
    {
        string table = DeviceSelection.FormatDevices([]);

        Assert.Contains("index", table);
        Assert.DoesNotContain("vulkan", table);
    }

    [Fact]
    public void Describe_FallsBackToTheNameWhenThereIsNoDescription()
    {
        Assert.Equal("GPU-0", DeviceSelection.Describe(new BackendDevice("GPU-0", "", "vulkan", string.Empty, 0, 0, DeviceType.DeviceTypeGpu)));
        Assert.Equal("Example GPU", DeviceSelection.Describe(new BackendDevice("GPU-0", "Example GPU", "vulkan", string.Empty, 0, 0, DeviceType.DeviceTypeGpu)));
    }

    private static BackendRequest BackendRequestFor(string name) => name.Trim().ToLowerInvariant() switch
    {
        "cuda" => BackendRequest.BackendCuda,
        "vulkan" => BackendRequest.BackendVulkan,
        "cpu_accel" => BackendRequest.BackendCpuAccel,
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    // Deliberately generic device names and memory sizes: the policy is what
    // is under test, not any particular machine's hardware.
    private static IReadOnlyList<BackendDevice> Devices() =>
    [
        new(Name: "Example iGPU", Description: "Example integrated GPU", Kind: "vulkan", DeviceId: "0000:00:02.0", MemoryTotal: 12_000_000_000, MemoryFree: 11_000_000_000, DeviceType.DeviceTypeIgpu) { Handle = (IntPtr)1 },
        new(Name: "Example dGPU", Description: "Example discrete GPU", Kind: "vulkan", DeviceId: "0000:01:00.0", MemoryTotal: 8_000_000_000, MemoryFree: 7_000_000_000, DeviceType.DeviceTypeGpu) { Handle = (IntPtr)2 },
        new(Name: "Example CPU", Description: "Example CPU", Kind: "cpu", DeviceId: string.Empty, MemoryTotal: 32_000_000_000, MemoryFree: 16_000_000_000, DeviceType.DeviceTypeCpu) { Handle = (IntPtr)3 },
    ];
}
