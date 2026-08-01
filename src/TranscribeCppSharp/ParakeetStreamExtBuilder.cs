#nullable enable

using System;
using TranscribeCppSharp.Interop;

namespace TranscribeCppSharp;

/// <summary>
/// Fluent builder for Parakeet streaming extension parameters.
/// </summary>
public sealed class ParakeetStreamExtBuilder : IDisposable
{
    private readonly ExtBuffer<ParakeetStreamExt> _buffer;

    public ParakeetStreamExtBuilder()
    {
        _buffer = new ExtBuffer<ParakeetStreamExt>(
            NativeMethods.ParakeetStreamExtInit,
            static p => p.ext.size,
            nameof(ParakeetStreamExtBuilder));
    }

    /// <summary>Attention context right size.</summary>
    public ParakeetStreamExtBuilder WithAttContextRight(int contextRight)
    {
        _buffer.Params.attContextRight = contextRight;
        return this;
    }

    internal IntPtr Build() => _buffer.Build();

    public void Dispose() => _buffer.Dispose();
}
