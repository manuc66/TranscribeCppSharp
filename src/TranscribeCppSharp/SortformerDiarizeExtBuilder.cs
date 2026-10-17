namespace TranscribeCppSharp;

using System;
using TranscribeCppSharp.Interop;

/// <summary>
/// Fluent builder for the Sortformer diarization extension parameters.
/// </summary>
/// <remarks>
/// transcribe.cpp 0.4 moved Sortformer out of the ASR/streaming API: a
/// Sortformer model serves <see cref="TranscribeRole.Diarize"/>, not
/// <see cref="TranscribeRole.Asr"/>, and its preset is carried on
/// <c>transcribe_diarize_params::family</c> (the DIARIZE_RUN extension slot)
/// rather than on the stream. Pass this builder to
/// <see cref="DiarizeParamsBuilder.WithSortformerExt"/>.
/// </remarks>
public sealed class SortformerDiarizeExtBuilder : IDisposable
{
    private readonly ExtBuffer<SortformerDiarizeExt> buffer;

    /// <inheritdoc/>
    public SortformerDiarizeExtBuilder()
    {
        buffer = new ExtBuffer<SortformerDiarizeExt>(
            NativeMethods.SortformerDiarizeExtInit,
            static p => p.ext.size,
            nameof(SortformerDiarizeExtBuilder));
    }

    /// <summary>Latency/quality preset for Sortformer diarization.</summary>
    public SortformerDiarizeExtBuilder WithPreset(SortformerPreset preset)
    {
        buffer.ThrowIfDisposed();
        buffer.Params.preset = preset;
        return this;
    }

    internal IntPtr Build() => buffer.Build();

    /// <inheritdoc/>
    public void Dispose() => buffer.Dispose();
}
