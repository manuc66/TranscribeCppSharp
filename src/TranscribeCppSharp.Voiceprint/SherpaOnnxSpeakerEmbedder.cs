namespace TranscribeCppSharp.Voiceprint;

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SherpaOnnx;

/// <summary>
/// <see cref="ISpeakerEmbedder"/> backed by sherpa-onnx local ONNX speaker embedding.
/// </summary>
/// <remarks>
/// Model weights are not bundled. Native sherpa-onnx runtime must be provided by
/// the application. This type is not thread-safe.
/// </remarks>
public sealed class SherpaOnnxSpeakerEmbedder : ISpeakerEmbedder, IDisposable
{
    private readonly SpeakerEmbeddingExtractor extractor;
    private bool disposed;

    /// <summary>Creates a local CPU speaker embedder.</summary>
    /// <param name="modelPath">Path to a sherpa-onnx speaker embedding .onnx model.</param>
    /// <param name="modelId">Stable id persisted with voiceprints; pin version + quantization.</param>
    /// <param name="numThreads">ONNX Runtime CPU threads. Benchmark; do not assume more is faster.</param>
    /// <param name="modelSha256">Optional expected SHA-256 of model file; verified against actual bytes. No `nl`.</param>
    public SherpaOnnxSpeakerEmbedder(string modelPath, string modelId, int numThreads = 1, string? modelSha256 = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(numThreads);

        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException("Speaker embedding model not found.", modelPath);
        }

        if (modelSha256 is not null && !ModelSha256Verifier.Verify(modelPath, modelSha256))
        {
            throw new ArgumentException("Speaker embedding model SHA-256 mismatch.", nameof(modelSha256));
        }

        ModelId = modelId;
        SampleRate = 16_000;

        var config = new SpeakerEmbeddingExtractorConfig
        {
            Model = modelPath,
            NumThreads = numThreads,
            Debug = 0,
            Provider = "cpu",
        };

        extractor = new SpeakerEmbeddingExtractor(config);
    }

    /// <inheritdoc />
    public string ModelId { get; }

    /// <inheritdoc />
    public int SampleRate { get; }

    /// <inheritdoc />
    public int Dimension => extractor.Dim;

    /// <inheritdoc />
    public ValueTask<float[]> EmbedAsync(ReadOnlyMemory<float> pcm16kMono, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        if (pcm16kMono.IsEmpty)
        {
            throw new ArgumentException("PCM snippet is empty.", nameof(pcm16kMono));
        }

        // sherpa-onnx C# AcceptWaveform takes float[], not ReadOnlyMemory.
        float[] samples = pcm16kMono.ToArray();
        ValidateFinite(samples, nameof(pcm16kMono));

        using OnlineStream stream = extractor.CreateStream();
        stream.AcceptWaveform(SampleRate, samples);
        stream.InputFinished();

        cancellationToken.ThrowIfCancellationRequested();

        if (!extractor.IsReady(stream))
        {
            throw new ArgumentException("Speaker snippet is too short for this model.", nameof(pcm16kMono));
        }

        float[] embedding = extractor.Compute(stream);
        ValidateFinite(embedding, nameof(pcm16kMono));

        // Native call cannot be canceled safely mid-computation; check at boundary.
        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.FromResult(embedding);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        extractor.Dispose();
        disposed = true;
        GC.SuppressFinalize(this);
    }

    private static void ValidateFinite(float[] values, string paramName)
    {
        foreach (float value in values)
        {
            if (!float.IsFinite(value))
            {
                throw new ArgumentException("PCM/embedding contains NaN or infinity.", paramName);
            }
        }
    }
}
