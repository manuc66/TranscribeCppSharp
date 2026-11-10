using Xunit;

namespace TranscribeCppSharp.Interop.Tests;

/// <summary>
/// Tests for the native library resolution fail-fast message.
/// When the native lib cannot be found, the resolver throws a
/// DllNotFoundException with an actionable message instead of the
/// cryptic default one.
/// </summary>
public class NativeResolverTests
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static readonly string[] PublishedRids =
        ["win-x64", "linux-x64", "linux-arm64", "osx-arm64", "osx-x64"];

    [Fact]
    public void BuildNotFoundMessage_ContainsRid()
    {
        var message = NativeMethods.BuildNotFoundMessage(new List<string> { "/tmp/libtranscribe.so" });

        Assert.Contains(NativeMethods.SupportedRid, message);
        Assert.Contains("dotnet add package TranscribeCppSharp.Native.", message);
        Assert.Contains("/tmp/libtranscribe.so", message);
        Assert.Contains("--version", message);
    }

    [Fact]
    public void SuggestedPackageRid_IsOneOfThePublishedRids()
    {
        // RuntimeInformation.RuntimeIdentifier is distro-specific on platforms
        // outside .NET's RID graph (Arch Linux reports "arch-x64"), so the
        // package named in the error must be mapped from OS + architecture.
        Assert.Contains(NativeMethods.SupportedRid, PublishedRids);
    }

    [Fact]
    public void BuildNotFoundMessage_NamesRejectedCandidates()
    {
        var rejected = new List<(string Path, string Reason)>
        {
            ("/home/user/.nuget/packages/transcribecppsharp.native.linux-x64/0.4.0/runtimes/linux-x64/native/libtranscribe.so",
                "reports transcribe.cpp 0.4.0"),
        };

        var message = NativeMethods.BuildNotFoundMessage(
            ["/tmp/libtranscribe.so", rejected[0].Path],
            rejected);

        Assert.Contains("Rejected candidates:", message);
        Assert.Contains(rejected[0].Path, message);
        Assert.Contains("reports transcribe.cpp 0.4.0", message);
        Assert.Contains("stale package", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExpectedNativeVersion_MatchesThePinnedUpstreamVersion()
    {
        // The version gate is only as good as this link: the value is injected
        // as assembly metadata by the Interop project from build/TRANSCRIBE_VERSION.
        string pinned = File.ReadAllText(
            Path.Combine(RepoRoot, "build", "TRANSCRIBE_VERSION")).Trim();

        Assert.False(string.IsNullOrEmpty(NativeMethods.ExpectedNativeVersion));
        Assert.Equal(pinned, NativeMethods.ExpectedNativeVersion);
    }

    [Fact]
    public void BuildNotFoundMessage_NoCandidates_IsStillActionable()
    {
        var message = NativeMethods.BuildNotFoundMessage(new List<string>());

        Assert.Contains("no candidates", message);
        Assert.Contains("dotnet add package TranscribeCppSharp.Native.", message);
    }

    [Fact]
    public void BuildNotFoundMessage_MentionsMuslAlpine()
    {
        var message = NativeMethods.BuildNotFoundMessage(new List<string> { "candidate" });

        Assert.Contains("musl", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Alpine", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Building from source", message);
    }

    [Fact]
    public void EnumerateCandidates_IncludesRuntimesRidNative()
    {
        // Regression: plain `dotnet run` (no <RuntimeIdentifier>) reports a portable
        // RID (e.g. arch-x64), but .NET places runtime-package binaries under
        // runtimes/<concrete-rid>/native/ — the resolver must search that layout.
        var baseDir = CreateTempBaseDirWithRuntimes();
        try
        {
            var candidates = NativeMethods.EnumerateCandidates(baseDir, packagesFolder: null).ToList();

            var fileName = GetNativeFileName();
            Assert.Contains(Path.Combine(baseDir, "runtimes", "linux-x64", "native", fileName), candidates);
        }
        finally
        {
            Directory.Delete(baseDir, recursive: true);
        }
    }

    [Fact]
    public void EnumerateCandidates_FirstCandidateIsAppBaseDirectory()
    {
        var candidates = NativeMethods.EnumerateCandidates("/tmp/fake-base", packagesFolder: null).ToList();

        Assert.NotEmpty(candidates);
        Assert.Equal(Path.Combine("/tmp/fake-base", GetNativeFileName()), candidates[0]);
    }

    private static string GetNativeFileName()
    {
        if (OperatingSystem.IsWindows())
        {
            return "transcribe.dll";
        }

        if (OperatingSystem.IsMacOS())
        {
            return "libtranscribe.dylib";
        }

        return "libtranscribe.so";
    }

    private static string CreateTempBaseDirWithRuntimes()
    {
        string baseDir = Path.Combine(Path.GetTempPath(), "transcribe-resolver-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(baseDir, "runtimes", "linux-x64", "native"));
        return baseDir;
    }
}
