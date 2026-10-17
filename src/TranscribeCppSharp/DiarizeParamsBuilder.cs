namespace TranscribeCppSharp;

using System;
using System.Runtime.InteropServices;
using TranscribeCppSharp.Interop;

/// <summary>
/// Fluent builder for diarization run parameters
/// (<c>transcribe_diarize_params</c>).
/// </summary>
public sealed class DiarizeParamsBuilder : IDisposable
{
    private readonly IntPtr handle;
    private DiarizeParams @params;
    private SortformerDiarizeExtBuilder? sortformerExt;
    private bool disposed;

    /// <inheritdoc/>
    public DiarizeParamsBuilder()
    {
        AbiValidation.ValidateSize<DiarizeParams>(AbiStruct.AbiDiarizeParams, nameof(DiarizeParams));
        handle = Marshal.AllocHGlobal(Marshal.SizeOf<DiarizeParams>());
        NativeMethods.DiarizeParamsInit(handle);
        @params = Marshal.PtrToStructure<DiarizeParams>(handle);
    }

    /// <summary>
    /// Sortformer family extension parameters (the latency/quality preset).
    /// Takes ownership of the builder — it is disposed when this builder is
    /// disposed or when another ext is set. Do not reuse it after passing it here.
    /// </summary>
    public DiarizeParamsBuilder WithSortformerExt(SortformerDiarizeExtBuilder ext)
    {
        ArgumentNullException.ThrowIfNull(ext);
        sortformerExt?.Dispose();
        sortformerExt = ext;
        @params.family = ext.Build();
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
            sortformerExt?.Dispose();
            sortformerExt = null;
            Marshal.FreeHGlobal(handle);
            disposed = true;
        }
    }
}
