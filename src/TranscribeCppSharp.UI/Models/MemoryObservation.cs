namespace TranscribeCppSharp.UI.Models;

/// <summary>
/// What a run observed about its device's free memory: the lowest figure seen
/// while it ran, on which device, and when.
/// </summary>
/// <remarks>
/// A measurement, not a prediction. It says "this configuration once ran with
/// this much free memory to spare", which is what the next run is compared
/// against; it says nothing about a configuration that has never run here.
/// </remarks>
public sealed record MemoryObservation(
    string DeviceName,
    ulong TotalBytes,
    ulong MinimumFreeBytes,
    DateTimeOffset MeasuredAt);
