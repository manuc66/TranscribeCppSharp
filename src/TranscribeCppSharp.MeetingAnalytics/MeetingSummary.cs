namespace TranscribeCppSharp.MeetingAnalytics;

using System;

/// <summary>Recording-level summary. Overlap/interruption are heuristics, not identity.</summary>
public sealed record MeetingSummary(
    TimeSpan Duration,
    int SpeakerCount,
    TimeSpan TotalSpeechTime,
    TimeSpan TotalSilenceTime,
    TimeSpan LongestSilence,
    TimeSpan TotalOverlapTime,
    double OverlapRatio,
    int? DominantSpeakerId,
    double DominantSpeakerShare,
    double TalkShareStandardDeviation,
    int SpeakerChangeCount,
    int IgnoredTurnCount,
    int WordCountAssigned);
