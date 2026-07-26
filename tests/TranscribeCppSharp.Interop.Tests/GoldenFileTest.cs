using System.Reflection;
using System.Runtime.InteropServices;
using TranscribeCppSharp.Generator;
using Xunit;

namespace TranscribeCppSharp.Interop.Tests;

/// <summary>
/// Snapshot test: catches any silent drift in generated output.
/// When the generator changes intentionally, update the golden file with:
///   dotnet run --project src/Generator
/// </summary>
public class GoldenFileTest
{
    [Fact]
    public void GeneratedOutput_MatchesCommittedSnapshot()
    {
        var rustPath = Path.Combine(TestConfig.RepoRoot, "rust", "transcribe_sys.rs");
        var goldenPath = Path.Combine(TestConfig.RepoRoot, "generated", "TranscribeCppSharp.Interop", "NativeMethods.cs");

        var parser = RustFfiParser.FromFile(rustPath);
        var generated = new CSharpGenerator().Generate(parser);
        var committed = File.ReadAllText(goldenPath);

        Assert.Equal(committed, generated);
    }
}
