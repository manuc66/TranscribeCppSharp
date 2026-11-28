namespace TranscribeCppSharp.MeetingAnalytics;

using System;

/// <summary>Heuristic overlap event. Not a verified interruption, not a `PersonId`.</summary>
public sealed record InterruptionEvent(
    TimeSpan Start,
    TimeSpan End,
    int InterruptedSpeakerId,
    int InterruptingSpeakerId,
    InterruptionKind Kind)
{
    /// <summary>Overlap duration.</summary>
    public TimeSpan Duration => End - Start;
}
