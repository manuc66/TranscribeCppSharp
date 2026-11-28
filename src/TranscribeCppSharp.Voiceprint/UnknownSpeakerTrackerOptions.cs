namespace TranscribeCppSharp.Voiceprint;

/// <summary>Bounds for anonymous unknown-speaker persistence. `0` = no cap. No `nl`.</summary>
public sealed record UnknownSpeakerTrackerOptions(
    int MaxEmbeddingsPerUnknown = 0,
    int MaxSources = 0,
    int MaxSourcesPerTranscript = 0)
{
    /// <summary>Validates caps.</summary>
    public void Validate()
    {
        System.ArgumentOutOfRangeException.ThrowIfNegative(MaxEmbeddingsPerUnknown);
        System.ArgumentOutOfRangeException.ThrowIfNegative(MaxSources);
        System.ArgumentOutOfRangeException.ThrowIfNegative(MaxSourcesPerTranscript);
    }
}
