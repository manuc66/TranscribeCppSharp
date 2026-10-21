using TranscribeCppSharp.Interop;
using TranscribeCppSharp.UI.Services;
using Xunit;

namespace TranscribeCppSharp.UI.Tests;

/// <summary>
/// What the app can say about memory before a run: which device a request resolves
/// to, whether the weights alone fit, and how the numbers are shown.
/// </summary>
public class MemoryPreflightTests
{
    private static BackendDevice Device(
        string name, DeviceType type, string kind, ulong total, ulong free)
        => new(name, name, kind, name, total, free, type);

    private static readonly BackendDevice Igpu =
        Device("Intel UHD 630", DeviceType.DeviceTypeIgpu, "vulkan", 12_000_000_000, 11_400_000_000);

    private static readonly BackendDevice DiscreteGpu =
        Device("Quadro P2000", DeviceType.DeviceTypeGpu, "vulkan", 4_294_967_296, 3_940_000_000);

    private static readonly BackendDevice Cpu =
        Device("Core i7", DeviceType.DeviceTypeCpu, "cpu", 16_000_000_000, 6_500_000_000);

    [Fact]
    public void AutoPrefersTheDiscreteGpuEvenWhenTheIgpuReportsMore()
    {
        BackendDevice? target = MemoryPreflight.Target([Igpu, DiscreteGpu, Cpu], BackendRequest.BackendAuto, null);

        Assert.Same(DiscreteGpu, target);
    }

    [Fact]
    public void AutoFallsBackToTheIgpuThenTheCpu()
    {
        Assert.Same(Igpu, MemoryPreflight.Target([Igpu, Cpu], BackendRequest.BackendAuto, null));
        Assert.Same(Cpu, MemoryPreflight.Target([Cpu], BackendRequest.BackendAuto, null));
        Assert.Null(MemoryPreflight.Target([], BackendRequest.BackendAuto, null));
    }

    [Fact]
    public void OnlyAMatchingDeviceCanBeThePrimary()
    {
        BackendDevice accel =
            Device("BLAS", DeviceType.DeviceTypeAccel, "accel", 0, 0);

        Assert.True(MemoryPreflight.Accepts(BackendRequest.BackendCpu, Cpu));
        Assert.False(MemoryPreflight.Accepts(BackendRequest.BackendCpu, DiscreteGpu));
        Assert.False(MemoryPreflight.Accepts(BackendRequest.BackendCpuAccel, accel));

        Assert.True(MemoryPreflight.Accepts(BackendRequest.BackendVulkan, DiscreteGpu));
        Assert.False(MemoryPreflight.Accepts(BackendRequest.BackendCuda, DiscreteGpu));

        Assert.True(MemoryPreflight.Accepts(BackendRequest.BackendAuto, Cpu));
        Assert.True(MemoryPreflight.Accepts(BackendRequest.BackendAuto, DiscreteGpu));
        Assert.False(MemoryPreflight.Accepts(BackendRequest.BackendAuto, accel));
    }

    [Fact]
    public void APinnedDeviceWinsOverThePolicy()
    {
        Assert.Same(
            Igpu,
            MemoryPreflight.Target([Igpu, DiscreteGpu, Cpu], BackendRequest.BackendAuto, Igpu));
    }

    [Fact]
    public void AForcedBackendPicksTheFirstDeviceOfItsKind()
    {
        // The native rule is "first matching device", which is enumeration order:
        // on this machine the Vulkan iGPU is listed before the discrete one.
        Assert.Same(Igpu, MemoryPreflight.Target([Igpu, DiscreteGpu, Cpu], BackendRequest.BackendVulkan, null));
        Assert.Same(Cpu, MemoryPreflight.Target([Igpu, Cpu], BackendRequest.BackendCpu, null));
        Assert.Null(MemoryPreflight.Target([Igpu], BackendRequest.BackendCuda, null));
    }

    [Fact]
    public void TheWeightsAreCheckedAgainstFreeMemoryButNothingElseIs()
    {
        Assert.True(MemoryPreflight.WeightsFit(523_000_000, DiscreteGpu));
        Assert.False(MemoryPreflight.WeightsFit(5_000_000_000, DiscreteGpu));

        // Unknown free is not a refusal: the run reports the real failure.
        BackendDevice unknown = Device("Unknown", DeviceType.DeviceTypeGpu, "cuda", 8_000_000_000, 0);
        Assert.True(MemoryPreflight.WeightsFit(99_000_000_000, unknown));
        Assert.True(MemoryPreflight.WeightsFit(1, null));
    }

    [Fact]
    public void BytesAreShownInTheUnitsTheSettingsTabUses()
    {
        Assert.Equal("4.0 GB", MemoryPreflight.FormatBytes(4_294_967_296));
        Assert.Equal("524 MB", MemoryPreflight.FormatBytes(548_946_272));
        Assert.Equal("512 B", MemoryPreflight.FormatBytes(512));
    }

    [Fact]
    public void ADescriptionNamesTheDeviceAndBothFigures()
    {
        string text = MemoryPreflight.Describe(DiscreteGpu);

        Assert.Contains("Quadro P2000", text);
        Assert.Contains("GPU", text);
        Assert.Contains("free of", text);
        Assert.Contains("4.0 GB", text);
    }

    [Fact]
    public void TheRunKeySeparatesEveryPartOfTheConfiguration()
    {
        string key = MemoryPreflight.RunKey("m", 300, BackendRequest.BackendVulkan, "Quadro");

        Assert.NotEqual(key, MemoryPreflight.RunKey("m", 60, BackendRequest.BackendVulkan, "Quadro"));
        Assert.NotEqual(key, MemoryPreflight.RunKey("m2", 300, BackendRequest.BackendVulkan, "Quadro"));
        Assert.NotEqual(key, MemoryPreflight.RunKey("m", 300, BackendRequest.BackendCpu, "Quadro"));
        Assert.NotEqual(key, MemoryPreflight.RunKey("m", 300, BackendRequest.BackendVulkan, "Intel"));
    }
}
