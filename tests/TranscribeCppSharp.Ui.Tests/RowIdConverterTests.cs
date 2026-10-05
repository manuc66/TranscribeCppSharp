using System.Globalization;
using TranscribeCppSharp.Ui.Converters;
using Xunit;

namespace TranscribeCppSharp.Ui.Tests;

public class RowIdConverterTests
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    [Fact]
    public void PrefixesAndSeparatesTheBoundValue()
    {
        var converter = new RowIdConverter { Prefix = "models-download" };

        object? id = converter.Convert("whisper-tiny", typeof(string), null, Invariant);

        Assert.Equal("models-download-whisper-tiny", id);
    }

    [Fact]
    public void ReturnsTheBarePrefixWhenTheBoundValueIsEmpty()
    {
        var converter = new RowIdConverter { Prefix = "models-download" };

        object? id = converter.Convert(string.Empty, typeof(string), null, Invariant);

        Assert.Equal("models-download", id);
    }

    [Fact]
    public void ReturnsTheBareValueWhenThereIsNoPrefix()
    {
        var converter = new RowIdConverter();

        object? id = converter.Convert("whisper-tiny", typeof(string), null, Invariant);

        Assert.Equal("whisper-tiny", id);
    }

    [Fact]
    public void ConvertsNullToAnEmptyResult()
    {
        var converter = new RowIdConverter();

        object? id = converter.Convert(null, typeof(string), null, Invariant);

        Assert.Equal(string.Empty, id);
    }

    [Fact]
    public void ConvertBackIsNotSupported()
    {
        var converter = new RowIdConverter();

        Assert.Throws<NotSupportedException>(
            () => converter.ConvertBack("models-download-x", typeof(string), null, Invariant));
    }
}
