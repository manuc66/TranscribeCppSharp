namespace TranscribeCppSharp;

using System;
using System.Runtime.InteropServices;
using TranscribeCppSharp.Interop;

/// <summary>
/// Fluent builder for a VAD session's parameters
/// (<c>transcribe_vad_session_params</c>).
/// </summary>
public sealed class VadSessionParamsBuilder : IDisposable
{
    private readonly IntPtr handle;
    private VadSessionParams @params;
    private bool disposed;

    /// <inheritdoc/>
    public VadSessionParamsBuilder()
    {
        AbiValidation.ValidateSize<VadSessionParams>(AbiStruct.AbiVadSessionParams, nameof(VadSessionParams));
        handle = Marshal.AllocHGlobal(Marshal.SizeOf<VadSessionParams>());
        NativeMethods.VadSessionParamsInit(handle);
        @params = Marshal.PtrToStructure<VadSessionParams>(handle);
    }

    /// <summary>Number of CPU threads. 0 = library default.</summary>
    public VadSessionParamsBuilder WithThreads(int nThreads)
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
