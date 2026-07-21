#nullable enable

using System;
using System.Runtime.InteropServices;

namespace TranscribeCppSharp;

/// <summary>
/// Helper for safe stack allocation with size bounds checking.
/// </summary>
internal static class StackAllocHelper
{
    /// <summary>
    /// Maximum safe size for stack allocation (1 KB).
    /// Beyond this, heap allocation should be used to avoid stack overflow.
    /// </summary>
    public const int MaxStackSize = 1024;

    /// <summary>
    /// Check if the given size is safe for stack allocation.
    /// </summary>
    /// <param name="size">The size to check.</param>
    /// <returns>True if safe for stackalloc, false if heap allocation should be used.</returns>
    public static bool IsSafeForStack(int size)
    {
        return size <= MaxStackSize;
    }

    /// <summary>
    /// Throw if the size exceeds the safe stack limit.
    /// </summary>
    /// <param name="size">The size to check.</param>
    /// <param name="structName">Name of the struct for error message.</param>
    public static void ThrowIfTooLarge(int size, string structName)
    {
        if (size > MaxStackSize)
        {
            throw new InvalidOperationException(
                $"Native struct '{structName}' is too large ({size} bytes) for stack allocation. " +
                $"Maximum safe size is {MaxStackSize} bytes. " +
                $"This may indicate an ABI mismatch or a very large struct that should use heap allocation.");
        }
    }
}
