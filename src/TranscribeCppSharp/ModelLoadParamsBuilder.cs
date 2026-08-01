#nullable enable

using System;
using System.Runtime.InteropServices;
using TranscribeCppSharp.Interop;

namespace TranscribeCppSharp;

/// <summary>
/// Fluent builder for model load parameters.
/// </summary>
public sealed class ModelLoadParamsBuilder : IDisposable
{
    private IntPtr handle;
    private ModelLoadParams @params;
    private bool disposed;

    /// <inheritdoc/>
    public ModelLoadParamsBuilder()
    {
        AbiValidation.ValidateSize<ModelLoadParams>(AbiStruct.AbiModelLoadParams, nameof(ModelLoadParams));
        handle = Marshal.AllocHGlobal(Marshal.SizeOf<ModelLoadParams>());
        NativeMethods.ModelLoadParamsInit(handle);
        @params = Marshal.PtrToStructure<ModelLoadParams>(handle);
    }

    /// <summary>Select the compute backend.</summary>
    public ModelLoadParamsBuilder WithBackend(BackendRequest backend)
    {
        @params.backend = backend;
        return this;
    }

    /// <summary>
    /// Select a specific GPU device index. 
    /// If the index is invalid or the device is busy, <see cref="Model.Load"/> 
    /// will throw a <see cref="TranscribeException"/> with <see cref="Status.ErrBackend"/>.
    /// </summary>
    public ModelLoadParamsBuilder WithGpuDevice(int device)
    {
        @params.gpuDevice = device;
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
