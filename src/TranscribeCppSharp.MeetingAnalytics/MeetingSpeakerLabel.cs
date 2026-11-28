namespace TranscribeCppSharp.MeetingAnalytics;

/// <summary>Optional per-recording speaker label. `SpeakerId` is not stable identity.</summary>
public sealed record MeetingSpeakerLabel(
    int SpeakerId,
    string? PersonId,
    string? DisplayName,
    MeetingSpeakerIdentityStatus Status,
    string? UnknownId = null);
