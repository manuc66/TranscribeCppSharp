#nullable enable

using System;
using System.Runtime.InteropServices;
using TranscribeCppSharp.Interop;

namespace TranscribeCppSharp;

/// <summary>
/// Fluent builder for Moonshine streaming extension parameters.
/// </summary>
public sealed class MoonshineExtBuilder : IDisposable
{
    private IntPtr _handle;
    private MoonshineStreamingStreamExt _params;
    private bool _disposed;
    internal bool _ownershipTransferred;

    public MoonshineExtBuilder()
    {
        var size = Marshal.SizeOf<MoonshineStreamingStreamExt>();
        _handle = Marshal.AllocHGlobal(size);
        NativeMethods.MoonshineStreamingStreamExtInit(_handle);
        _params = Marshal.PtrToStructure<MoonshineStreamingStreamExt>(_handle);
    }

    /// <summary>Minimum decode interval in milliseconds.</summary>
    public MoonshineExtBuilder WithMinDecodeIntervalMs(int ms)
    {
        _params.minDecodeIntervalMs = ms;
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
