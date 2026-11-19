using System.IO;
using TranscribeCppSharp.SharedSource;
using Xunit;

namespace TranscribeCppSharp.Interop.Tests;

/// <summary>
/// The guard the generator and the two build tools put around paths they derive
/// from their inputs.
/// </summary>
public class BuildPathTests
{
    [Fact]
    public void APathUnderTheRootIsReturnedAsAFullPath()
    {
        using var workspace = new TempWorkspace();

        string contained = BuildPath.Contain(workspace.Path, Path.Combine(workspace.Path, "ffi", "c", "transcribe.h"));

        Assert.Equal(Path.Combine(workspace.Path, "ffi", "c", "transcribe.h"), contained);
    }

    [Fact]
    public void TheRootItselfIsAccepted()
    {
        using var workspace = new TempWorkspace();

        Assert.Equal(workspace.Path, BuildPath.Contain(workspace.Path, workspace.Path));
    }

    [Fact]
    public void DotsInsideTheRootAreNormalized()
    {
        using var workspace = new TempWorkspace();

        string contained = BuildPath.Contain(workspace.Path, Path.Combine(workspace.Path, ".", "done", "..", "done"));

        Assert.Equal(Path.Combine(workspace.Path, "done"), contained);
    }

    [Theory]
    [InlineData("..")]
    [InlineData("../outside.txt")]
    [InlineData("sub/../../outside.txt")]
    public void APathThatClimbsOutIsRefused(string candidate)
    {
        using var workspace = new TempWorkspace();

        var ex = Assert.Throws<InvalidOperationException>(
            () => BuildPath.Contain(workspace.Path, Path.Combine(workspace.Path, candidate)));

        Assert.Contains("it is outside", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ASiblingWhoseNameStartsWithTheRootIsNotMistakenForIt()
    {
        using var workspace = new TempWorkspace();
        string sibling = workspace.Path + "-evil";

        Assert.Throws<InvalidOperationException>(
            () => BuildPath.Contain(workspace.Path, Path.Combine(sibling, "file.txt")));
    }

    [Fact]
    public void AnAbsolutePathOutsideTheRootIsRefused()
    {
        using var workspace = new TempWorkspace();

        Assert.Throws<InvalidOperationException>(
            () => BuildPath.Contain(workspace.Path, Path.Combine(Path.GetTempPath(), "elsewhere.txt")));
    }
}
