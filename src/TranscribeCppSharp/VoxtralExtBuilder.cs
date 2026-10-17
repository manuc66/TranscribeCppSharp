namespace TranscribeCppSharp;

using System;
using TranscribeCppSharp.Interop;

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

    /// <summary>
    /// What the native <c>voxtral_realtime_stream_ext_init</c> sets, read from the
    /// initialized struct rather than written here.
    /// </summary>
    /// <remarks>
    /// Both are the sentinel -1. For the delay tokens that means the model default,
    /// 6 tokens; for the decode interval it means the family default cadence.
    /// Leaving the extension off asks for the same.
    /// </remarks>
    private static VoxtralRealtimeStreamExt Initialized()
    {
        using var builder = new VoxtralExtBuilder();
        return builder.buffer.Params;
    }

    /// <inheritdoc cref="Initialized"/>
    public static int DefaultNumDelayTokens => Initialized().numDelayTokens;

    /// <inheritdoc cref="Initialized"/>
    public static int DefaultMinDecodeIntervalMs => Initialized().minDecodeIntervalMs;

    internal IntPtr Build() => buffer.Build();

    /// <inheritdoc/>
    public void Dispose() => buffer.Dispose();
}
