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

    public ParakeetStreamExtBuilder()
    {
        var size = Marshal.SizeOf<ParakeetStreamExt>();
        _handle = Marshal.AllocHGlobal(size);
        NativeMethods.ParakeetStreamExtInit(_handle);
        _params = Marshal.PtrToStructure<ParakeetStreamExt>(_handle);
        if (_params.ext.size != (ulong)size)
            throw new InvalidOperationException(
                $"ABI struct size mismatch for ParakeetStreamExt: C# expects {size} bytes, native reports {_params.ext.size} bytes. " +
                $"Regenerate bindings or update the struct definition.");
    }

    /// <summary>Attention context right size.</summary>
    public ParakeetStreamExtBuilder WithAttContextRight(int contextRight)
    {
        _params.attContextRight = contextRight;
        return this;
    }

    internal IntPtr Build()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ParakeetStreamExtBuilder));
        Marshal.StructureToPtr(_params, _handle, false);
        return _handle;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            Marshal.FreeHGlobal(_handle);
            _disposed = true;
        }
    }
}
