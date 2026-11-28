namespace TranscribeCppSharp.MeetingAnalytics;

using System;

/// <summary>Gap in the union of attributed speech.</summary>
public sealed record SilenceEvent(TimeSpan Start, TimeSpan End)
{
    /// <summary>Silence duration.</summary>
    public TimeSpan Duration => End - Start;
}
