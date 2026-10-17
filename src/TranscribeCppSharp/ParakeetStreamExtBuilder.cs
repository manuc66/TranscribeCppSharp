namespace TranscribeCppSharp;

using System;
using TranscribeCppSharp.Interop;

/// <summary>
/// Fluent builder for Parakeet streaming extension parameters.
/// </summary>
public sealed class ParakeetStreamExtBuilder : IDisposable
{
    private readonly ExtBuffer<ParakeetStreamExt> buffer;

    /// <inheritdoc/>
    public ParakeetStreamExtBuilder()
    {
        buffer = new ExtBuffer<ParakeetStreamExt>(
            NativeMethods.ParakeetStreamExtInit,
            static p => p.ext.size,
            nameof(ParakeetStreamExtBuilder));
    }

    /// <summary>Attention context right size.</summary>
    public ParakeetStreamExtBuilder WithAttContextRight(int contextRight)
    {
        buffer.ThrowIfDisposed();
        buffer.Params.attContextRight = contextRight;
        return this;
    }

    /// <summary>
    /// What the native <c>parakeet_stream_ext_init</c> sets for the right context,
    /// read from the initialized struct rather than written here.
    /// </summary>
    /// <remarks>
    /// -1 means "use the model's default" — the first entry of its
    /// <c>att_context_size_choices</c>, which is the max-accuracy/max-latency one.
    /// Leaving the extension off asks for the same thing.
    /// </remarks>
    public static int DefaultAttContextRight
    {
        get
        {
            using var builder = new ParakeetStreamExtBuilder();
            return builder.buffer.Params.attContextRight;
        }
    }

    internal IntPtr Build() => buffer.Build();

    /// <inheritdoc/>
    public void Dispose() => buffer.Dispose();
}
