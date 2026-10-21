using System.Globalization;
using Avalonia.Data.Converters;
using TranscribeCppSharp.Interop;
using TranscribeCppSharp.UI.Services;

namespace TranscribeCppSharp.UI.Converters;

/// <summary>
/// Shows the devices grid's type as "GPU", "iGPU", "CPU" or "accelerator" instead
/// of the enum members' "DeviceTypeGpu" spelling.
/// </summary>
public sealed class DeviceTypeNameConverter : IValueConverter
{
    /// <inheritdoc/>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is DeviceType type
            ? MemoryPreflight.DeviceTypeLabel(type)
            : value?.ToString() ?? string.Empty;

    /// <inheritdoc/>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("The device grid is read-only.");
}
