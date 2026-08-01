#nullable enable

using System;
using System.Runtime.InteropServices;

namespace TranscribeCppSharp;

/// <summary>
/// Helper for safe native buffer allocation: uses the stack for small structs
/// (fast path) and falls back to unmanaged heap memory above a size threshold,
/// so oversized structs never crash the process with a stack overflow.
/// </summary>
internal static class StackAllocHelper
{
    /// <summary>
    /// Maximum safe size for stack allocation (1 KB). Beyond this, heap
    /// allocation is used to avoid stack overflow.
    /// </summary>
    internal const int MaxStackSize = 1024;

    /// <summary>
    /// Provide a native buffer of <paramref name="size"/> bytes (stack-allocated
    /// when small, heap-allocated otherwise) and invoke <paramref name="use"/>
    /// with a pointer to it. The buffer is valid only for the duration of the
    /// callback. The heap variant is freed automatically.
    /// </summary>
    internal static unsafe void RunWithBuffer(int size, Action<IntPtr> use)
    {
        ArgumentNullException.ThrowIfNull(use);
        if (size < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(size), size, "Buffer size must be non-negative.");
        }

        if (size > MaxStackSize)
        {
            var ptr = Marshal.AllocHGlobal(size);
            try
            {
                use(ptr);
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }

            return;
        }

        Span<byte> buffer = stackalloc byte[size];
        fixed (byte* pBuffer = buffer)
        {
            use((IntPtr)pBuffer);
        }
    }

    /// <summary>
    /// Same as <see cref="RunWithBuffer(int, Action{IntPtr})"/> but returns
    /// the value produced by <paramref name="use"/>. The buffer is valid only for
    /// the duration of the callback.
    /// </summary>
    internal static unsafe T RunWithBuffer<T>(int size, Func<IntPtr, T> use)
    {
        ArgumentNullException.ThrowIfNull(use);
        if (size < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(size), size, "Buffer size must be non-negative.");
        }

        if (size > MaxStackSize)
        {
            var ptr = Marshal.AllocHGlobal(size);
            try
            {
                return use(ptr);
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }

        Span<byte> buffer = stackalloc byte[size];
        fixed (byte* pBuffer = buffer)
        {
            return use((IntPtr)pBuffer);
        }
    }
}
