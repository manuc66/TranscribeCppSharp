#nullable enable

using TranscribeCppSharp.Interop;

namespace TranscribeCppSharp;

/// <summary>
/// A compute backend device (CPU, GPU, iGPU).
/// </summary>
public record BackendDevice(
    string Name,
    string Description,
    string Kind,
    string DeviceId,
    ulong MemoryTotal,
    ulong MemoryFree,
    DeviceType DeviceType);
