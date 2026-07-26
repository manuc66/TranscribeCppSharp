#nullable enable

using System;
using System.Runtime.InteropServices;
using TranscribeCppSharp.Interop;

namespace TranscribeCppSharp;

/// <summary>
/// Fluent builder for Voxtral realtime streaming extension parameters.
/// </summary>
public sealed class VoxtralExtBuilder : IDisposable
{
    private IntPtr _handle;
    private VoxtralRealtimeStreamExt _params;
    private bool _disposed;
    private bool _ownershipTransferred;

    internal void TransferOwnership() => _ownershipTransferred = true;

    public VoxtralExtBuilder()
    {
        var size = Marshal.SizeOf<VoxtralRealtimeStreamExt>();
        _handle = Marshal.AllocHGlobal(size);
        NativeMethods.VoxtralRealtimeStreamExtInit(_handle);
        _params = Marshal.PtrToStructure<VoxtralRealtimeStreamExt>(_handle);
    }

    /// <summary>Number of delay tokens.</summary>
    public VoxtralExtBuilder WithNumDelayTokens(int numDelayTokens)
    {
        _params.numDelayTokens = numDelayTokens;
        return this;
    }

    /// <summary>Minimum decode interval in milliseconds.</summary>
    public VoxtralExtBuilder WithMinDecodeIntervalMs(int ms)
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
