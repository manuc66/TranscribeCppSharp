namespace TranscribeCppSharp.Voiceprint;

using System;
using System.Collections.Generic;

/// <summary>A locally saved speaker profile containing embeddings, not audio.</summary>
/// <param name="PersonId">Stable caller-owned person id; not a transcribe.cpp numeric SpeakerId.</param>
/// <param name="DisplayName">Name to put on exports after explicit user choice.</param>
/// <param name="EmbeddingModelId">Exact embedding model id; embeddings across models must not mix.</param>
/// <param name="Dimension">Embedding dimension.</param>
/// <param name="Embeddings">One or more enrollment embeddings for the same person.</param>
/// <param name="CreatedAt">UTC creation time.</param>
/// <param name="SourceSampleCount">Number of enrollment snippets used.</param>
public sealed record Voiceprint(
    string PersonId,
    string DisplayName,
    string EmbeddingModelId,
    int Dimension,
    IReadOnlyList<float[]> Embeddings,
    DateTimeOffset CreatedAt,
    int SourceSampleCount);
