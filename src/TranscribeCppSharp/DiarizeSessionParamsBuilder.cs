namespace TranscribeCppSharp;

using System;
using System.Runtime.InteropServices;
using TranscribeCppSharp.Interop;

/// <summary>
/// Fluent builder for a diarization session's parameters
/// (<c>transcribe_diarize_session_params</c>).
/// </summary>
public sealed class DiarizeSessionParamsBuilder : IDisposable
{
    private readonly IntPtr handle;
    private DiarizeSessionParams @params;
    private bool disposed;

    /// <inheritdoc/>
    public DiarizeSessionParamsBuilder()
    {
        AbiValidation.ValidateSize<DiarizeSessionParams>(AbiStruct.AbiDiarizeSessionParams, nameof(DiarizeSessionParams));
        handle = Marshal.AllocHGlobal(Marshal.SizeOf<DiarizeSessionParams>());
        NativeMethods.DiarizeSessionParamsInit(handle);
        @params = Marshal.PtrToStructure<DiarizeSessionParams>(handle);
    }

    /// <summary>Number of CPU threads. 0 = library default.</summary>
    public DiarizeSessionParamsBuilder WithThreads(int nThreads)
    {
        @params.nThreads = nThreads;
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
