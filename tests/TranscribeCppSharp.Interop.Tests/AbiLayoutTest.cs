#nullable enable

using System;
using System.Reflection;
using System.Runtime.InteropServices;
using TranscribeCppSharp.Interop;
using Xunit;

namespace TranscribeCppSharp.Interop.Tests;

/// <summary>
/// Locks the managed struct declarations to the native repr(C) layout.
/// Ground truth comes from the compile-time checks bindgen emits in
/// transcribe_sys.rs (size_of!/align_of!/offset_of!).
///
/// This test is pure-managed: it runs even without the native library and
/// catches drift between the C# structs and the upstream C layout (wrong field
/// order, wrong type, missing field, bad Pack — anything that shifts offsets).
/// The bindings only ship 64-bit RIDs; the constants are therefore validated
/// against 64-bit layout rules.
/// </summary>
public class AbiLayoutTest
{
    private static readonly Assembly InteropAssembly = typeof(NativeMethods).Assembly;

    [Fact]
    public void AllStructs_MatchNativeBindgenLayout()
    {
        Assert.NotEmpty(AbiLayout.All);

        foreach (var (typeName, size, align, offsets) in AbiLayout.All)
        {
            var type = InteropAssembly.GetType($"TranscribeCppSharp.Interop.{typeName}");
            Assert.True(type != null, $"AbiLayout references unknown type '{typeName}'");
            Assert.True(type!.IsValueType && !type.IsEnum, $"{typeName} should be a struct");

            var actualSize = (ulong)Marshal.SizeOf(type);
            Assert.True(actualSize == size,
                $"{typeName}: managed size {actualSize} != native {size}. " +
                "Regenerate bindings or fix the struct declaration.");

            Assert.NotEmpty(offsets);
            foreach (var (field, offset) in offsets)
            {
                var actualOffset = (ulong)Marshal.OffsetOf(type, field);
                Assert.True(actualOffset == offset,
                    $"{typeName}.{field}: managed offset {actualOffset} != native {offset}. " +
                    "Regenerate bindings or fix the struct declaration.");
            }
        }
    }

    [Fact]
    public void EveryPublicStruct_HasAbiLayoutEntry()
    {
        // Guards against the parser silently missing a struct in transcribe_sys.rs:
        // iterating only over AbiLayout.All (above) would never catch a struct that
        // the parser failed to extract. This test inverts the direction.
        var publicStructs = InteropAssembly.GetExportedTypes()
            .Where(t => t.IsValueType && !t.IsEnum)
            .Select(t => t.Name)
            .ToList();

        Assert.NotEmpty(publicStructs);
        var layoutNames = AbiLayout.All.Select(x => x.TypeName).ToHashSet();
        var missing = publicStructs.Where(n => !layoutNames.Contains(n)).ToList();

        Assert.True(missing.Count == 0,
            "Structs without an AbiLayout entry (parser missed them in transcribe_sys.rs): " +
            string.Join(", ", missing));
    }

    [SkippableFact]
    public void NativeSizes_MatchManagedStructs()
    {
        // Requires the native library; skipped explicitly when unavailable.
        foreach (var (typeName, size, _, _) in AbiLayout.All)
        {
            if (!Enum.TryParse<AbiStruct>("Abi" + typeName, out var abi))
                continue; // ext structs / WhisperChunkTrace have no AbiStruct token

            nuint nativeSize;
            try
            {
                nativeSize = NativeMethods.AbiStructSize(abi);
            }
            catch (DllNotFoundException)
            {
                Skip.If(true, "Native library not available in this environment.");
                return;
            }

            Assert.True((ulong)nativeSize == size,
                $"{typeName}: native ABI size {nativeSize} != bindgen size {size}. " +
                "The shipped native library does not match the pinned bindings.");
        }
    }
}
