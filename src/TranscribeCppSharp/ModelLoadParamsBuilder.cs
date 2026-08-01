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
    private IntPtr _handle;
    private ModelLoadParams _params;
    private bool _disposed;

    public ModelLoadParamsBuilder()
    {
        AbiValidation.ValidateSize<ModelLoadParams>(AbiStruct.AbiModelLoadParams, nameof(ModelLoadParams));
        _handle = Marshal.AllocHGlobal(Marshal.SizeOf<ModelLoadParams>());
        NativeMethods.ModelLoadParamsInit(_handle);
        _params = Marshal.PtrToStructure<ModelLoadParams>(_handle);
    }

    /// <summary>Select the compute backend.</summary>
    public ModelLoadParamsBuilder WithBackend(BackendRequest backend)
    {
        _params.backend = backend;
        return this;
    }

    /// <summary>
    /// Select a specific GPU device index. 
    /// If the index is invalid or the device is busy, <see cref="Model.Load"/> 
    /// will throw a <see cref="TranscribeException"/> with <see cref="Status.ErrBackend"/>.
    /// </summary>
    public ModelLoadParamsBuilder WithGpuDevice(int device)
    {
        _params.gpuDevice = device;
        return this;
    }

    internal IntPtr Build()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ModelLoadParamsBuilder));
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
