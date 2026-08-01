#nullable enable

using System;
using System.Runtime.InteropServices;
using TranscribeCppSharp.Interop;

namespace TranscribeCppSharp;

/// <summary>
/// Fluent builder for session parameters.
/// </summary>
public sealed class SessionParamsBuilder : IDisposable
{
    private IntPtr _handle;
    private SessionParams _params;
    private bool _disposed;

    public SessionParamsBuilder()
    {
        AbiValidation.ValidateSize<SessionParams>(AbiStruct.AbiSessionParams, nameof(SessionParams));
        _handle = Marshal.AllocHGlobal(Marshal.SizeOf<SessionParams>());
        NativeMethods.SessionParamsInit(_handle);
        _params = Marshal.PtrToStructure<SessionParams>(_handle);
    }

    /// <summary>Number of CPU threads. 0 = library default.</summary>
    public SessionParamsBuilder WithThreads(int nThreads)
    {
        _params.nThreads = nThreads;
        return this;
    }

    /// <summary>KV cache data type for flash attention.</summary>
    public SessionParamsBuilder WithKvType(KvType kvType)
    {
        _params.kvType = kvType;
        return this;
    }

    /// <summary>Decoder context window cap (tokens). 0 = model max.</summary>
    public SessionParamsBuilder WithContextSize(int nCtx)
    {
        _params.nCtx = nCtx;
        return this;
    }

    internal IntPtr Build()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(SessionParamsBuilder));
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
