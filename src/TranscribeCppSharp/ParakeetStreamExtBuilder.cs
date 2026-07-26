#nullable enable

using System;
using System.Runtime.InteropServices;
using TranscribeCppSharp.Interop;

namespace TranscribeCppSharp;

/// <summary>
/// Fluent builder for Parakeet streaming extension parameters.
/// </summary>
public sealed class ParakeetStreamExtBuilder : IDisposable
{
    private IntPtr _handle;
    private ParakeetStreamExt _params;
    private bool _disposed;
    private bool _ownershipTransferred;

    internal void TransferOwnership() => _ownershipTransferred = true;

    public ParakeetStreamExtBuilder()
    {
        var size = Marshal.SizeOf<ParakeetStreamExt>();
        _handle = Marshal.AllocHGlobal(size);
        NativeMethods.ParakeetStreamExtInit(_handle);
        _params = Marshal.PtrToStructure<ParakeetStreamExt>(_handle);
    }

    /// <summary>Attention context right size.</summary>
    public ParakeetStreamExtBuilder WithAttContextRight(int contextRight)
    {
        _params.attContextRight = contextRight;
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
