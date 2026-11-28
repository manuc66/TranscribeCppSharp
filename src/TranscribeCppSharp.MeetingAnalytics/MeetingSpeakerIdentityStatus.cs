namespace TranscribeCppSharp.MeetingAnalytics;

/// <summary>Optional label state for a per-recording speaker. No biometric proof.</summary>
public enum MeetingSpeakerIdentityStatus
{
    /// <summary>No identity layer supplied.</summary>
    Anonymous,

    /// <summary>Caller-supplied `PersonId` after voiceprint match.</summary>
    Known,

    /// <summary>No known person matched.</summary>
    Unknown,

    /// <summary>Possible match, insufficient score/margin.</summary>
    Ambiguous,
}
