#nullable enable

using System.Collections.Generic;
using TranscribeCppSharp.Interop;

namespace TranscribeCppSharp;

/// <summary>
/// Result of a single item in a batch transcription.
/// </summary>
public record BatchResult(
    int Index,
    string FullText,
    string DetectedLanguage,
    Status Status,
    IReadOnlyList<SegmentResult> Segments,
    IReadOnlyList<WordResult> Words,
    IReadOnlyList<TokenResult> Tokens,
    TimingsResult? Timing);
