using System.Runtime.CompilerServices;
using TranscribeCppSharp;
using TranscribeCppSharp.Interop;
using TranscribeCppSharp.Ui.Models;

namespace TranscribeCppSharp.Ui.Services;

public class TranscriptionService : ITranscriptionService
{
    public async System.Threading.Tasks.Task<TranscriptionResult> TranscribeAsync(
        string audioPath,
        string modelPath,
        TranscriptionOptions options,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var model = Model.Load(modelPath, builder =>
        {
            if (options.BackendRequest != null)
                builder.WithBackend(options.BackendRequest.Value);
            if (options.Device != null)
                builder.WithDevice(options.Device);
        });

        try
        {
            using var session = model.CreateSession();
            var pcm = await File.ReadAllBytesAsync(audioPath, cancellationToken);

            // Convert bytes to float samples (16-bit PCM to float)
            var samples = new float[pcm.Length / 2];
            for (int i = 0; i < samples.Length; i++)
            {
                short sample = BitConverter.ToInt16(pcm, i * 2);
                samples[i] = sample / 32768f;
            }

            var result = await System.Threading.Tasks.Task.Run(() =>
            {
                return session.Run(samples, builder =>
                {
                    builder
                        .WithLanguage(options.Language)
                        .WithTimestamps(options.TimestampKind)
                        .WithDiarize(options.DiarizeMode);

                    if (options.PncMode.HasValue)
                        builder.WithPnc(options.PncMode.Value);
                    if (options.ItnMode.HasValue)
                        builder.WithItn(options.ItnMode.Value);
                }, cancellationToken);
            }, cancellationToken);

            progress?.Report(1.0);

            return new TranscriptionResult
            {
                FullText = result.FullText,
                Segments = result.Segments.ToList(),
                Words = result.Words.ToList(),
                SpeakerSegments = result.SpeakerSegments.ToList(),
                DetectedLanguage = result.DetectedLanguage,
                WasAborted = result.WasAborted,
                WasTruncated = result.WasTruncated
            };
        }
        finally
        {
            model.Dispose();
        }
    }

    public async IAsyncEnumerable<TranscribeCppSharp.Ui.Models.StreamUpdate> StreamTranscribeAsync(
        string modelPath,
        StreamOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var model = Model.Load(modelPath, builder =>
        {
            if (options.BackendRequest != null)
                builder.WithBackend(options.BackendRequest.Value);
            if (options.Device != null)
                builder.WithDevice(options.Device);
        });

        try
        {
            using var session = model.CreateSession();
            using var stream = session.CreateStream();

            // This is a simplified streaming implementation
            // Real implementation would feed audio chunks from microphone
            await System.Threading.Tasks.Task.Run(() => stream.Begin(), cancellationToken);

            // Placeholder: yield periodic updates
            while (!cancellationToken.IsCancellationRequested)
            {
                await System.Threading.Tasks.Task.Delay(100, cancellationToken);
                var text = stream.GetCurrentText();
                yield return new TranscribeCppSharp.Ui.Models.StreamUpdate
                {
                    FullText = text.FullText,
                    CommittedText = text.CommittedText,
                    TentativeText = text.TentativeText,
                    IsFinal = false
                };
            }

            stream.Complete();
        }
        finally
        {
            model.Dispose();
        }
    }

    public async System.Threading.Tasks.Task<List<BatchItemResult>> BatchTranscribeAsync(
        IReadOnlyList<string> audioPaths,
        string modelPath,
        TranscriptionOptions options,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var model = Model.Load(modelPath, builder =>
        {
            if (options.BackendRequest != null)
                builder.WithBackend(options.BackendRequest.Value);
            if (options.Device != null)
                builder.WithDevice(options.Device);
        });

        try
        {
            using var session = model.CreateSession();
            var results = new List<BatchItemResult>();

            for (int i = 0; i < audioPaths.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var audioPath = audioPaths[i];
                var pcm = await File.ReadAllBytesAsync(audioPath, cancellationToken);

                var samples = new float[pcm.Length / 2];
                for (int j = 0; j < samples.Length; j++)
                {
                    short sample = BitConverter.ToInt16(pcm, j * 2);
                    samples[j] = sample / 32768f;
                }

                var result = await System.Threading.Tasks.Task.Run(() =>
                {
                    return session.Run(samples, builder =>
                    {
                        builder
                            .WithLanguage(options.Language)
                            .WithTimestamps(options.TimestampKind)
                            .WithDiarize(options.DiarizeMode);
                    }, cancellationToken);
                }, cancellationToken);

                results.Add(new BatchItemResult
                {
                    AudioPath = audioPath,
                    FullText = result.FullText,
                    DetectedLanguage = result.DetectedLanguage,
                    Status = result.WasAborted ? "Aborted" : "Ok"
                });

                progress?.Report(i + 1);
            }

            return results;
        }
        finally
        {
            model.Dispose();
        }
    }
}
