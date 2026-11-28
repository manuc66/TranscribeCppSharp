namespace TranscribeCppSharp.Voiceprint;

/// <summary>Quality gate for embeddings. No universal threshold; no `nl` currently.</summary>
public sealed record VoiceprintQualityPolicy(
    int MinSourceSampleCount = 1,
    int MinEmbeddingCount = 1,
    int RequiredDimension = 0,
    double MinNorm = 0.0,
    double MaxNorm = double.PositiveInfinity,
    double MinMeanPairwiseCosine = double.NegativeInfinity,
    double MinSelfMatch = double.NegativeInfinity,
    double MaxOutlierScore = double.PositiveInfinity,
    bool RequireFinite = true,
    bool RequireSameModel = true,
    bool RequireSameDimension = true)
{
    /// <summary>Validates structure, not accuracy.</summary>
    public void Validate()
    {
        System.ArgumentOutOfRangeException.ThrowIfNegative(MinSourceSampleCount);
        System.ArgumentOutOfRangeException.ThrowIfNegative(MinEmbeddingCount);
        System.ArgumentOutOfRangeException.ThrowIfNegative(RequiredDimension);
        System.ArgumentOutOfRangeException.ThrowIfNegative(MinNorm);

        if (!double.IsPositiveInfinity(MaxNorm) && MaxNorm < MinNorm)
        {
            throw new System.ArgumentOutOfRangeException(nameof(MaxNorm));
        }

        if (double.IsNaN(MinMeanPairwiseCosine))
        {
            throw new System.ArgumentOutOfRangeException(nameof(MinMeanPairwiseCosine));
        }

        if (double.IsNaN(MinSelfMatch))
        {
            throw new System.ArgumentOutOfRangeException(nameof(MinSelfMatch));
        }

        if (double.IsNaN(MaxOutlierScore))
        {
            throw new System.ArgumentOutOfRangeException(nameof(MaxOutlierScore));
        }
    }
}
