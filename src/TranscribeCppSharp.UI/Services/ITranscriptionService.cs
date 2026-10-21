using TranscribeCppSharp.UI.Models;

namespace TranscribeCppSharp.UI.Services;

/// <summary>
/// Which pass a run is in, when a separate diarizer is chained after the
/// transcriber. There is no third value: a run without a diarizer never leaves
/// <see cref="Transcribing"/>.
/// </summary>
public enum TranscriptionPhase
{
    /// <summary>The transcriber is producing text.</summary>
    Transcribing,

    /// <summary>
    /// The transcriber has finished; the separate diarizer is reading the same
    /// audio for speaker turns. No text is produced in this pass.
    /// </summary>
    Diarizing,
}

public interface ITranscriptionService
{
    Task<TranscriptionResult> TranscribeAsync(
        string audioPath,
        string modelPath,
        TranscriptionOptions options,
        IProgress<double>? progress = null,
        IProgress<string>? liveText = null,
        IProgress<TranscriptionPhase>? phase = null,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<StreamUpdate> StreamTranscribeAsync(
        string modelPath,
        StreamOptions options,
        CancellationToken cancellationToken = default);

    Task<List<BatchItemResult>> BatchTranscribeAsync(
        IReadOnlyList<string> audioPaths,
        string modelPath,
        TranscriptionOptions options,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs at most one window of <paramref name="audioPath"/> with the selected
    /// settings and reports whether it held, without producing a transcript.
    /// </summary>
    /// <remarks>
    /// It exercises the same allocation as the first window of a real run, which is
    /// what an out-of-memory failure at <c>Run</c> is about. It does not test later
    /// windows, the diarizer, or the decoder's output length; the UI says so where
    /// the button is.
    /// </remarks>
    Task<MemoryProbeResult> ProbeMemoryAsync(
        string audioPath,
        string modelPath,
        TranscriptionOptions options,
        CancellationToken cancellationToken = default);
}
