namespace TranscribeCppSharp.MeetingAnalytics;

using System;
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
    int IgnoredTurnCount,
    IReadOnlyList<MeetingSpeakerLabel> SpeakerLabels)
{
    /// <summary>Returns analytics with optional labels; original statistics are unchanged.</summary>
    public MeetingAnalytics WithSpeakerLabels(IReadOnlyList<MeetingSpeakerLabel> labels)
    {
        ArgumentNullException.ThrowIfNull(labels);
        return this with { SpeakerLabels = labels };
    }
}
