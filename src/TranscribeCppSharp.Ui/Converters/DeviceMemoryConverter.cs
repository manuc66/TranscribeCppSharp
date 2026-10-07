using System.Globalization;
using Avalonia.Data.Converters;

namespace TranscribeCppSharp.Ui.Converters;

/// <summary>
/// Formats a device's byte count as the CLI's device table does: GiB with one
/// decimal above 1 GiB, whole MiB below, and a dash for an unknown (zero) size.
/// </summary>
/// <remarks>
/// <see cref="TranscribeCppSharp.BackendDevice.MemoryTotal"/> is in bytes. The
/// grid previously bound it with a "MB" format, which printed the raw byte
/// count under an MB label. The native field is the same one the CLI already
/// converts in <c>DeviceSelection.Memory(ulong bytes)</c>; this mirrors that
/// conversion, since the UI does not reference the CLI project.
/// </remarks>
public sealed class DeviceMemoryConverter : IValueConverter
{
    /// <inheritdoc/>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        ulong bytes = value switch
        {
            ulong u => u,
            long l when l >= 0 => (ulong)l,
            int i when i >= 0 => (ulong)i,
            _ => 0,
        };

        if (bytes == 0)
        {
            return "-";
        }

        return bytes >= 1024UL * 1024 * 1024
            ? string.Create(CultureInfo.InvariantCulture, $"{bytes / (1024d * 1024 * 1024):0.0} GB")
            : string.Create(CultureInfo.InvariantCulture, $"{bytes / (1024d * 1024):0} MB");
    }

    /// <inheritdoc/>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("Device memory is read-only.");
}
