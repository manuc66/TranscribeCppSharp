namespace TranscribeCppSharp.Server;

/// <summary>One transcription job, already validated by the HTTP layer.</summary>
/// <param name="AudioPath">A local file the backend may reopen.</param>
/// <param name="Language">Source language code, or null to let the model decide.</param>
/// <param name="Task">Transcribe, or translate to English.</param>
/// <param name="Diarize">Whether to ask the model for speaker attribution.</param>
/// <param name="WantWords">Whether the caller asked for word timestamps.</param>
/// <param name="CancellationToken">Cancelled when the client disconnects.</param>
internal sealed record TranscriptionRequest(
    string AudioPath,
    string? Language,
    TranscriptionTask Task,
    bool Diarize,
    bool WantWords,
    CancellationToken CancellationToken);

/// <summary>One merged transcript, in file time.</summary>
/// <param name="Text">The whole transcript.</param>
/// <param name="Language">Detected language, empty when the model reports none.</param>
/// <param name="DurationSeconds">Audio duration.</param>
/// <param name="Segments">Segments in order, with a speaker only when attribution ran.</param>
/// <param name="Words">Word timestamps, empty when the model cannot produce them.</param>
internal sealed record TranscriptionOutput(
    string Text,
    string Language,
    double DurationSeconds,
    IReadOnlyList<TranscriptionSegment> Segments,
    IReadOnlyList<TranscriptionWord> Words);

/// <summary>A timed segment of the transcript.</summary>
/// <param name="Id">Zero-based index.</param>
/// <param name="StartSeconds">Start in the original file.</param>
/// <param name="EndSeconds">End in the original file.</param>
/// <param name="Text">What was said.</param>
/// <param name="Speaker">Speaker index, or null when attribution did not run.</param>
internal sealed record TranscriptionSegment(
    int Id,
    double StartSeconds,
    double EndSeconds,
    string Text,
    int? Speaker);

/// <summary>A word and its position in the original file.</summary>
/// <param name="Text">The word.</param>
/// <param name="StartSeconds">Start in the original file.</param>
/// <param name="EndSeconds">End in the original file.</param>
internal sealed record TranscriptionWord(string Text, double StartSeconds, double EndSeconds);

/// <summary>
/// What the HTTP layer needs from a transcriber. Separate from the native
/// implementation so the endpoints can be tested on a machine with no model and
/// no GPU.
/// </summary>
internal interface ITranscriptionBackend : IAsyncDisposable
{
    /// <summary>The id this server answers to in the <c>model</c> form field.</summary>
    string ModelId { get; }

    /// <summary>Whether the model is in memory. Loaded lazily, on first use.</summary>
    bool IsModelLoaded { get; }

    /// <summary>Runs one job. Implementations serialize their native calls.</summary>
    Task<TranscriptionOutput> TranscribeAsync(TranscriptionRequest request);
}

/// <summary>
/// The model is not available and this server is not allowed to fetch it. A
/// configuration problem, not a malformed request.
/// </summary>
internal sealed class ModelUnavailableException : Exception
{
    /// <summary>Creates the exception with a message the client may read.</summary>
    internal ModelUnavailableException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and an inner cause.</summary>
    internal ModelUnavailableException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
