using System.Reflection;
using TranscribeCppSharp.Generator;
using Xunit;

namespace TranscribeCppSharp.Interop.Tests;

/// <summary>
/// Verifies every Rust enum value maps to the correct C# enum int value.
/// Catches silent reordering, renaming, or value drift.
/// </summary>
public class EnumParityTest
{
    private static readonly Assembly InteropAssembly = typeof(NativeMethods).Assembly;

    [Fact]
    public void AllEnumValues_MatchRustValues()
    {
        var rustPath = Path.Combine(TestConfig.RepoRoot, "rust", "transcribe_sys.rs");
        var parser = RustFfiParser.FromFile(rustPath);

        foreach (var rustEnum in parser.ParseEnums())
        {
            var csTypeName = ToPascalCase(rustEnum.TypeName);
            var csType = InteropAssembly.GetType($"TranscribeCppSharp.Interop.{csTypeName}");

            Assert.NotNull(csType);
            Assert.True(csType.IsEnum, $"{csTypeName} should be an enum");

            foreach (var v in rustEnum.Values)
            {
                var csValueName = ToPascalCase(v.Name);
                var csValue = (int)Enum.Parse(csType, csValueName, ignoreCase: false);
                var rustValue = int.Parse(v.Value);

                Assert.Equal(rustValue, csValue);
            }
        }
    }

    [SkippableTheory]
    [InlineData(AbiStruct.AbiModelLoadParams)]
    [InlineData(AbiStruct.AbiSessionParams)]
    [InlineData(AbiStruct.AbiRunParams)]
    [InlineData(AbiStruct.AbiStreamParams)]
    [InlineData(AbiStruct.AbiCapabilities)]
    [InlineData(AbiStruct.AbiTimings)]
    [InlineData(AbiStruct.AbiSegment)]
    [InlineData(AbiStruct.AbiWord)]
    [InlineData(AbiStruct.AbiToken)]
    [InlineData(AbiStruct.AbiStreamUpdate)]
    [InlineData(AbiStruct.AbiStreamText)]
    [InlineData(AbiStruct.AbiSessionLimits)]
    [InlineData(AbiStruct.AbiExt)]
    [InlineData(AbiStruct.AbiBackendDevice)]
    public void AbiStructSize_IsNonZero(AbiStruct which)
    {
        nuint size;
        try
        {
            size = (nuint)NativeMethods.AbiStructSize(which);
            Assert.True(size > 0, $"ABI struct {which} reported size {size}");
        }
        catch (DllNotFoundException)
        {
            Skip.If(true, "Native library not available in test environment.");
        }
    }

    private static string ToPascalCase(string s)
    {
        s = System.Text.RegularExpressions.Regex.Replace(s, @"^transcribe_", "",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return string.Join("", s.Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => char.ToUpper(w[0]) + (w.Length > 1 ? w[1..].ToLower() : "")));
    }
}
