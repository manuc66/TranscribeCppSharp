#nullable enable

using System;
using System.Runtime.InteropServices;
using TranscribeCppSharp.Interop;

namespace TranscribeCppSharp;

/// <summary>
/// Fluent builder for streaming parameters.
/// </summary>
public sealed class StreamParamsBuilder : IDisposable
{
    private IntPtr _handle;
    private StreamParams _params;
    private bool _disposed;

    public StreamParamsBuilder()
    {
        var abiSize = (int)NativeMethods.AbiStructSize(AbiStruct.AbiStreamParams);
        var csSize = Marshal.SizeOf<StreamParams>();
        if (csSize != abiSize)
            throw new InvalidOperationException(
                $"ABI struct size mismatch for StreamParams: C# expects {csSize} bytes, native reports {abiSize} bytes.");
        _handle = Marshal.AllocHGlobal(abiSize);
        NativeMethods.StreamParamsInit(_handle);
        _params = Marshal.PtrToStructure<StreamParams>(_handle);
    }

    /// <summary>When to commit results to the output.</summary>
    public StreamParamsBuilder WithCommitPolicy(StreamCommitPolicy policy)
    {
        _params.commitPolicy = policy;
        return this;
    }

    /// <summary>Number of stable prefix agreements before auto-commit.</summary>
    public StreamParamsBuilder WithStablePrefixAgreement(uint n)
    {
        _params.stablePrefixAgreementN = n;
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
