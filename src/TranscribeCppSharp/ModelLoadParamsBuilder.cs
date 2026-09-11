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
    /// Select an exact compute device (handle from
    /// <see cref="Backends.EnumerateDevices"/>). NULL (default) means
    /// automatic selection. Exact selection never falls back to another
    /// device; a mismatched backend/device pair makes
    /// <see cref="Model.Load"/> throw <see cref="TranscribeException"/>.
    /// Handles are process-local: persist DeviceId, re-enumerate per process.
    /// A device without a handle (IntPtr.Zero, e.g. hand-constructed) is
    /// rejected — pass no device for automatic selection.
    /// </summary>
    public ModelLoadParamsBuilder WithDevice(BackendDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (device.Handle == IntPtr.Zero)
        {
            throw new ArgumentException(
                "The device has no runtime handle (IntPtr.Zero): it was not produced by " +
                $"{nameof(Backends.EnumerateDevices)}(). A zero handle means automatic allocation; omit {nameof(WithDevice)} for that instead.",
                nameof(device));
        }

        @params.device = device.Handle;
        return this;
    }

    /// <summary>Select an exact compute device by its runtime-owned handle.</summary>
    public ModelLoadParamsBuilder WithDevice(IntPtr deviceHandle)
    {
        @params.device = deviceHandle;
        return this;
    }

    /// <summary>
    /// Removed in transcribe.cpp 0.2: integer GPU indices no longer exist.
    /// Enumerate <see cref="Backends.EnumerateDevices"/> and use
    /// <see cref="WithDevice(BackendDevice)"/> instead (0 is now a
    /// selectable device, no longer the auto sentinel — pass NULL /
    /// do not call WithDevice for automatic selection).
    /// </summary>
    [Obsolete("transcribe.cpp 0.2 removed integer gpu_device indices. Use WithDevice(BackendDevice) from Backends.EnumerateDevices() instead.", error: true)]
    public ModelLoadParamsBuilder WithGpuDevice(int device)
    {
        throw new NotSupportedException(
            "transcribe.cpp 0.2 removed integer gpu_device indices. " +
            "Enumerate Backends.EnumerateDevices() and use WithDevice() instead.");
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
