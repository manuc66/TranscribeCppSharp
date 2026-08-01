#nullable enable

using System;

namespace TranscribeCppSharp;

/// <summary>
/// Result of a StreamFeed or StreamComplete call.
/// </summary>
public record StreamUpdateResult(
    bool ResultChanged,
    bool IsFinal,
    int Revision,
    TimeSpan InputReceived,
    TimeSpan AudioCommitted,
    TimeSpan Buffered,
    bool CommittedChanged,
    bool TentativeChanged);
