#nullable enable

namespace TranscribeCppSharp;

/// <summary>
/// Current text state of a streaming session.
/// </summary>
public record StreamTextResult(
    string FullText,
    string CommittedText,
    string TentativeText);
