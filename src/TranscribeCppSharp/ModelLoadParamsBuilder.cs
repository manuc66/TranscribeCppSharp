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
    private readonly IntPtr handle;
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
    /// An invalid index (negative, out of range, non-GPU device, vendor
    /// mismatch, or non-zero with a CPU/CPU_ACCEL request) makes
    /// <see cref="Model.Load"/> throw a <see cref="TranscribeException"/> with
    /// <see cref="Status.ErrInvalidArg"/>. <see cref="Status.ErrBackend"/> is
    /// reserved for the requested backend being unavailable entirely.
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
