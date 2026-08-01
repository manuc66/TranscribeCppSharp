#nullable enable

using System;
using TranscribeCppSharp.Interop;

namespace TranscribeCppSharp;

/// <summary>
/// Fluent builder for Voxtral realtime streaming extension parameters.
/// </summary>
public sealed class VoxtralExtBuilder : IDisposable
{
    private readonly ExtBuffer<VoxtralRealtimeStreamExt> _buffer;

    public VoxtralExtBuilder()
    {
        _buffer = new ExtBuffer<VoxtralRealtimeStreamExt>(
            NativeMethods.VoxtralRealtimeStreamExtInit,
            static p => p.ext.size,
            nameof(VoxtralExtBuilder));
    }

    /// <summary>Number of delay tokens.</summary>
    public VoxtralExtBuilder WithNumDelayTokens(int numDelayTokens)
    {
        _buffer.Params.numDelayTokens = numDelayTokens;
        return this;
    }

    /// <summary>Minimum decode interval in milliseconds.</summary>
    public VoxtralExtBuilder WithMinDecodeIntervalMs(int ms)
    {
        _buffer.Params.minDecodeIntervalMs = ms;
        return this;
    }

    internal IntPtr Build() => _buffer.Build();

    public void Dispose() => _buffer.Dispose();
}
