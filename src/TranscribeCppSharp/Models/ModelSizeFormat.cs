namespace TranscribeCppSharp.Models;

using System.Globalization;

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
