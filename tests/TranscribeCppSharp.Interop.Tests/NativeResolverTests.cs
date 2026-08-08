#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using TranscribeCppSharp.Interop;
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
    [Fact]
    public void BuildNotFoundMessage_ContainsRid()
    {
        var rid = RuntimeInformation.RuntimeIdentifier;
        var message = NativeMethods.BuildNotFoundMessage(new List<string> { "/tmp/libtranscribe.so" });

        Assert.Contains(rid, message);
        Assert.Contains("dotnet add package TranscribeCppSharp.Native.", message);
        Assert.Contains("/tmp/libtranscribe.so", message);
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
        string sep = Path.DirectorySeparatorChar.ToString();
        var candidates = NativeMethods.EnumerateCandidates().ToList();

        Assert.Contains(candidates, c => c.Contains($"runtimes{sep}") && c.Contains($"{sep}native{sep}"));
    }

    [Fact]
    public void EnumerateCandidates_FirstCandidateIsAppBaseDirectory()
    {
        var candidates = NativeMethods.EnumerateCandidates().ToList();

        Assert.NotEmpty(candidates);
        Assert.Equal(Path.Combine(AppContext.BaseDirectory, "libtranscribe.so"), candidates[0]);
    }
}
