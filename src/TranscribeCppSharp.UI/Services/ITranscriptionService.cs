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
}
