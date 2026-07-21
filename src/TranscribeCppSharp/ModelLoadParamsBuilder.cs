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
        var abiSize = (int)NativeMethods.AbiStructSize(AbiStruct.AbiModelLoadParams);
        var csSize = Marshal.SizeOf<ModelLoadParams>();
        if (csSize != abiSize)
            throw new InvalidOperationException(
                $"ABI struct size mismatch for ModelLoadParams: C# expects {csSize} bytes, native reports {abiSize} bytes. " +
                $"Regenerate bindings or update the struct definition.");
        _handle = Marshal.AllocHGlobal(abiSize);
        NativeMethods.ModelLoadParamsInit(_handle);
        _params = Marshal.PtrToStructure<ModelLoadParams>(_handle);
    }

    /// <summary>Select the compute backend.</summary>
    public ModelLoadParamsBuilder WithBackend(BackendRequest backend)
    {
        _params.backend = backend;
        return this;
    }

    /// <summary>Select a specific GPU device index.</summary>
    public ModelLoadParamsBuilder WithGpuDevice(int device)
    {
        _params.gpuDevice = device;
        return this;
    }

    internal IntPtr Handle
    {
        get
        {
            Marshal.StructureToPtr(_params, _handle, false);
            return _handle;
        }
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
