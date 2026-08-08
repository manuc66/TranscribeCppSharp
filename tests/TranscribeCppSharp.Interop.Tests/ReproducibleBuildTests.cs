#nullable enable

using System;
using System.IO;
using Xunit;

namespace TranscribeCppSharp.Interop.Tests;

/// <summary>
/// Guards the packaging invariant that CI uploads and pushes the .snupkg symbol
/// packages: the *.nupkg glob does not match *.snupkg, so both must be
/// enumerated explicitly in the pack/push steps. Native packages are
/// content-only and never produce symbols, so they are intentionally excluded.
/// </summary>
public class ReproducibleBuildTests
{
    private static readonly string RepoRoot = TestConfig.RepoRoot;

    private static readonly string WorkflowPath = Path.Combine(RepoRoot, ".github", "workflows", "ci.yml");

    [Fact]
    public void CiWorkflow_UploadsAndPushesSymbolPackages()
    {
        Skip.IfNot(File.Exists(WorkflowPath), $"CI workflow not found at {WorkflowPath}");

        var ci = File.ReadAllText(WorkflowPath);

        // Upload: the interop and wrapper pack jobs must include the .snupkg
        // files in their upload-artifact globs.
        Assert.Contains("nupkgs/*.snupkg", ci);

        // Push: both push steps (nuget.org and GitHub Packages) must enumerate
        // the .snupkg explicitly, since the "*.nupkg" glob does not match it.
        int nupkgPushes = CountOccurrences(ci, "dotnet nuget push");
        int snupkgPushes = CountOccurrences(ci, "nupkgs/*.snupkg");

        Assert.True(nupkgPushes >= 2, "Expected at least the two dotnet nuget push steps (nuget.org + GitHub Packages).");
        Assert.True(snupkgPushes >= 2, "Each dotnet nuget push must also push the .snupkg files.");
    }

    private static int CountOccurrences(string text, string value)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}
