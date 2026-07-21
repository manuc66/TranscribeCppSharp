#nullable enable

using System;
using System.Collections.Generic;

namespace TranscribeCppSharp;

/// <summary>A transcribed text segment with timestamps.</summary>
public record SegmentResult(TimeSpan Start, TimeSpan End, string Text, int SpeakerId = -1);

/// <summary>A transcribed word with timestamps.</summary>
public record WordResult(TimeSpan Start, TimeSpan End, string Text);

/// <summary>A transcription token with timing and probability.</summary>
public record TokenResult(int Id, float Probability, TimeSpan Start, TimeSpan End, string Text);

/// <summary>Performance timings for a transcription run.</summary>
public record TimingsResult(float LoadMs, float MelMs, float EncodeMs, float DecodeMs);

/// <summary>Complete transcription result.</summary>
public sealed class Transcript
{
    public string FullText { get; init; } = "";
    public string DetectedLanguage { get; init; } = "";
    public IReadOnlyList<SegmentResult> Segments { get; init; } = [];
    public IReadOnlyList<WordResult> Words { get; init; } = [];
    public IReadOnlyList<TokenResult> Tokens { get; init; } = [];
    public TimingsResult? Timing { get; init; }
}
