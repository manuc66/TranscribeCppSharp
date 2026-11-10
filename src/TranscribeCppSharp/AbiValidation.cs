namespace TranscribeCppSharp;

using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using TranscribeCppSharp.Interop;

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
                $"ABI struct size mismatch for {typeName}: C# expects {csSize} bytes, native reports {nativeSize} bytes." +
                DescribeLoadedNativeLibrary() +
                " Regenerate bindings or update the struct definition.");
        }

        SChecked.TryAdd(key, true);
    }

    /// <summary>
    /// Names the native library that is actually loaded, so a size mismatch
    /// points at the side that is out of step. The resolver only accepts a
    /// library whose version matches the bindings, so a mismatch here means a
    /// development build with moved structs rather than a stale package — but
    /// the path and version still make that visible.
    /// </summary>
    internal static string DescribeLoadedNativeLibrary()
    {
        var path = NativeMethods.LoadedNativeLibraryPath;
        if (string.IsNullOrEmpty(path))
        {
            return string.Empty;
        }

        var version = NativeMethods.LoadedNativeVersion;
        return string.IsNullOrEmpty(version)
            ? $" Loaded native: {path}."
            : $" Loaded native: {path} (transcribe.cpp {version}).";
    }
}
