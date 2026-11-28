namespace TranscribeCppSharp.MeetingAnalytics;

using System;

/// <summary>Per-recording anonymous speaker statistics. `SpeakerId` is not a `PersonId`.</summary>
public sealed record SpeakerTalkStats(
    int SpeakerId,
    TimeSpan TalkTime,
    double TalkShare,
    int TurnCount,
    TimeSpan AverageTurnDuration,
    TimeSpan LongestTurn,
    TimeSpan FirstSpeechAt,
    TimeSpan LastSpeechAt,
    int WordCount,
    double WordsPerMinute);
