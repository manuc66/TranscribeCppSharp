namespace TranscribeCppSharp;

using System;

/// <summary>A transcribed text segment with timestamps.</summary>
public record SegmentResult(TimeSpan Start, TimeSpan End, string Text, int SpeakerId = 0);
