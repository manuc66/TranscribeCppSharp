// Backend/device policy for the CLI. Deliberately free of native calls so it
// can be unit tested on any machine (CI has no GPU); the native probing stays
// in Backends. The semantics are the ones documented in the upstream header
// (transcribe_model_load_params / transcribe_backend_request):
//
//   - device NULL + backend AUTO -> upstream policy: every discrete GPU is
//     probed before any integrated GPU, and CPU is the final fallback. This is
//     the default, so a GPU is used when one initializes, with no flag.
//   - device non-NULL + backend AUTO -> the selected device determines the
//     backend.
//   - device non-NULL + explicit backend -> the device must match the backend,
//     otherwise the load fails with TRANSCRIBE_ERR_INVALID_ARG. Exact
//     selection never falls back, so a forced device or backend is either
//     honoured or fails loudly — never silently retried elsewhere.

using System.Globalization;
using System.Text;
using TranscribeCppSharp;
using TranscribeCppSharp.Interop;

namespace TranscribeCppSharp.Cli;

/// <summary>Compute choice resolved from the CLI flags, ready for Model.Load.</summary>
/// <param name="Backend">Backend request to pass to the load params.</param>
/// <param name="Device">Exact device, or null to let the backend decide.</param>
/// <param name="Request">Short description of what was requested, for the console.</param>
internal sealed record DeviceChoice(BackendRequest Backend, BackendDevice? Device, string Request);

internal static class DeviceSelection
{
    /// <summary>Accepted --backend values, in --help order.</summary>
    internal const string BackendChoices = "auto, cpu, cpu-accel, metal, vulkan, cuda, rocm";

    /// <summary>
    /// Parses --backend. A null or empty value means auto, the documented
    /// upstream default.
    /// </summary>
    internal static bool TryParseBackend(string? name, out BackendRequest request, out string? error)
    {
        BackendRequest? parsed = string.IsNullOrWhiteSpace(name)
            ? BackendRequest.BackendAuto
            : name.Trim().ToLowerInvariant() switch
            {
                "auto" => BackendRequest.BackendAuto,
                "cpu" => BackendRequest.BackendCpu,
                // Both spellings: the documented one and the one people type.
                "cpu-accel" or "cpu_accel" => BackendRequest.BackendCpuAccel,
                "metal" => BackendRequest.BackendMetal,
                "vulkan" => BackendRequest.BackendVulkan,
                "cuda" => BackendRequest.BackendCuda,
                "rocm" => BackendRequest.BackendRocm,
                _ => null,
            };

        if (parsed is null)
        {
            request = BackendRequest.BackendAuto;
            error = $"unknown --backend '{name}' (expected one of: {BackendChoices})";
            return false;
        }

        request = parsed.Value;
        error = null;
        return true;
    }

    /// <summary>
    /// The device kind an explicit backend requires, or null for AUTO (where
    /// the backend follows the device). Host-memory accelerators report kind
    /// "accel" and, per the header, cannot be selected as a primary device.
    /// </summary>
    internal static string? RequiredKind(BackendRequest request) => request switch
    {
        BackendRequest.BackendCpu or BackendRequest.BackendCpuAccel => "cpu",
        BackendRequest.BackendMetal => "metal",
        BackendRequest.BackendVulkan => "vulkan",
        BackendRequest.BackendCuda => "cuda",
        BackendRequest.BackendRocm => "rocm",
        _ => null,
    };

    /// <summary>The CLI spelling of a backend, as accepted by --backend.</summary>
    internal static string Name(BackendRequest request) => request switch
    {
        BackendRequest.BackendAuto => "auto",
        BackendRequest.BackendCpu => "cpu",
        BackendRequest.BackendCpuAccel => "cpu-accel",
        BackendRequest.BackendMetal => "metal",
        BackendRequest.BackendVulkan => "vulkan",
        BackendRequest.BackendCuda => "cuda",
        BackendRequest.BackendRocm => "rocm",
        _ => request.ToString(),
    };

    /// <summary>
    /// Resolves --device N against the enumerated devices and checks it against
    /// a forced --backend. Whether a forced backend exists at all is a native
    /// question (Backends.BackendAvailable) and is deliberately not decided
    /// here.
    /// </summary>
    internal static DeviceChoice? Resolve(
        IReadOnlyList<BackendDevice> devices,
        BackendRequest request,
        int? deviceIndex,
        out string? error)
    {
        error = null;
        string requested = Name(request);

        if (deviceIndex is null)
        {
            return new DeviceChoice(request, null, requested);
        }

        if (deviceIndex < 0 || deviceIndex >= devices.Count)
        {
            error = $"no device with index {deviceIndex} ({devices.Count} available; run 'transcribe --list-devices')";
            return null;
        }

        BackendDevice device = devices[deviceIndex.Value];
        string? required = RequiredKind(request);
        if (required is not null && !string.Equals(device.Kind, required, StringComparison.OrdinalIgnoreCase))
        {
            error = $"device {deviceIndex} is a '{device.Kind}' device but --backend {requested} was requested: "
                + "an exact device must match an explicit backend. Use --backend auto to let the device pick its backend.";
            return null;
        }

        return new DeviceChoice(request, device, $"device {deviceIndex} ({Describe(device)})");
    }

    /// <summary>Device table for --list-devices and for error messages.</summary>
    internal static string FormatDevices(IReadOnlyList<BackendDevice> devices)
    {
        var table = new StringBuilder();
        table.AppendLine(CultureInfo.InvariantCulture, $"{"index",-6}{"kind",-9}{"type",-7}{"memory",10}  device");
        for (int i = 0; i < devices.Count; i++)
        {
            BackendDevice device = devices[i];
            table.AppendLine(CultureInfo.InvariantCulture, $"{i,-6}{device.Kind,-9}{TypeName(device.DeviceType),-7}{Memory(device.MemoryTotal),10}  {Describe(device)}");
        }

        return table.ToString().TrimEnd();
    }

    /// <summary>Short device label: the human description, else the raw name.</summary>
    internal static string Describe(BackendDevice device)
        => string.IsNullOrWhiteSpace(device.Description) ? device.Name : device.Description;

    private static string TypeName(DeviceType type) => type switch
    {
        DeviceType.DeviceTypeCpu => "CPU",
        DeviceType.DeviceTypeGpu => "GPU",
        DeviceType.DeviceTypeIgpu => "IGPU",
        DeviceType.DeviceTypeAccel => "ACCEL",
        _ => type.ToString(),
    };

    private static string Memory(ulong bytes)
    {
        if (bytes == 0)
        {
            return "-";
        }

        return bytes >= 1024UL * 1024 * 1024
            ? string.Create(CultureInfo.InvariantCulture, $"{bytes / (1024d * 1024 * 1024):0.0} GB")
            : string.Create(CultureInfo.InvariantCulture, $"{bytes / (1024d * 1024):0} MB");
    }
}
