#nullable enable

using System;
using System.Runtime.InteropServices;
using TranscribeCppSharp.Interop;

namespace TranscribeCppSharp;

/// <summary>
/// Fluent builder for Parakeet buffered streaming extension parameters.
/// </summary>
public sealed class ParakeetBufferedStreamExtBuilder : IDisposable
{
    private IntPtr _handle;
    private ParakeetBufferedStreamExt _params;
    private bool _disposed;
    internal bool _ownershipTransferred;

    public ParakeetBufferedStreamExtBuilder()
    {
        var size = Marshal.SizeOf<ParakeetBufferedStreamExt>();
        _handle = Marshal.AllocHGlobal(size);
        NativeMethods.ParakeetBufferedStreamExtInit(_handle);
        _params = Marshal.PtrToStructure<ParakeetBufferedStreamExt>(_handle);
    }

    /// <summary>Left context in milliseconds.</summary>
    public ParakeetBufferedStreamExtBuilder WithLeftMs(int leftMs)
    {
        _params.leftMs = leftMs;
        return this;
    }

    /// <summary>Chunk size in milliseconds.</summary>
    public ParakeetBufferedStreamExtBuilder WithChunkMs(int chunkMs)
    {
        _params.chunkMs = chunkMs;
        return this;
    }

    /// <summary>Right context in milliseconds.</summary>
    public ParakeetBufferedStreamExtBuilder WithRightMs(int rightMs)
    {
        _params.rightMs = rightMs;
        return this;
    }

    internal IntPtr Build()
    {
        Marshal.StructureToPtr(_params, _handle, false);
        return _handle;
    }

    public void Dispose()
    {
        if (!_disposed && !_ownershipTransferred)
        {
            Marshal.FreeHGlobal(_handle);
        }
        _disposed = true;
    }
}
