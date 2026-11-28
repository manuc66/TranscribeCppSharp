namespace TranscribeCppSharp.Voiceprint;

/// <summary>Audio gate before speaker embedding. No universal defaults claimed.</summary>
public sealed record AudioQualityPolicy(
    int MinSampleRate = 16_000,
    int RequiredSampleRate = 16_000,
    int MinChannels = 1,
    int Channels = 1,
    int MinBitDepth = 16,
    int BitDepth = 16,
    bool Require16BitPcm = true,
    bool RequireMono = true,
    bool RequireResampled16k = true,
    int MinDurationMs = 0,
    int MaxDurationMs = 0,
    int MinTurnMs = 0,
    int MaxSnippetMs = 0,
    double MinRms = 0.0,
    double MaxPeak = 1.0,
    double MaxClippingRatio = 1.0,
    double MaxSilenceRatio = 1.0,
    double MinSnrDb = double.NegativeInfinity,
    double MinSpeakerProbability = double.NaN,
    bool RequireFinite = true,
    bool RequireSpeech = false,
    bool RejectOverlap = false)
{
    /// <summary>Validates structural thresholds. NaN `MinSpeakerProbability` means not used.</summary>
    public void Validate()
    {
        System.ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MinSampleRate);
        System.ArgumentOutOfRangeException.ThrowIfNegativeOrZero(RequiredSampleRate);
        System.ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MinChannels);
        System.ArgumentOutOfRangeException.ThrowIfNegativeOrZero(Channels);
        System.ArgumentOutOfRangeException.ThrowIfNegative(MinBitDepth);
        System.ArgumentOutOfRangeException.ThrowIfNegative(BitDepth);
        System.ArgumentOutOfRangeException.ThrowIfNegative(MinDurationMs);
        System.ArgumentOutOfRangeException.ThrowIfNegative(MaxDurationMs);
        System.ArgumentOutOfRangeException.ThrowIfNegative(MinTurnMs);
        System.ArgumentOutOfRangeException.ThrowIfNegative(MaxSnippetMs);
        System.ArgumentOutOfRangeException.ThrowIfNegative(MinRms);
        System.ArgumentOutOfRangeException.ThrowIfNegative(MaxPeak);
        System.ArgumentOutOfRangeException.ThrowIfNegative(MaxClippingRatio);
        System.ArgumentOutOfRangeException.ThrowIfNegative(MaxSilenceRatio);

        if (double.IsNaN(MinSnrDb))
        {
            throw new System.ArgumentOutOfRangeException(nameof(MinSnrDb));
        }

        if (!double.IsNaN(MinSpeakerProbability) && (MinSpeakerProbability < 0.0 || MinSpeakerProbability > 1.0))
        {
            throw new System.ArgumentOutOfRangeException(nameof(MinSpeakerProbability));
        }
    }
}
