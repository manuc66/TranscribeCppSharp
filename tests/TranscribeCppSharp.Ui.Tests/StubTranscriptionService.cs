using TranscribeCppSharp.Ui.Models;
using TranscribeCppSharp.Ui.Services;

namespace TranscribeCppSharp.Ui.Tests;

/// <summary>
/// Stands in for the transcription service in view-model tests.
/// </summary>
/// <remarks>
/// Every method throws or yields nothing: the tests exercise what a view model shows
/// before a run starts, never a run. Shared so the two test files that construct
/// view models do not each declare their own copy of the same fifteen lines.
/// </remarks>
internal sealed class StubTranscriptionService : ITranscriptionService
{
    /// <inheritdoc/>
    public Task<TranscriptionResult> TranscribeAsync(
        string audioPath, string modelPath, TranscriptionOptions options,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    /// <inheritdoc/>
    public async IAsyncEnumerable<StreamUpdate> StreamTranscribeAsync(
        string modelPath, StreamOptions options,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        yield break;
    }

    /// <inheritdoc/>
    public Task<List<BatchItemResult>> BatchTranscribeAsync(
        IReadOnlyList<string> audioPaths, string modelPath, TranscriptionOptions options,
        IProgress<int>? progress = null, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
}