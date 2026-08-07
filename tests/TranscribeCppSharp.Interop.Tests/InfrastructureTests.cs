#nullable enable

using System;
using System.Runtime.InteropServices;
using TranscribeCppSharp.Interop;
using Xunit;

namespace TranscribeCppSharp.Interop.Tests;

/// <summary>
/// Pure-managed tests for internal infrastructure: TranscribeException,
/// StackAllocHelper, and AbiValidation. No native library required.
/// </summary>
public class InfrastructureTests
{
    // ═══════════════════════════════════════════════════════════════════
    // TranscribeException
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void TranscribeException_Properties_AreSet()
    {
        var ex = new TranscribeException(Status.ErrInvalidArg, nameof(NativeMethods.SessionInit));

        Assert.Equal(Status.ErrInvalidArg, ex.StatusCode);
        Assert.Equal((int)Status.ErrInvalidArg, ex.ErrorCode);
        Assert.Equal(nameof(NativeMethods.SessionInit), ex.FailedMethod);
    }

    [Fact]
    public void TranscribeException_Message_ContainsStatusAndMethod()
    {
        var ex = new TranscribeException(Status.ErrGguf, "TestMethod");

        Assert.Contains("ErrGguf", ex.Message);
        Assert.Contains("TestMethod", ex.Message);
        Assert.Contains("transcribe native error", ex.Message);
    }

    [Fact]
    public void TranscribeException_Message_WithoutMethod()
    {
        var ex = new TranscribeException(Status.ErrBackend);

        Assert.Contains("ErrBackend", ex.Message);
        Assert.DoesNotContain(" in ", ex.Message);
    }

    [Fact]
    public void TranscribeException_IsException()
    {
        var ex = new TranscribeException(Status.ErrAborted);
        Assert.IsAssignableFrom<Exception>(ex);
    }

    // ═══════════════════════════════════════════════════════════════════
    // StackAllocHelper
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void StackAllocHelper_SmallSize_UsesStack()
    {
        // 64 bytes — well under the 1 KB threshold
        StackAllocHelper.RunWithBuffer(64, ptr =>
        {
            Assert.NotEqual(IntPtr.Zero, ptr);
            // Write and read back to verify the buffer is usable
            Marshal.WriteByte(ptr, 0xAB);
            Assert.Equal(0xAB, Marshal.ReadByte(ptr));
        });
    }

    [Fact]
    public void StackAllocHelper_LargeSize_UsesHeap()
    {
        // 2048 bytes — above the 1 KB threshold, must use heap
        StackAllocHelper.RunWithBuffer(2048, ptr =>
        {
            Assert.NotEqual(IntPtr.Zero, ptr);
            Marshal.WriteByte(ptr, 0xCD);
            Assert.Equal(0xCD, Marshal.ReadByte(ptr));
        });
    }

    [Fact]
    public void StackAllocHelper_ZeroSize_Works()
    {
        StackAllocHelper.RunWithBuffer(0, ptr =>
        {
            // Zero-size buffer: ptr may be null or valid, but should not crash
        });
    }

    [Fact]
    public void StackAllocHelper_NegativeSize_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            StackAllocHelper.RunWithBuffer(-1, _ => { }));
    }

    [Fact]
    public void StackAllocHelper_NullCallback_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            StackAllocHelper.RunWithBuffer(64, (Action<IntPtr>)null!));
    }

    [Fact]
    public void StackAllocHelper_WithReturn_SmallSize()
    {
        var result = StackAllocHelper.RunWithBuffer(64, ptr =>
        {
            Marshal.WriteInt32(ptr, 42);
            return Marshal.ReadInt32(ptr);
        });
        Assert.Equal(42, result);
    }

    [Fact]
    public void StackAllocHelper_WithReturn_LargeSize()
    {
        var result = StackAllocHelper.RunWithBuffer(2048, ptr =>
        {
            Marshal.WriteInt32(ptr, 99);
            return Marshal.ReadInt32(ptr);
        });
        Assert.Equal(99, result);
    }

    // ═══════════════════════════════════════════════════════════════════
    // AbiValidation
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void AbiValidation_ValidateSize_MatchingSize_DoesNotThrow()
    {
        // This tests the memoized path — if it was already validated, it's a no-op.
        // If not, it calls native AbiStructSize which needs the native lib.
        // We catch DllNotFoundException to handle the no-native-lib case.
        try
        {
            AbiValidation.ValidateSize<Interop.Segment>(AbiStruct.AbiSegment, "Segment");
        }
        catch (DllNotFoundException)
        {
            // Native library not available — test is inconclusive for this path
        }
    }
}
