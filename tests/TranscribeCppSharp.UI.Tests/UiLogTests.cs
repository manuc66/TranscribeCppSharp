using TranscribeCppSharp.Models;
using TranscribeCppSharp.UI.Models;
using TranscribeCppSharp.UI.Services;
using TranscribeCppSharp.UI.ViewModels;
using Xunit;

namespace TranscribeCppSharp.UI.Tests;

/// <summary>
/// A failure the UI catches is also written to a file, so it can be read after
/// the fact instead of only as a status line that cannot be copied.
/// </summary>
[Collection("UiGlobalState")]
public class UiLogTests
{
    private static string TempLog()
        => Path.Combine(
            Path.GetTempPath(),
            "tcsharp-uilog-" + Guid.NewGuid().ToString("N"),
            "ui.log");

    [Fact]
    public void AFailureIsWrittenWithItsTypeMessageAndStack()
    {
        string log = TempLog();
        string? previous = UiLog.FilePathOverride;
        UiLog.FilePathOverride = log;
        try
        {
            // A real throw, so the exception carries a stack trace: one built but
            // never thrown has none, and the stack is the point.
            InvalidOperationException exception;
            try
            {
                throw new InvalidOperationException("boom", new TimeoutException("inner"));
            }
            catch (InvalidOperationException ex)
            {
                exception = ex;
            }

            UiLog.Failure("streaming start", exception);

            Assert.True(File.Exists(log));
            string text = File.ReadAllText(log);
            Assert.Contains("streaming start", text, StringComparison.Ordinal);
            Assert.Contains(nameof(InvalidOperationException), text, StringComparison.Ordinal);
            Assert.Contains("boom", text, StringComparison.Ordinal);
            Assert.Contains(nameof(TimeoutException), text, StringComparison.Ordinal);

            // The stack, not just the message: the line of status text is what the
            // user already had.
            Assert.Contains("UiLogTests", text, StringComparison.Ordinal);
        }
        finally
        {
            UiLog.FilePathOverride = previous;
        }
    }

    /// <summary>
    /// A log that cannot be written must not replace the failure with a worse one.
    /// </summary>
    [Fact]
    public void AnUnwritableLogDoesNotThrow()
    {
        string? previous = UiLog.FilePathOverride;

        // A directory where a file is expected: AppendAllText fails.
        string directory = Path.Combine(Path.GetTempPath(), "tcsharp-uilog-dir-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        UiLog.FilePathOverride = directory;
        try
        {
            UiLog.Failure("nowhere", new InvalidOperationException("boom"));
        }
        finally
        {
            UiLog.FilePathOverride = previous;
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// The reported case: Start on the streaming tab failed, and the status line
    /// now says where the full failure was written.
    /// </summary>
    [Fact]
    public async Task AStreamingFailureIsLoggedAndTheStatusNamesTheFile()
    {
        string log = TempLog();
        string? previous = UiLog.FilePathOverride;
        string previousRoot = ModelStore.CacheRootOverride ?? string.Empty;
        bool hadRoot = ModelStore.CacheRootOverride is not null;
        UiLog.FilePathOverride = log;
        // An empty cache root, so the constructor does not find the user's real
        // models and load one.
        ModelStore.CacheRootOverride = Path.Combine(
            Path.GetTempPath(), "tcsharp-uilog-cache-" + Guid.NewGuid().ToString("N"));
        try
        {
            var viewModel = new StreamingViewModel(
                new ThrowingStreamingService(),
                new SettingsViewModel(),
                new ModelCapabilityCache());

            await viewModel.StartStreamingCommand.ExecuteAsync(null);

            Assert.Contains("logged to", viewModel.StatusMessage, StringComparison.Ordinal);
            Assert.Contains(log, viewModel.StatusMessage, StringComparison.Ordinal);

            Assert.True(File.Exists(log));
            Assert.Contains("boom", File.ReadAllText(log), StringComparison.Ordinal);
        }
        finally
        {
            UiLog.FilePathOverride = previous;
            ModelStore.CacheRootOverride = hadRoot ? previousRoot : null;
        }
    }

    /// <summary>Streams nothing but a failure, as a model that will not load does.</summary>
    private sealed class ThrowingStreamingService : ITranscriptionService
    {
        public Task<TranscriptionResult> TranscribeAsync(
            string audioPath, string modelPath, TranscriptionOptions options,
            IProgress<double>? progress = null, IProgress<string>? liveText = null, IProgress<TranscriptionPhase>? phase = null, IProgress<TranscriptionStep>? step = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public async IAsyncEnumerable<StreamUpdate> StreamTranscribeAsync(
            string modelPath, StreamOptions options,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            throw new InvalidOperationException("boom");
#pragma warning disable CS0162 // unreachable: keeps the method an iterator
            yield break;
#pragma warning restore CS0162
        }

        public Task<MemoryProbeResult> ProbeMemoryAsync(
            string audioPath, string modelPath, TranscriptionOptions options,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<List<BatchItemResult>> BatchTranscribeAsync(
            IReadOnlyList<string> audioPaths, string modelPath, TranscriptionOptions options,
            IProgress<int>? progress = null, IProgress<BatchFileStep>? fileStep = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
