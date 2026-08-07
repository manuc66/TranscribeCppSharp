#nullable enable

using System;
using TranscribeCppSharp.Interop;

namespace TranscribeCppSharp;

/// <summary>
/// Fluent builder for Voxtral realtime streaming extension parameters.
/// </summary>
public sealed class VoxtralExtBuilder : IDisposable
{
    private readonly ExtBuffer<VoxtralRealtimeStreamExt> buffer;

    /// <inheritdoc/>
    public VoxtralExtBuilder()
    {
        buffer = new ExtBuffer<VoxtralRealtimeStreamExt>(
            NativeMethods.VoxtralRealtimeStreamExtInit,
            static p => p.ext.size,
            nameof(VoxtralExtBuilder));
    }

    /// <summary>Number of delay tokens.</summary>
    public VoxtralExtBuilder WithNumDelayTokens(int numDelayTokens)
    {
        buffer.ThrowIfDisposed();
        buffer.Params.numDelayTokens = numDelayTokens;
        return this;
    }

    /// <summary>Minimum decode interval in milliseconds.</summary>
    public VoxtralExtBuilder WithMinDecodeIntervalMs(int ms)
    {
        buffer.ThrowIfDisposed();
        buffer.Params.minDecodeIntervalMs = ms;
        return this;
    }

    internal IntPtr Build() => buffer.Build();

    /// <inheritdoc/>
    public void Dispose() => buffer.Dispose();
}
