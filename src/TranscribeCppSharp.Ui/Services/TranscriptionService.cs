using System.Runtime.CompilerServices;
using TranscribeCppSharp;
using TranscribeCppSharp.Audio;
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
            using var audioSource = AudioLoader.Open(audioPath);

            var result = await System.Threading.Tasks.Task.Run(() =>
            {
                var allSegments = new List<TranscribeCppSharp.SegmentResult>();
                var allWords = new List<TranscribeCppSharp.WordResult>();
                var allSpeakerSegments = new List<TranscribeCppSharp.SpeakerSegmentResult>();
                string fullText = string.Empty;
                string detectedLanguage = string.Empty;
                bool wasAborted = false;
                bool wasTruncated = false;

                if (WindowPlanner.TryPlan((int)Math.Min(audioSource.LengthSamples, int.MaxValue), options.WindowSeconds, out var windows, out var error))
                {
                    int windowIndex = 0;
                    foreach (var window in windows)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var samples = audioSource.ReadWindow(window.OffsetSamples, window.LengthSamples);
                        var windowResult = session.Run(samples, builder =>
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

                        // Deduplicate overlap: skip segments that start before the window's start
                        long windowStartMs = window.StartMs;
                        foreach (var seg in windowResult.Segments)
                        {
                            if (seg.Start.TotalMilliseconds >= windowStartMs)
                            {
                                allSegments.Add(seg);
                            }
                        }
                        allWords.AddRange(windowResult.Words);
                        allSpeakerSegments.AddRange(windowResult.SpeakerSegments);
                        fullText = windowResult.FullText;
                        detectedLanguage = windowResult.DetectedLanguage;
                        wasAborted = windowResult.WasAborted;
                        wasTruncated = windowResult.WasTruncated;

                        windowIndex++;
                        progress?.Report((double)windowIndex / windows.Count);
                    }
                }
                else
                {
                    // Window too small or audio empty: transcribe whole file at once
                    var samples = audioSource.ReadWindow(0, (int)Math.Min(audioSource.LengthSamples, int.MaxValue));
                    var wholeResult = session.Run(samples, builder =>
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

                    allSegments.AddRange(wholeResult.Segments);
                    allWords.AddRange(wholeResult.Words);
                    allSpeakerSegments.AddRange(wholeResult.SpeakerSegments);
                    fullText = wholeResult.FullText;
                    detectedLanguage = wholeResult.DetectedLanguage;
                    wasAborted = wholeResult.WasAborted;
                    wasTruncated = wholeResult.WasTruncated;
                    progress?.Report(1.0);
                }

                return new TranscriptionResult
                {
                    FullText = fullText,
                    Segments = allSegments,
                    Words = allWords,
                    SpeakerSegments = allSpeakerSegments,
                    DetectedLanguage = detectedLanguage,
                    WasAborted = wasAborted,
                    WasTruncated = wasTruncated
                };
            }, cancellationToken);

            return result;
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
            using var microphone = new MicrophoneCapture();

            await System.Threading.Tasks.Task.Run(() => stream.Begin(null, streamConfig =>
            {
                streamConfig.WithCommitPolicy(options.CommitPolicy);
            }), cancellationToken);

            microphone.Start();

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var samples = microphone.ReadAvailable();
                    if (samples.Length > 0)
                    {
                        await System.Threading.Tasks.Task.Run(() => stream.Feed(samples), cancellationToken);
                    }

                    var text = stream.GetCurrentText();
                    yield return new TranscribeCppSharp.Ui.Models.StreamUpdate
                    {
                        FullText = text.FullText,
                        CommittedText = text.CommittedText,
                        TentativeText = text.TentativeText,
                        IsFinal = false
                    };

                    await System.Threading.Tasks.Task.Delay(50, cancellationToken);
                }

                var finalText = stream.GetCurrentText();
                yield return new TranscribeCppSharp.Ui.Models.StreamUpdate
                {
                    FullText = finalText.FullText,
                    CommittedText = finalText.CommittedText,
                    TentativeText = finalText.TentativeText,
                    IsFinal = true
                };

                stream.Complete();
            }
            finally
            {
                microphone.Stop();
            }
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
                using var audioSource = AudioLoader.Open(audioPath);

                var result = await System.Threading.Tasks.Task.Run(() =>
                {
                    var samples = audioSource.ReadWindow(0, (int)Math.Min(audioSource.LengthSamples, int.MaxValue));
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
