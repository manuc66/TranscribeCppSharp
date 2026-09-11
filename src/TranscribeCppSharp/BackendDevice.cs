#nullable enable

using System;
using TranscribeCppSharp.Interop;

namespace TranscribeCppSharp;

/// <summary>
/// A compute backend device (CPU, GPU, iGPU).
/// <see cref="Handle"/> is the runtime-owned opaque device handle, valid for
/// the life of the process (do not free). Pass it to
/// <see cref="ModelLoadParamsBuilder.WithDevice(BackendDevice)"/> for exact
/// selection, or persist <see cref="DeviceId"/> and re-enumerate later.
/// </summary>
public record BackendDevice(
    string Name,
    string Description,
    string Kind,
    string DeviceId,
    ulong MemoryTotal,
    ulong MemoryFree,
    DeviceType DeviceType)
{
    /// <summary>Opaque runtime-owned device handle (IntPtr.Zero = automatic selection).</summary>
    public IntPtr Handle { get; init; } = IntPtr.Zero;
}
