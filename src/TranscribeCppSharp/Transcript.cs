#nullable enable

using System.Collections.Generic;

namespace TranscribeCppSharp;

/// <summary>
/// Complete transcription result.
/// All strings are managed copies — safe to keep after the session is disposed.
/// </summary>
public sealed class Transcript
{
    /// <summary>Complete transcribed text of the run. Corresponds to native transcribe_full_text(session).</summary>
    public string FullText { get; init; } = string.Empty;

    /// <summary>Detected language code (e.g. "en"). Corresponds to native transcribe_detected_language(session).</summary>
    public string DetectedLanguage { get; init; } = string.Empty;

    /// <summary>True when the run was cancelled mid-flight. Corresponds to native transcribe_was_aborted(session).</summary>
    public bool WasAborted { get; init; }

    /// <summary>True when the run was cut short by its token limit. Corresponds to native transcribe_was_truncated(session).</summary>
    public bool WasTruncated { get; init; }

    /// <summary>Segments, read via native transcribe_n_segments / transcribe_get_segment.</summary>
    public IReadOnlyList<SegmentResult> Segments { get; init; } = [];

    /// <summary>Words, read via native transcribe_n_words / transcribe_get_word.</summary>
    public IReadOnlyList<WordResult> Words { get; init; } = [];

    /// <summary>Tokens, read via native transcribe_n_tokens / transcribe_get_token.</summary>
    public IReadOnlyList<TokenResult> Tokens { get; init; } = [];

    /// <summary>Timings of the run, via native transcribe_get_timings. Null when the run produced no timings.</summary>
    public TimingsResult? Timing { get; init; }
}
