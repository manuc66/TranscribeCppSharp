namespace TranscribeCppSharp.Voiceprint;

using TranscribeCppSharp.Audio;

/// <summary>`SpeakerAssignment` + source diarization probability. Not speaker identity; no `nl`.</summary>
public sealed record AssignedSpeakerSegment(
    MergedSegment Segment,
    int SpeakerId,
    float? DiarizationProbability,
    string? DiarizationProbabilityState);
