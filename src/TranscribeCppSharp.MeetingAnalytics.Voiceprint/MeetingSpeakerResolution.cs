namespace TranscribeCppSharp.MeetingAnalytics.Voiceprint;

using System.Collections.Generic;
using TranscribeCppSharp.MeetingAnalytics;
using TranscribeCppSharp.Voiceprint;

/// <summary>Analytics plus optional identity labels. Raw transcript is not rewritten.</summary>
public sealed record MeetingSpeakerResolution(
    MeetingAnalytics Analytics,
    IReadOnlyList<SpeakerIdentity> Identities,
    IReadOnlyList<UnknownSpeaker> UnknownSpeakers,
    IReadOnlyDictionary<int, string> SpeakerNames);
