#nullable enable

using System;
using TranscribeCppSharp.Interop;

namespace TranscribeCppSharp;

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

    internal IntPtr Build() => buffer.Build();

    /// <inheritdoc/>
    public void Dispose() => buffer.Dispose();
}
