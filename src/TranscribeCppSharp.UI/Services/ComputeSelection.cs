using TranscribeCppSharp.Interop;

namespace TranscribeCppSharp.UI.Services;

/// <summary>
/// The Settings tab's compute choice, as it is remembered between launches.
/// </summary>
/// <param name="Backend">The backend to request.</param>
/// <param name="DeviceId">
/// The stable id of the device to pin, or null to let the backend choose. The
/// runtime handle is not persisted: it is valid only for the process that
/// enumerated it.
/// </param>
public readonly record struct ComputeSelection(BackendRequest Backend, string? DeviceId);
