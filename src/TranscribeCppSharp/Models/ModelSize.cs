// One way to render a byte count, so the CLI and the GUI say the same thing
// about the same model.
//
// This existed twice: ModelStore printed `size / (1024 * 1024)` as "MB" in three
// places and never changed unit, while the model manager scaled up to GB. The
// same 1.08 GiB model was "1106 MB" in the terminal and "1.08 GB" in the window.
// Both were honest arithmetic on the same manifest number and neither was wrong,
// but a user comparing the two had no reason to expect two answers.

namespace TranscribeCppSharp.Models;

using System.Globalization;

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
