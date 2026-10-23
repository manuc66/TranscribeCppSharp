namespace TranscribeCppSharp;

using System;
using TranscribeCppSharp.Interop;

/// <summary>
/// Fluent builder for Voxtral realtime streaming extension parameters.
/// </summary>
public sealed class VoxtralExtBuilder : ExtBuilder<VoxtralRealtimeStreamExt>
{
    /// <summary>Creates a new builder.</summary>
    public VoxtralExtBuilder()
        : base(
            NativeMethods.VoxtralRealtimeStreamExtInit,
            static p => p.ext.size,
            nameof(VoxtralExtBuilder))
    {
    }

    /// <summary>Number of delay tokens.</summary>
    public VoxtralExtBuilder WithNumDelayTokens(int numDelayTokens)
    {
        ThrowIfDisposed();
        Params.numDelayTokens = numDelayTokens;
        return this;
    }

    /// <summary>Minimum decode interval in milliseconds.</summary>
    public VoxtralExtBuilder WithMinDecodeIntervalMs(int ms)
    {
        ThrowIfDisposed();
        Params.minDecodeIntervalMs = ms;
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
        return builder.Params;
    }

    /// <inheritdoc cref="Initialized"/>
    public static int DefaultNumDelayTokens => Initialized().numDelayTokens;

    /// <inheritdoc cref="Initialized"/>
    public static int DefaultMinDecodeIntervalMs => Initialized().minDecodeIntervalMs;

    internal new IntPtr Build() => base.Build();

    // Dispose is inherited from ExtBuilder<T>.
}
