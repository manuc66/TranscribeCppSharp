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
}
