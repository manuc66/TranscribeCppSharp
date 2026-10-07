using System.Globalization;
using Avalonia.Data.Converters;

namespace TranscribeCppSharp.Ui.Converters;

/// <summary>
/// Builds a per-row automation id from a value bound on that row.
/// </summary>
/// <remarks>
/// A static AutomationId inside a DataTemplate is repeated on every row, so it
/// cannot address one: the model grid would offer 72 elements all called
/// "download". Pairing the action with the row's own value gives one id per
/// element — "models-download-whisper-tiny" — which is what a test, a UI
/// automation client or a screen reader needs.
/// <para>
/// Uniqueness is the caller's job: two equal bound values would collide. The
/// rows are model aliases, which the manifest keeps unique.
/// </para>
/// </remarks>
public sealed class RowIdConverter : IValueConverter
{
    /// <summary>Prefixes the bound value, e.g. "download" then "whisper-tiny".</summary>
    public string Prefix { get; set; } = string.Empty;

    /// <summary>Separator between the prefix and the bound value.</summary>
    public string Separator { get; set; } = "-";

    /// <inheritdoc/>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string name = value?.ToString() ?? string.Empty;
        return name.Length == 0
            ? Prefix
            : Prefix.Length == 0 ? name : string.Concat(Prefix, Separator, name);
    }

    /// <inheritdoc/>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("Automation ids are read-only.");
}
