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
    private MoonshineExtBuilder? _moonshineExt;
    private ParakeetStreamExtBuilder? _parakeetStreamExt;
    private ParakeetBufferedStreamExtBuilder? _parakeetBufferedExt;
    private VoxtralExtBuilder? _voxtralExt;
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

    /// <summary>Moonshine streaming extension parameters.</summary>
    public StreamParamsBuilder WithMoonshineExt(MoonshineExtBuilder ext)
    {
        ClearFamily();
        _moonshineExt = ext;
        _params.family = ext.Build();
        return this;
    }

    /// <summary>Parakeet streaming extension parameters.</summary>
    public StreamParamsBuilder WithParakeetStreamExt(ParakeetStreamExtBuilder ext)
    {
        ClearFamily();
        _parakeetStreamExt = ext;
        _params.family = ext.Build();
        return this;
    }

    /// <summary>Parakeet buffered streaming extension parameters.</summary>
    public StreamParamsBuilder WithParakeetBufferedStreamExt(ParakeetBufferedStreamExtBuilder ext)
    {
        ClearFamily();
        _parakeetBufferedExt = ext;
        _params.family = ext.Build();
        return this;
    }

    /// <summary>Voxtral realtime streaming extension parameters.</summary>
    public StreamParamsBuilder WithVoxtralExt(VoxtralExtBuilder ext)
    {
        ClearFamily();
        _voxtralExt = ext;
        _params.family = ext.Build();
        return this;
    }

    private void ClearFamily()
    {
        _moonshineExt?.Dispose();
        _moonshineExt = null;
        _parakeetStreamExt?.Dispose();
        _parakeetStreamExt = null;
        _parakeetBufferedExt?.Dispose();
        _parakeetBufferedExt = null;
        _voxtralExt?.Dispose();
        _voxtralExt = null;
        _params.family = IntPtr.Zero;
    }

    internal IntPtr Build()
    {
        Marshal.StructureToPtr(_params, _handle, false);
        return _handle;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            ClearFamily();
            Marshal.FreeHGlobal(_handle);
            _disposed = true;
        }
    }
}
