using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TranscribeCppSharp;
using TranscribeCppSharp.Audio;
using TranscribeCppSharp.Interop;
using TranscribeCppSharp.Models;
using TranscribeCppSharp.SharedSource;
using TranscribeCppSharp.UI.Models;

namespace TranscribeCppSharp.UI.Services;

public partial class TranscriptionService
{
    public async Task<List<BatchItemResult>> BatchTranscribeAsync(
        IReadOnlyList<string> audioPaths,
        string modelPath,
        TranscriptionOptions options,
        IProgress<int>? progress = null,
        IProgress<BatchFileStep>? fileStepProgress = null,
        CancellationToken cancellationToken = default)
    {
        // Before the model is resolved: stopping a batch must not first spend a
        // multi-gigabyte download on a model nobody is going to run.
        cancellationToken.ThrowIfCancellationRequested();

        var model = LoadModel(modelPath, options.BackendRequest, options.Device);

        // Declared outside the try so a cancellation can return what finished
        // instead of throwing it away. The caller's token is what distinguishes
        // "cancelled" from "complete"; the item in flight is dropped, because the
        // wrapper turns a native abort into cancellation before reading its
        // partial result.
        var results = new List<BatchItemResult>();

        try
        {
            using var session = CreateSession(model, options);

            for (int i = 0; i < audioPaths.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var audioPath = audioPaths[i];

                // Started is reported before the audio is opened, so the UI can
                // name the file that is in flight rather than only the one that
                // just finished. The native call has no finer callback.
                fileStepProgress?.Report(new BatchFileStep(i + 1, audioPaths.Count, audioPath, BatchFilePhase.Started));

                // Each file is independent: a file that cannot be opened, or one
                // the model fails on, is recorded and the rest still run. Letting
                // the exception out here would discard the items that already
                // finished and skip every file after the bad one.
                try
                {
                    using var audioSource = AudioLoader.Open(audioPath);

                    var result = await System.Threading.Tasks.Task.Run(() =>
                    {
                        var samples = audioSource.ReadWindow(0, (int)Math.Min(audioSource.LengthSamples, int.MaxValue));
                        return session.Run(samples, builder => Configure(builder, options), cancellationToken);
                    }, cancellationToken);

                    results.Add(new BatchItemResult
                    {
                        AudioPath = audioPath,
                        FullText = result.FullText,
                        DetectedLanguage = result.DetectedLanguage,
                        // The partial text is kept; the label says it stopped early.
                        Status = result.WasAborted
                            ? "Aborted"
                            : result.StatusCode == Status.ErrOutputRepetition
                                ? "Ok (output repetition)"
                                : result.StatusCode == Status.ErrOutputTruncated
                                    ? "Ok (truncated)"
                                    : "Ok"
                    });

                    fileStepProgress?.Report(new BatchFileStep(
                        i + 1, audioPaths.Count, audioPath, BatchFilePhase.Completed));
                }
                catch (OperationCanceledException)
                {
                    // The reader stopped the batch: keep what finished and let the
                    // caller see that the token, not a failure, ended it.
                    throw;
                }
                catch (Exception ex) when (ex is AudioLoadException or IOException
                                               or UnauthorizedAccessException or TranscribeException)
                {
                    results.Add(new BatchItemResult
                    {
                        AudioPath = audioPath,
                        Status = $"Error: {ex.Message}"
                    });

                    fileStepProgress?.Report(new BatchFileStep(
                        i + 1, audioPaths.Count, audioPath, BatchFilePhase.Failed));
                }

                progress?.Report(i + 1);
            }

            return results;
        }
        catch (OperationCanceledException)
        {
            return results;
        }
        finally
        {
            model.Dispose();
        }
    }

}
