namespace TranscribeCppSharp;

using System;

/// <summary>A transcription token with timing and probability.</summary>
public record TokenResult(int Id, float Probability, TimeSpan Start, TimeSpan End, string Text);
