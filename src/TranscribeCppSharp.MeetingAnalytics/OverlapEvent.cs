namespace TranscribeCppSharp.MeetingAnalytics;

using System;

/// <summary>Pairwise overlap interval. Multi-speaker overlap can produce multiple pairs.</summary>
public sealed record OverlapEvent(TimeSpan Start, TimeSpan End, int SpeakerA, int SpeakerB)
{
    /// <summary>Overlap duration.</summary>
    public TimeSpan Duration => End - Start;
}
