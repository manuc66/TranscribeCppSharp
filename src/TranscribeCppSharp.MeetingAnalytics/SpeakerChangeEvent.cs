namespace TranscribeCppSharp.MeetingAnalytics;

using System;

/// <summary>Heuristic change in the active speaker sequence.</summary>
public sealed record SpeakerChangeEvent(TimeSpan At, int FromSpeakerId, int ToSpeakerId);
