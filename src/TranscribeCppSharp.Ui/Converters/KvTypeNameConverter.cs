using System;
using System.Globalization;
using Avalonia.Data.Converters;
using TranscribeCppSharp.Interop;

namespace TranscribeCppSharp.Ui.Converters;

/// <summary>
/// Display names for the <see cref="KvType"/> picker: the enum members carry the
/// "KvType" prefix, which is noise in a combo box, so "Auto" reads better than
/// "KvTypeAuto".
/// </summary>
public sealed class KvTypeNameConverter : IValueConverter
{
    /// <inheritdoc/>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            KvType.KvTypeAuto => "Auto",
            KvType.KvTypeF32 => "F32",
            KvType.KvTypeF16 => "F16",
            _ => value?.ToString() ?? string.Empty,
        };

    /// <inheritdoc/>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("The KV type picker is chosen from its list.");
}
