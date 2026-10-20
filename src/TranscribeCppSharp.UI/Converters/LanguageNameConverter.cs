using System.Globalization;
using Avalonia.Data.Converters;
using TranscribeCppSharp.UI.Models;

namespace TranscribeCppSharp.UI.Converters;

/// <summary>
/// Shows a decoder language code with its English name, so the picker reads
/// "fr — French" instead of a bare "fr". The item itself stays the code, which
/// is what the run passes to the model.
/// </summary>
public sealed class LanguageNameConverter : IValueConverter
{
    /// <inheritdoc/>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string code ? LanguageNames.Describe(code) : value;

    /// <inheritdoc/>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("The picker chooses a code from its list; its label is not one.");
}
