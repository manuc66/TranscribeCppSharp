namespace TranscribeCppSharp.UI.Models;

/// <summary>
/// One choice of the device picker: a device to pin, or null for automatic
/// selection.
/// </summary>
/// <remarks>
/// A record rather than binding the ComboBox straight to a nullable
/// <see cref="BackendDevice"/>. Null has no label to show as a selected item, so
/// once a device was picked the box offered no way back to Auto. This entry gives
/// Auto a row of its own that the reader can select again.
/// </remarks>
/// <param name="Device">The device to pin, or null to let the backend choose.</param>
public sealed record DeviceOption(BackendDevice? Device)
{
    /// <summary>The automatic choice: no device is pinned.</summary>
    public static DeviceOption Automatic { get; } = new((BackendDevice?)null);

    /// <summary>What the picker shows for this choice.</summary>
    public string Label => Device is null
        ? "Auto — let the backend choose"
        : string.IsNullOrWhiteSpace(Device.Kind)
            ? Device.Name
            : $"{Device.Name} ({Device.Kind})";
}
