namespace TranscribeCppSharp.MeetingAnalytics;

using System.Collections.Generic;

/// <summary>Anonymous per-recording speaker analytics. No voiceprint, no `PersonCorrect`, no `nl`.</summary>
public sealed record MeetingAnalytics(
    MeetingSummary Summary,
    IReadOnlyList<SpeakerTalkStats> Speakers,
    IReadOnlyList<OverlapEvent> Overlaps,
    IReadOnlyList<SilenceEvent> Silences,
    IReadOnlyList<InterruptionEvent> Interruptions,
    IReadOnlyList<InterruptionEvent> Backchannels,
    IReadOnlyList<SpeakerChangeEvent> SpeakerChanges,
    int IgnoredTurnCount);
