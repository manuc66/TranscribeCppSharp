#nullable enable

namespace TranscribeCppSharp;

/// <summary>Performance timings for a transcription run.</summary>
public record TimingsResult(float LoadMs, float MelMs, float EncodeMs, float DecodeMs);
