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
    private IntPtr handle;
    private SessionParams @params;
    private bool disposed;

    /// <inheritdoc/>
    public SessionParamsBuilder()
    {
        AbiValidation.ValidateSize<SessionParams>(AbiStruct.AbiSessionParams, nameof(SessionParams));
        handle = Marshal.AllocHGlobal(Marshal.SizeOf<SessionParams>());
        NativeMethods.SessionParamsInit(handle);
        @params = Marshal.PtrToStructure<SessionParams>(handle);
    }

    /// <summary>Number of CPU threads. 0 = library default.</summary>
    public SessionParamsBuilder WithThreads(int nThreads)
    {
        @params.nThreads = nThreads;
        return this;
    }

    /// <summary>KV cache data type for flash attention.</summary>
    public SessionParamsBuilder WithKvType(KvType kvType)
    {
        @params.kvType = kvType;
        return this;
    }

    /// <summary>Decoder context window cap (tokens). 0 = model max.</summary>
    public SessionParamsBuilder WithContextSize(int nCtx)
    {
        @params.nCtx = nCtx;
        return this;
    }

    internal IntPtr Build()
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        Marshal.StructureToPtr(@params, handle, false);
        return handle;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!disposed)
        {
            Marshal.FreeHGlobal(handle);
            disposed = true;
        }
    }
}
