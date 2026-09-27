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

    /// <summary>
    /// Same as the <see cref="RunWithBuffer{T}(int, Func{IntPtr, T})"/>
    /// overload, with an extra argument passed to <paramref name="use"/>. This
    /// exists for callers that also have a
    /// <c>ReadOnlySpan&lt;float&gt;</c> to hand to the native call: such a span
    /// cannot be captured by a lambda or closed over by a local function
    /// (CS9108), so it has to travel as a parameter.
    /// </summary>
    // NOSONAR csharpsquid:S6640 — `unsafe` is the mechanism of this whole file,
    // not an incidental addition: stackalloc plus a pointer to hand to a P/Invoke
    // is what RunWithBuffer is for. There is no safe equivalent — turning a Span
    // into a native pointer needs `fixed`, and the obvious alternative
    // (Unsafe.AsPointer) also requires an unsafe context, which was checked
    // rather than assumed. The two overloads above this one are the same shape
    // and are already in the analysis baseline. What is safe here is the
    // discipline: the buffer is a local, its lifetime is the callback's, and
    // StackAllocHelper owns the stack-vs-heap decision so no caller can get it
    // wrong.
    internal static unsafe T RunWithBuffer<T, TArg>(int size, Func<IntPtr, TArg, T> use, TArg arg)
        where TArg : allows ref struct
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
                return use(ptr, arg);
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }

        Span<byte> buffer = stackalloc byte[size];
        fixed (byte* pBuffer = buffer)
        {
            return use((IntPtr)pBuffer, arg);
        }
    }
}
