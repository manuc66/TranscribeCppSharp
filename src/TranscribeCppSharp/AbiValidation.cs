#nullable enable

using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using TranscribeCppSharp.Interop;

namespace TranscribeCppSharp;

/// <summary>
/// Centralized ABI validation for the managed/native struct boundary.
/// Guards against the native library drifting from the pinned bindings:
/// total struct size is checked against the native-reported ABI size.
/// Field-offset integrity is verified separately (AbiLayout, generated from the
/// bindgen compile-time checks) — see AbiLayoutTest.
/// </summary>
internal static class AbiValidation
{
    /// <summary>
    /// The native library reports ABI sizes that cannot change within a loaded
    /// process, and <see cref="Marshal.SizeOf{T}()"/> is constant per type, so each
    /// (ABI token, type) pair is validated exactly once and then memoized.
    /// This keeps the guard on the hot path (every builder construction) to a
    /// cheap dictionary lookup after the first call.
    /// </summary>
    private static readonly ConcurrentDictionary<(AbiStruct Abi, Type Type), bool> SChecked = new();

    /// <summary>
    /// Throw if the managed <typeparamref name="T"/> size does not match the size
    /// reported by the native library for <paramref name="abi"/>.
    /// </summary>
    public static void ValidateSize<T>(AbiStruct abi, string typeName)
        where T : struct
    {
        var key = (abi, typeof(T));
        if (SChecked.ContainsKey(key))
        {
            return;
        }

        var nativeSize = NativeMethods.AbiStructSize(abi);
        var csSize = (nuint)Marshal.SizeOf<T>();
        if (csSize != nativeSize)
        {
            throw new InvalidOperationException(
                $"ABI struct size mismatch for {typeName}: C# expects {csSize} bytes, native reports {nativeSize} bytes. " +
                "Regenerate bindings or update the struct definition.");
        }

        SChecked.TryAdd(key, true);
    }
}
