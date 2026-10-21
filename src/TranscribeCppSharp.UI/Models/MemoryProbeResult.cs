namespace TranscribeCppSharp.UI.Models;

/// <summary>
/// The outcome of a memory probe: one window of the selected file, run with the
/// selected settings, measured.
/// </summary>
/// <param name="Succeeded">Whether the window completed.</param>
/// <param name="OutOfMemory">Whether it failed with the native <c>ErrOom</c>.</param>
/// <param name="Observation">The lowest free figure seen, when the device reports one.</param>
/// <param name="Message">The native message when it failed, otherwise null.</param>
public sealed record MemoryProbeResult(
    bool Succeeded,
    bool OutOfMemory,
    MemoryObservation? Observation,
    string? Message);
