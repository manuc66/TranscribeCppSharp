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
        var abiSize = (int)NativeMethods.AbiStructSize(AbiStruct.AbiSessionParams);
        var csSize = Marshal.SizeOf<SessionParams>();
        if (csSize != abiSize)
            throw new InvalidOperationException(
                $"ABI struct size mismatch for SessionParams: C# expects {csSize} bytes, native reports {abiSize} bytes. " +
                $"Regenerate bindings or update the struct definition.");
        _handle = Marshal.AllocHGlobal(abiSize);
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
