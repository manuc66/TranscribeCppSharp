namespace TranscribeCppSharp.Voiceprint;

/// <summary>Quality of embeddings in a `Voiceprint`. Not `PersonCorrect`, not `Known`, not identity proof.</summary>
public sealed record VoiceprintQuality(
    bool Passed,
    string Reason,
    int Dimension,
    int EmbeddingCount,
    int SourceSampleCount,
    bool Finite,
    bool SameModel,
    bool SameDimension,
    double MinNorm,
    double MaxNorm,
    double MeanNorm,
    double MeanPairwiseCosine,
    double MinSelfMatch,
    double MaxOutlierScore,
    string EmbeddingModelId);
