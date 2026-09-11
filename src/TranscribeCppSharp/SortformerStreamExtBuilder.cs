#nullable enable

using System;
using TranscribeCppSharp.Interop;

namespace TranscribeCppSharp;

/// <summary>
/// Fluent builder for Sortformer streaming extension parameters.
/// </summary>
public sealed class SortformerStreamExtBuilder : IDisposable
{
    private readonly ExtBuffer<SortformerStreamExt> buffer;

    /// <inheritdoc/>
    public SortformerStreamExtBuilder()
    {
        buffer = new ExtBuffer<SortformerStreamExt>(
            NativeMethods.SortformerStreamExtInit,
            static p => p.ext.size,
            nameof(SortformerStreamExtBuilder));
    }

    /// <summary>Latency/quality preset for Sortformer streaming.</summary>
    public SortformerStreamExtBuilder WithPreset(SortformerPreset preset)
    {
        buffer.ThrowIfDisposed();
        buffer.Params.preset = preset;
        return this;
    }

    internal IntPtr Build() => buffer.Build();

    /// <inheritdoc/>
    public void Dispose() => buffer.Dispose();
}
