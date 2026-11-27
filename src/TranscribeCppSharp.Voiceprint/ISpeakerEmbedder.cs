namespace TranscribeCppSharp.Voiceprint;

using System;
using System.Threading;
using System.Threading.Tasks;

/// <summary>Extracts speaker embeddings from local 16 kHz mono float PCM.</summary>
public interface ISpeakerEmbedder
{
    /// <summary>Stable model identifier + version + quantization used for embeddings.</summary>
    string ModelId { get; }

    /// <summary>Input sample rate in Hz. TranscribeCppSharp audio is normally 16 kHz.</summary>
    int SampleRate { get; }

    /// <summary>Embedding dimension, or a positive value once the model is loaded.</summary>
    int Dimension { get; }

    /// <summary>
    /// Computes one speaker embedding. Input must be finite 16 kHz mono PCM;
    /// resampling/filtering is the caller's responsibility.
    /// </summary>
    ValueTask<float[]> EmbedAsync(ReadOnlyMemory<float> pcm16kMono, CancellationToken cancellationToken = default);
}
