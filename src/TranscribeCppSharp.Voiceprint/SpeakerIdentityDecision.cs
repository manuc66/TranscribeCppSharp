namespace TranscribeCppSharp.Voiceprint;

/// <summary>Outcome of matching a speaker embedding against local voiceprints.</summary>
public enum SpeakerIdentityDecision
{
    /// <summary>Score and margin passed caller-calibrated thresholds.</summary>
    Known,

    /// <summary>No saved voiceprint with same model + dimension produced a match.</summary>
    Unknown,

    /// <summary>A possible match exists, but score/margin is insufficient.</summary>
    Ambiguous,
}
