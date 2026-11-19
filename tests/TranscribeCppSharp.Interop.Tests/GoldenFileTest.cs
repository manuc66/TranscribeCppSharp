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
        var rustPath = Path.Combine(TestConfig.RepoRoot, "ffi", "rust", "transcribe_sys.rs");
        var headerDir = Path.Combine(TestConfig.RepoRoot, "ffi", "c");
        var goldenPath = Path.Combine(TestConfig.RepoRoot, "generated", "TranscribeCppSharp.Interop", "NativeMethods.cs");

        var parser = new RustFfiParser(File.ReadAllText(rustPath));
        var headerDoc = CHeaderDoc.FromContents(HeaderContents(headerDir));
        var generated = new CSharpGenerator().Generate(parser, headerDoc).Replace("\r\n", "\n");
        var committed = File.ReadAllText(goldenPath).Replace("\r\n", "\n");

        Assert.Equal(committed, generated);
    }

    /// <summary>
    /// The headers that make up the public surface, in the order the generator
    /// merges them, read from the checkout.
    /// </summary>
    /// <remarks>
    /// Kept in step with Generator/Program.cs HeaderFiles: the generated output
    /// depends on this order, and a drift between the two fails this test.
    /// </remarks>
    private static IEnumerable<string> HeaderContents(string directory)
    {
        var paths = new List<string>();
        string rootHeader = Path.Combine(directory, "transcribe.h");
        if (File.Exists(rootHeader))
        {
            paths.Add(rootHeader);
        }

        string subHeaderDir = Path.Combine(directory, "transcribe");
        if (Directory.Exists(subHeaderDir))
        {
            paths.AddRange(Directory.GetFiles(subHeaderDir, "*.h").OrderBy(p => p, StringComparer.Ordinal));
        }

        return paths.Select(File.ReadAllText);
    }
}
