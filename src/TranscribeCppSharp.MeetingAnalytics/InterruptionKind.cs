namespace TranscribeCppSharp.MeetingAnalytics;

/// <summary>Heuristic turn-event classification.</summary>
public enum InterruptionKind
{
    /// <summary>Later speaker overlaps an earlier speaker after minimum speech.</summary>
    Interruption,

    /// <summary>Short overlapping turn; may be a backchannel.</summary>
    Backchannel,
}
