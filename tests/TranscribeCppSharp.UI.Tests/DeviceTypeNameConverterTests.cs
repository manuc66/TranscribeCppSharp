using System.Globalization;
using TranscribeCppSharp.Interop;
using TranscribeCppSharp.UI.Converters;
using Xunit;

namespace TranscribeCppSharp.UI.Tests;

/// <summary>
/// The devices grid shows "GPU", not "DeviceTypeGpu": the enum spelling is noise
/// in a table the reader is scanning for what the machine has.
/// </summary>
public class DeviceTypeNameConverterTests
{
    [Theory]
    [InlineData(DeviceType.DeviceTypeCpu, "CPU")]
    [InlineData(DeviceType.DeviceTypeGpu, "GPU")]
    [InlineData(DeviceType.DeviceTypeIgpu, "iGPU")]
    [InlineData(DeviceType.DeviceTypeAccel, "accelerator")]
    public void NamesTheTypeWithoutTheEnumPrefix(DeviceType type, string expected)
    {
        var converter = new DeviceTypeNameConverter();

        object? label = converter.Convert(type, typeof(string), null, CultureInfo.InvariantCulture);

        Assert.Equal(expected, label);
    }
}
