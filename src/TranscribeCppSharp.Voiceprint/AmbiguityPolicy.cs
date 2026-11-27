namespace TranscribeCppSharp.Voiceprint;

/// <summary>Caller-calibrated thresholds. No universal threshold is assumed.</summary>
/// <param name="MinScore">Minimum best cosine score for <see cref="SpeakerIdentityDecision.Known"/>.</param>
/// <param name="MinMargin">Minimum best-minus-second-best margin for Known.</param>
/// <param name="MaxRechecks">Maximum alternative embeddings considered by <see cref="VoiceprintMatcher.MatchWithRechecks"/>.</param>
public sealed record AmbiguityPolicy(
    double MinScore,
    double MinMargin,
    int MaxRechecks = 1)
{
    /// <summary>Validates thresholds. Range checks are structural, not accuracy claims.</summary>
    public void Validate()
    {
        if (double.IsNaN(MinScore) || double.IsInfinity(MinScore) || MinScore < -1.0 || MinScore > 1.0)
        {
            throw new System.ArgumentOutOfRangeException(nameof(MinScore));
        }

        if (double.IsNaN(MinMargin) || double.IsInfinity(MinMargin) || MinMargin < 0.0 || MinMargin > 2.0)
        {
            throw new System.ArgumentOutOfRangeException(nameof(MinMargin));
        }

        System.ArgumentOutOfRangeException.ThrowIfNegative(MaxRechecks);
    }
}
