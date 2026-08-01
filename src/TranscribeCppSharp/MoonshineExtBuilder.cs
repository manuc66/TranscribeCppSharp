#nullable enable

using System;
using TranscribeCppSharp.Interop;

namespace TranscribeCppSharp;

/// <summary>
/// Fluent builder for Moonshine streaming extension parameters.
/// </summary>
public sealed class MoonshineExtBuilder : IDisposable
{
    private readonly ExtBuffer<MoonshineStreamingStreamExt> _buffer;

    public MoonshineExtBuilder()
    {
        _buffer = new ExtBuffer<MoonshineStreamingStreamExt>(
            NativeMethods.MoonshineStreamingStreamExtInit,
            static p => p.ext.size,
            nameof(MoonshineExtBuilder));
    }

    /// <summary>Minimum decode interval in milliseconds.</summary>
    public MoonshineExtBuilder WithMinDecodeIntervalMs(int ms)
    {
        _buffer.Params.minDecodeIntervalMs = ms;
        return this;
    }

    internal IntPtr Build() => _buffer.Build();

    public void Dispose() => _buffer.Dispose();
}
