#nullable enable

using System;

namespace TranscribeCppSharp;

/// <summary>A "who spoke when" row, populated when diarization ran.</summary>
public record SpeakerSegmentResult(TimeSpan Start, TimeSpan End, int SpeakerId, float Probability);
