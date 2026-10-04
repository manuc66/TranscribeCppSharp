using TranscribeCppSharp.Ui.Models;

namespace TranscribeCppSharp.Ui.Services;

public interface ITranscriptionService
{
    Task<TranscriptionResult> TranscribeAsync(
        string audioPath,
        string modelPath,
        TranscriptionOptions options,
        IProgress<double>? progress = null,
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
