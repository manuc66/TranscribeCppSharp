#nullable enable

using System.Globalization;
using TranscribeCppSharp.Models;
using Xunit;

namespace TranscribeCppSharp.Interop.Tests;

/// <summary>
/// Tests for the shared size formatter.
/// </summary>
/// <remarks>
/// This exists because the same number was rendered two ways: the CLI said
/// "1106 MB" where the model manager said "1.08 GB" for one 1.08 GiB model. The
/// arithmetic was right in both; having two answers for one number was the defect.
/// The formatter now lives in the wrapper, and these tests pin what it emits so a
/// second front end cannot quietly grow its own.
/// </remarks>
public class ModelSizeFormatTests
{
    [Theory]
    [InlineData(0, "0 MB")]
    [InlineData(-1, "0 MB")]
    [InlineData(1024, "1 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(35466912, "33.82 MB")]
    [InlineData(1160366080, "1.08 GB")]
    [InlineData(17138659808, "15.96 GB")]
    public void Format_RendersTheLargestUnitThatKeepsTheValueAtOrAboveOne(long bytes, string expected)
        => Assert.Equal(expected, ModelSizeFormat.Format(bytes).ToString());

    [Theory]
    [InlineData(35466912, "MB")]
    [InlineData(1024, "KB")]
    [InlineData(17138659808, "GB")]
    public void Format_ReportsTheUnitItUsed(long bytes, string expected)
        => Assert.Equal(expected, ModelSizeFormat.Format(bytes).Unit);

    [Fact]
    public void Format_RoundsToTwoDecimals()
    {
        // A third decimal of a gigabyte is noise on a download size, and the
        // manifest's byte counts are displayed, not compared.
        Assert.Equal("1 GB", ModelSizeFormat.Format(1024L * 1024 * 1024).ToString());
        Assert.Equal("1.23 GB", ModelSizeFormat.Format((long)(1.234 * 1024 * 1024 * 1024)).ToString());
    }

    [Fact]
    public void Format_UsesTheBinaryBaseAndSaysSo()
    {
        // The base is 1024, so "MB" here is what the binary prefix calls MiB. Both
        // former implementations already used 1024 and already said "MB"; the
        // labels were kept rather than corrected silently, and the difference is
        // recorded in the formatter's documentation instead.
        Assert.Equal("1 KB", ModelSizeFormat.Format(1024).ToString());
        Assert.Equal("1 MB", ModelSizeFormat.Format(1024 * 1024).ToString());
        Assert.Equal("1 GB", ModelSizeFormat.Format(1024L * 1024 * 1024).ToString());
    }

    [Fact]
    public void Format_DoesNotFollowTheAmbientCulture()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            // These strings sit in a terminal column and a grid column beside other
            // numbers. A French "1,08 GB" reads as a thousands separator.
            CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
            Assert.Equal("1.08 GB", ModelSizeFormat.Format(1160366080).ToString());
            Assert.Equal("    1.08", ModelSizeFormat.Format(1160366080).Number(8));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void Number_PadsSoAColumnStaysAlignedAcrossUnits()
    {
        // The unit is its own column in --list-models, so a 15.96 GB row and a
        // 33.8 MB row must line up. Padding the whole cell would not achieve that.
        Assert.Equal("   15.96", ModelSizeFormat.Format(17138659808).Number(8));
        Assert.Equal("   33.82", ModelSizeFormat.Format(35466912).Number(8));
        Assert.Equal("       1", ModelSizeFormat.Format(1024 * 1024).Number(8));
    }

    [Fact]
    public void TheSameNumberRendersIdenticallyForEveryCaller()
    {
        // The regression this whole type exists for.
        const long Bytes = 1160366080;
        string formatted = ModelSizeFormat.Format(Bytes).ToString();

        Assert.Equal("1.08 GB", formatted);
        Assert.DoesNotContain("1106", formatted, StringComparison.Ordinal);
    }
}