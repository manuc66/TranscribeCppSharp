namespace TranscribeCppSharp.MeetingAnalytics.Voiceprint;

using System;
using TranscribeCppSharp.MeetingAnalytics;
using TranscribeCppSharp.Voiceprint;

/// <summary>Optional identity resolution for meeting analytics. Anonymous by default; no `nl`.</summary>
public sealed record MeetingSpeakerResolutionOptions(
    AmbiguityPolicy IdentityPolicy,
    MeetingAnalyticsOptions? AnalyticsOptions = null,
    AudioQualityPolicy? AudioQuality = null,
    bool PersistUnknownSpeakers = true,
    int MaxSnippetsPerSpeaker = 3,
    int MinTurnMs = 0,
    int PaddingMs = 0,
    int MaxSnippetMs = 0,
    string? ModelSha256 = null)
{
    /// <summary>Validates structural options; thresholds remain caller-calibrated.</summary>
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(IdentityPolicy);
        IdentityPolicy.Validate();
        AnalyticsOptions?.Validate();
        AudioQuality?.Validate();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxSnippetsPerSpeaker);
        ArgumentOutOfRangeException.ThrowIfNegative(MinTurnMs);
        ArgumentOutOfRangeException.ThrowIfNegative(PaddingMs);
        ArgumentOutOfRangeException.ThrowIfNegative(MaxSnippetMs);
    }
}
