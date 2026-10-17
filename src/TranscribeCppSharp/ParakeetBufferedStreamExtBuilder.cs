namespace TranscribeCppSharp;

using System;
using TranscribeCppSharp.Interop;

/// <summary>
/// Fluent builder for Parakeet buffered streaming extension parameters.
/// </summary>
public sealed class ParakeetBufferedStreamExtBuilder : IDisposable
{
    private readonly ExtBuffer<ParakeetBufferedStreamExt> buffer;

    /// <inheritdoc/>
    public ParakeetBufferedStreamExtBuilder()
    {
        buffer = new ExtBuffer<ParakeetBufferedStreamExt>(
            NativeMethods.ParakeetBufferedStreamExtInit,
            static p => p.ext.size,
            nameof(ParakeetBufferedStreamExtBuilder));
    }

    /// <summary>Left context in milliseconds.</summary>
    public ParakeetBufferedStreamExtBuilder WithLeftMs(int leftMs)
    {
        buffer.ThrowIfDisposed();
        buffer.Params.leftMs = leftMs;
        return this;
    }

    /// <summary>Chunk size in milliseconds.</summary>
    public ParakeetBufferedStreamExtBuilder WithChunkMs(int chunkMs)
    {
        buffer.ThrowIfDisposed();
        buffer.Params.chunkMs = chunkMs;
        return this;
    }

    /// <summary>Right context in milliseconds.</summary>
    public ParakeetBufferedStreamExtBuilder WithRightMs(int rightMs)
    {
        buffer.ThrowIfDisposed();
        buffer.Params.rightMs = rightMs;
        return this;
    }

    /// <summary>
    /// What the native <c>parakeet_buffered_stream_ext_init</c> sets, read from the
    /// initialized struct rather than written here.
    /// </summary>
    /// <remarks>
    /// Each field's native default is the sentinel -1, meaning "use the model
    /// default for this field"; leaving the field out of the extension asks for the
    /// same thing. The model's own default is a (left, chunk, right) tuple in its
    /// training menu — for parakeet-unified-en-0.6b the best-accuracy one is
    /// 5600/1040/1040 ms, per the interop struct's doc.
    /// </remarks>
    private static ParakeetBufferedStreamExt Initialized()
    {
        using var builder = new ParakeetBufferedStreamExtBuilder();
        return builder.buffer.Params;
    }

    /// <inheritdoc cref="Initialized"/>
    public static int DefaultLeftMs => Initialized().leftMs;

    /// <inheritdoc cref="Initialized"/>
    public static int DefaultChunkMs => Initialized().chunkMs;

    /// <inheritdoc cref="Initialized"/>
    public static int DefaultRightMs => Initialized().rightMs;

    internal IntPtr Build() => buffer.Build();

    /// <inheritdoc/>
    public void Dispose() => buffer.Dispose();
}
