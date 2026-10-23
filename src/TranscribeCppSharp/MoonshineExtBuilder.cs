namespace TranscribeCppSharp;

using System;
using TranscribeCppSharp.Interop;

/// <summary>
/// Fluent builder for Moonshine streaming extension parameters.
/// </summary>
public sealed class MoonshineExtBuilder : ExtBuilder<MoonshineStreamingStreamExt>
{
    /// <summary>Creates a new builder.</summary>
    public MoonshineExtBuilder()
        : base(
            NativeMethods.MoonshineStreamingStreamExtInit,
            static p => p.ext.size,
            nameof(MoonshineExtBuilder))
    {
    }

    /// <summary>Minimum decode interval in milliseconds.</summary>
    public MoonshineExtBuilder WithMinDecodeIntervalMs(int ms)
    {
        ThrowIfDisposed();
        Params.minDecodeIntervalMs = ms;
        return this;
    }

    /// <summary>
    /// What the native <c>moonshine_streaming_stream_ext_init</c> sets, read from
    /// the initialized struct rather than written here.
    /// </summary>
    /// <remarks>
    /// The native default is a sentinel, not a cadence: -1 means "use the family
    /// default", which is what leaving the extension off also asks for. The value
    /// is read from the struct so an upstream change is not silently missed; the
    /// meaning is upstream's, documented on the interop struct.
    /// </remarks>
    public static int DefaultMinDecodeIntervalMs
    {
        get
        {
            using var builder = new MoonshineExtBuilder();
            return builder.Params.minDecodeIntervalMs;
        }
    }

    internal new IntPtr Build() => base.Build();

    // Dispose is inherited from ExtBuilder<T>.
}
