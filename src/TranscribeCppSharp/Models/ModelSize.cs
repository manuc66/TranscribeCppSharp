// One way to render a byte count, so the CLI and the GUI say the same thing
// about the same model.
//
// This existed twice: ModelStore printed `size / (1024 * 1024)` as "MB" in three
// places and never changed unit, while the model manager scaled up to GB. The
// same 1.08 GiB model was "1106 MB" in the terminal and "1.08 GB" in the window.
// Both were honest arithmetic on the same manifest number and neither was wrong,
// but a user comparing the two had no reason to expect two answers.

using System.Globalization;

namespace TranscribeCppSharp.Models;

/// <summary>
/// A byte count split into a number and a unit, so a caller can align the number
/// in a column and print the unit beside it.
/// </summary>
/// <param name="Value">The magnitude, in <paramref name="Unit"/>.</param>
/// <param name="Unit">The unit, one of "KB", "MB" or "GB".</param>
public readonly record struct ModelSize(double Value, string Unit)
{
    /// <summary>
    /// Renders as a number and a unit, e.g. "1.08 GB".
    /// </summary>
    /// <remarks>
    /// Invariant culture, because this is compared against other sizes rather than
    /// read as prose: a French locale printing "1,08 GB" in a table column reads
    /// as a thousands separator at a glance.
    /// </remarks>
    /// <returns>The formatted size.</returns>
    public override string ToString()
        => string.Create(
            CultureInfo.InvariantCulture,
            $"{Value:0.##} {Unit}");

    /// <summary>The number alone, for a column that prints the unit separately.</summary>
    /// <param name="width">Field width, right aligned.</param>
    /// <returns>The formatted number.</returns>
    public string Number(int width) => Value.ToString("0.##", CultureInfo.InvariantCulture).PadLeft(width);
}

/// <summary>
/// Formats model sizes for display.
/// </summary>
public static class ModelSizeFormat
{
    private const long Kibi = 1024L;
    private const long Mebi = Kibi * 1024;
    private const long Gibi = Mebi * 1024;

    /// <summary>
    /// Splits a byte count into a magnitude and a unit.
    /// </summary>
    /// <param name="bytes">The size; 0 or less yields "0 MB".</param>
    /// <returns>The size, scaled to the largest unit that leaves it at or above 1.</returns>
    /// <remarks>
    /// The base is 1024, so "MB" here means what the binary prefix calls MiB.
    /// Both existing implementations already used 1024 and already said "MB",
    /// so the labels are kept rather than corrected in passing; the difference is
    /// recorded here instead of hidden.
    /// <para>
    /// Rounding is to two decimals. A model size is a download size, where the
    /// third decimal of a gigabyte is noise, and the manifest's own numbers are
    /// exact byte counts to display rather than values to compare.
    /// </para>
    /// </remarks>
    public static ModelSize Format(long bytes)
    {
        if (bytes <= 0)
        {
            return new ModelSize(0, "MB");
        }

        if (bytes < Mebi)
        {
            return new ModelSize(bytes / (double)Kibi, "KB");
        }

        if (bytes < Gibi)
        {
            return new ModelSize(bytes / (double)Mebi, "MB");
        }

        return new ModelSize(bytes / (double)Gibi, "GB");
    }
}
