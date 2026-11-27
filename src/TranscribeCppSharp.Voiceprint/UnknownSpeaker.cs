namespace TranscribeCppSharp.Voiceprint;

using System;
using System.Collections.Generic;

/// <summary>Anonymous speaker seen in transcripts, pending later identification. Not a `PersonId`.</summary>
public sealed record UnknownSpeaker(
    string UnknownId,
    string EmbeddingModelId,
    int Dimension,
    IReadOnlyList<float[]> Embeddings,
    IReadOnlyList<UnknownSpeakerSource> Sources,
    DateTimeOffset FirstSeen,
    DateTimeOffset LastSeen,
    UnknownSpeakerStatus Status,
    string? PersonId,
    string? DisplayName,
    int SourceSampleCount)
{
    /// <summary>Converts a pending unknown for **cluster matching only**; not identity verification.</summary>
    public Voiceprint ToPendingVoiceprint() => new(
        UnknownId,
        DisplayName ?? UnknownId,
        EmbeddingModelId,
        Dimension,
        Embeddings,
        FirstSeen,
        SourceSampleCount);
}
