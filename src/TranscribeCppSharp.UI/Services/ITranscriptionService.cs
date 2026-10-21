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

/// <summary>One countable step of an offline run.</summary>
/// <remarks>
/// The fraction on <see cref="IProgress{T}"/> is what the bar fills with; this is
/// the same work named as a step, so the UI can say "window 3 of 12" instead of
/// only 25%. <see cref="Completed"/> may be 0: the first report announces the
/// total before the first window has finished.
/// </remarks>
public sealed record TranscriptionStep(TranscriptionPhase Phase, int Completed, int Total);

/// <summary>Where one file of a batch is, when there is nothing finer to report.</summary>
/// <remarks>
/// The batch runs a whole file in one native call, so unlike the single-file path
/// it has no window callback and no fraction within a file. These are the steps it
/// can state truthfully: the file started, and it finished or failed.
/// </remarks>
public enum BatchFilePhase
{
    /// <summary>The file has been opened and the native call is in flight.</summary>
    Started,

    /// <summary>The file produced a result.</summary>
    Completed,

    /// <summary>The file could not be read or the model failed on it.</summary>
    Failed,
}

/// <summary>One file of a batch, named and counted.</summary>
/// <param name="Index">1-based position in the queue the batch was given.</param>
/// <param name="Count">Size of that queue.</param>
/// <param name="AudioPath">The file this step is about.</param>
/// <param name="Phase">Started, completed or failed.</param>
public sealed record BatchFileStep(int Index, int Count, string AudioPath, BatchFilePhase Phase);

public interface ITranscriptionService
{
    Task<TranscriptionResult> TranscribeAsync(
        string audioPath,
        string modelPath,
        TranscriptionOptions options,
        IProgress<double>? progress = null,
        IProgress<string>? liveText = null,
        IProgress<TranscriptionPhase>? phase = null,
        IProgress<TranscriptionStep>? stepProgress = null,
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
        IProgress<BatchFileStep>? fileStepProgress = null,
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
