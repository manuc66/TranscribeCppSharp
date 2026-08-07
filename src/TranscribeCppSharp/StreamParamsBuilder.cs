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
    private readonly IntPtr handle;
    private StreamParams @params;
    private MoonshineExtBuilder? moonshineExt;
    private ParakeetStreamExtBuilder? parakeetStreamExt;
    private ParakeetBufferedStreamExtBuilder? parakeetBufferedExt;
    private VoxtralExtBuilder? voxtralExt;
    private bool disposed;

    /// <inheritdoc/>
    public StreamParamsBuilder()
    {
        AbiValidation.ValidateSize<StreamParams>(AbiStruct.AbiStreamParams, nameof(StreamParams));
        handle = Marshal.AllocHGlobal(Marshal.SizeOf<StreamParams>());
        NativeMethods.StreamParamsInit(handle);
        @params = Marshal.PtrToStructure<StreamParams>(handle);
    }

    /// <summary>When to commit results to the output.</summary>
    public StreamParamsBuilder WithCommitPolicy(StreamCommitPolicy policy)
    {
        @params.commitPolicy = policy;
        return this;
    }

    /// <summary>Number of stable prefix agreements before auto-commit.</summary>
    public StreamParamsBuilder WithStablePrefixAgreement(uint n)
    {
        @params.stablePrefixAgreementN = n;
        return this;
    }

    /// <summary>Moonshine streaming extension parameters.</summary>
    public StreamParamsBuilder WithMoonshineExt(MoonshineExtBuilder ext)
    {
        ArgumentNullException.ThrowIfNull(ext);
        ClearFamily();
        moonshineExt = ext;
        @params.family = ext.Build();
        return this;
    }

    /// <summary>Parakeet streaming extension parameters.</summary>
    public StreamParamsBuilder WithParakeetStreamExt(ParakeetStreamExtBuilder ext)
    {
        ArgumentNullException.ThrowIfNull(ext);
        ClearFamily();
        parakeetStreamExt = ext;
        @params.family = ext.Build();
        return this;
    }

    /// <summary>Parakeet buffered streaming extension parameters.</summary>
    public StreamParamsBuilder WithParakeetBufferedStreamExt(ParakeetBufferedStreamExtBuilder ext)
    {
        ArgumentNullException.ThrowIfNull(ext);
        ClearFamily();
        parakeetBufferedExt = ext;
        @params.family = ext.Build();
        return this;
    }

    /// <summary>Voxtral realtime streaming extension parameters.</summary>
    public StreamParamsBuilder WithVoxtralExt(VoxtralExtBuilder ext)
    {
        ArgumentNullException.ThrowIfNull(ext);
        ClearFamily();
        voxtralExt = ext;
        @params.family = ext.Build();
        return this;
    }

    private void ClearFamily()
    {
        moonshineExt?.Dispose();
        moonshineExt = null;
        parakeetStreamExt?.Dispose();
        parakeetStreamExt = null;
        parakeetBufferedExt?.Dispose();
        parakeetBufferedExt = null;
        voxtralExt?.Dispose();
        voxtralExt = null;
        @params.family = IntPtr.Zero;
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
            ClearFamily();
            Marshal.FreeHGlobal(handle);
            disposed = true;
        }
    }
}
