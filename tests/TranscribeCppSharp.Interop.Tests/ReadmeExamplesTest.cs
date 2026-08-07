#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace TranscribeCppSharp.Interop.Tests;

/// <summary>
/// Verifies that code blocks in README.md match sections marked in source files.
/// Source files use // @readme-begin &lt;name&gt; and // @end &lt;name&gt; markers.
/// If the source changes and the README is not updated, this test fails.
/// </summary>
public class ReadmeExamplesTest
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static readonly string ReadmePath = Path.Combine(RepoRoot, "README.md");

    private static readonly string[] SourceFiles = new[]
    {
        "tests/TranscribeCppSharp.Interop.Tests/HighLevelApiTests.cs",
        "samples/SmokeTest/Program.cs"
    };

    [Fact]
    public void Readme_ShouldExist()
    {
        Assert.True(File.Exists(ReadmePath), $"README.md not found at {ReadmePath}");
    }

    [Fact]
    public void ReadmeCodeBlocks_ShouldMatchMarkedSourceSections()
    {
        var sourceSections = ExtractAllMarkedSections();
        var readmeSections = ExtractReadmeSections();

        Assert.NotEmpty(sourceSections);
        Assert.NotEmpty(readmeSections);

        foreach (var (name, sourceContent) in sourceSections)
        {
            Assert.True(
                readmeSections.ContainsKey(name),
                $"README has no code block for marked section '{name}' from source.");

            var readmeContent = readmeSections[name];
            Assert.Equal(
                NormalizeWhitespace(sourceContent),
                NormalizeWhitespace(readmeContent));
        }
    }

    private static Dictionary<string, string> ExtractAllMarkedSections()
    {
        var result = new Dictionary<string, string>();

        foreach (var relativePath in SourceFiles)
        {
            var fullPath = Path.Combine(RepoRoot, relativePath);
            if (!File.Exists(fullPath)) continue;

            var lines = File.ReadAllLines(fullPath);
            for (int i = 0; i < lines.Length; i++)
            {
                var beginMatch = Regex.Match(lines[i], @"//\s*@readme-begin\s+(\S+)");
                if (!beginMatch.Success) continue;

                var name = beginMatch.Groups[1].Value;
                var sectionLines = new List<string>();

                for (int j = i + 1; j < lines.Length; j++)
                {
                    if (Regex.IsMatch(lines[j], $@"//\s*@end\s+{Regex.Escape(name)}"))
                        break;
                    sectionLines.Add(lines[j]);
                }

                result[name] = string.Join("\n", sectionLines);
            }
        }

        return result;
    }

    private static Dictionary<string, string> ExtractReadmeSections()
    {
        var content = File.ReadAllText(ReadmePath);
        var result = new Dictionary<string, string>();

        // Match: <!-- @readme <name> -->
        var markerPattern = new Regex(@"<!--\s*@readme\s+(\S+)\s*-->");
        var codeBlockPattern = new Regex(@"```csharp\r?\n(?<code>.*?)```", RegexOptions.Singleline);

        var markers = markerPattern.Matches(content);
        foreach (Match marker in markers)
        {
            var name = marker.Groups[1].Value;
            var markerIndex = marker.Index;

            // Find the code block that follows this marker
            foreach (Match cb in codeBlockPattern.Matches(content))
            {
                if (cb.Index > markerIndex)
                {
                    result[name] = cb.Groups["code"].Value.TrimEnd('\r', '\n');
                    break;
                }
            }
        }

        return result;
    }

    private static string NormalizeWhitespace(string text)
    {
        var lines = text.Split('\n');
        var normalized = new List<string>();
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed))
                normalized.Add(trimmed);
        }
        return string.Join("\n", normalized);
    }
}
