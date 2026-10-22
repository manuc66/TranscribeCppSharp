using System.Globalization;
using TranscribeCppSharp.Interop;

namespace TranscribeCppSharp.UI.Services;

/// <summary>
/// What the app can say about memory before a run, and what it cannot.
/// </summary>
/// <remarks>
/// The native library exposes each device's total and free memory, but not how
/// much a model will need: the weight file is knowable, the compute graph built
/// for one window is not. This helper therefore answers only what is certain —
/// which device a request resolves to, and whether the weights alone fit in its
/// free memory — and never guesses the rest. "Free" is a live snapshot as of the
/// call; zero means the device did not report it, not that no memory is free.
/// </remarks>
public static class MemoryPreflight
{
    /// <summary>
    /// The device a request resolves to, following the native policy: a pinned
    /// device wins; Auto probes discrete GPUs before integrated ones and falls back
    /// to the CPU; a forced backend picks the first device of its kind.
    /// </summary>
    public static BackendDevice? Target(
        IReadOnlyList<BackendDevice> devices,
        BackendRequest? backend,
        BackendDevice? pinned)
    {
        ArgumentNullException.ThrowIfNull(devices);

        if (pinned is not null)
        {
            return pinned;
        }

        BackendRequest request = backend ?? BackendRequest.BackendAuto;
        return request switch
        {
            BackendRequest.BackendAuto =>
                devices.FirstOrDefault(d => d.DeviceType == DeviceType.DeviceTypeGpu)
                ?? devices.FirstOrDefault(d => d.DeviceType == DeviceType.DeviceTypeIgpu)
                ?? devices.FirstOrDefault(d => d.DeviceType == DeviceType.DeviceTypeCpu),

            _ => devices.FirstOrDefault(d => Accepts(request, d)),
        };
    }

    /// <summary>
    /// Whether <paramref name="device"/> can be the primary device for
    /// <paramref name="backend"/>.
    /// </summary>
    /// <remarks>
    /// This mirrors the native rule: an explicit backend accepts only a device of
    /// its kind, CPU and CPU_ACCEL accept an exact CPU device, and an ACCEL device
    /// (BLAS/AMX) can never be the primary. With AUTO any primary device is valid;
    /// the selected device then decides the backend.
    /// </remarks>
    public static bool Accepts(BackendRequest backend, BackendDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        if (device.DeviceType == DeviceType.DeviceTypeAccel)
        {
            return false;
        }

        return backend switch
        {
            BackendRequest.BackendCpu or BackendRequest.BackendCpuAccel =>
                device.DeviceType == DeviceType.DeviceTypeCpu,
            BackendRequest.BackendMetal => KindMatches(device.Kind, BackendRequest.BackendMetal),
            BackendRequest.BackendVulkan => KindMatches(device.Kind, BackendRequest.BackendVulkan),
            BackendRequest.BackendCuda => KindMatches(device.Kind, BackendRequest.BackendCuda),
            BackendRequest.BackendRocm => KindMatches(device.Kind, BackendRequest.BackendRocm),
            _ => true,
        };
    }

    /// <summary>
    /// Whether the weight file alone fits in the target's free memory. True when
    /// the target or its free figure is unknown: refusing on a missing number would
    /// be a guess, and the run itself reports the real failure.
    /// </summary>
    public static bool WeightsFit(long modelBytes, BackendDevice? target)
        => target is null
        || target.MemoryFree == 0
        || modelBytes < 0
        || (ulong)modelBytes <= target.MemoryFree;

    /// <summary>A device and its live memory, in the terms the UI shows.</summary>
    public static string Describe(BackendDevice? device)
        => device is null
            ? "no device reported"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{device.Name} ({DeviceTypeLabel(device.DeviceType)}) — {FormatBytes(device.MemoryFree)} free of {FormatBytes(device.MemoryTotal)}");

    /// <summary>Bytes in the units the rest of the UI uses.</summary>
    public static string FormatBytes(ulong bytes)
    {
        const ulong gib = 1024UL * 1024 * 1024;
        const ulong mib = 1024UL * 1024;

        if (bytes >= gib)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{bytes / (double)gib:0.0} GB");
        }

        return bytes >= mib
            ? string.Create(CultureInfo.InvariantCulture, $"{bytes / (double)mib:0} MB")
            : string.Create(CultureInfo.InvariantCulture, $"{bytes} B");
    }

    /// <summary>
    /// The key a measured observation is stored under: the same model, window,
    /// backend and device is the configuration whose memory behaviour transfers.
    /// </summary>
    public static string RunKey(
        string modelAlias,
        int windowSeconds,
        BackendRequest backend,
        string deviceName)
        => string.Create(
            CultureInfo.InvariantCulture,
            $"{modelAlias}|{windowSeconds}|{backend}|{deviceName}");

    /// <summary>
    /// The key a window is remembered under: the model, backend and device decide
    /// what the window may be, so the value follows that combination rather than
    /// the tab or the process.
    /// </summary>
    /// <remarks>
    /// Deliberately not <see cref="RunKey"/>: that key includes the window itself,
    /// which is the value being looked up. The device name is the resolved device
    /// (or the pinned one when no free figure was reported), because the same
    /// model on two devices needs two different windows.
    /// </remarks>
    public static string CombinationKey(
        string modelAlias,
        BackendRequest backend,
        string deviceName)
        => string.Create(
            CultureInfo.InvariantCulture,
            $"{modelAlias}|{backend}|{deviceName}");

    /// <summary>The device type's short name, without the enum's "DeviceType" prefix.</summary>
    public static string DeviceTypeLabel(DeviceType type) => type switch
    {
        DeviceType.DeviceTypeCpu => "CPU",
        DeviceType.DeviceTypeGpu => "GPU",
        DeviceType.DeviceTypeIgpu => "iGPU",
        DeviceType.DeviceTypeAccel => "accelerator",
        _ => type.ToString(),
    };

    private static bool KindMatches(string kind, BackendRequest request) => request switch
    {
        BackendRequest.BackendMetal => string.Equals(kind, "metal", StringComparison.OrdinalIgnoreCase),
        BackendRequest.BackendVulkan => string.Equals(kind, "vulkan", StringComparison.OrdinalIgnoreCase),
        BackendRequest.BackendCuda => string.Equals(kind, "cuda", StringComparison.OrdinalIgnoreCase),
        BackendRequest.BackendRocm => string.Equals(kind, "rocm", StringComparison.OrdinalIgnoreCase),
        _ => false,
    };
}
