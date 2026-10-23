using System.Runtime.CompilerServices;
using TranscribeCppSharp.Audio;
using TranscribeCppSharp.Interop;
using TranscribeCppSharp.Models;
using TranscribeCppSharp.SharedSource;
using TranscribeCppSharp.UI.Models;

namespace TranscribeCppSharp.UI.Services;

public class TranscriptionService : ITranscriptionService
{
    public async Task<TranscriptionResult> TranscribeAsync(
        string audioPath,
        string modelPath,
        TranscriptionOptions options,
        IProgress<double>? progress = null,
        IProgress<string>? liveText = null,
        IProgress<TranscriptionPhase>? phase = null,
        IProgress<TranscriptionStep>? stepProgress = null,
        CancellationToken cancellationToken = default)
    {
        bool chain = options.DiarizerModel is { Length: > 0 };

        // Optional pre-pass: a VAD model finds the speech intervals, and the ASR
        // run is planned over them instead of the whole file. It runs before the
        // ASR model is loaded, so only one model is in memory at a time. The phase
        // is indeterminate because the native VAD reports no fraction.
        IReadOnlyList<SpeechInterval>? speech = null;
        if (options.VadModel is { Length: > 0 })
        {
            phase?.Report(TranscriptionPhase.DetectingSpeech);
            speech = await PlanSpeechWindows(audioPath, options, cancellationToken).ConfigureAwait(false);
        }

        // Pass one covers 90% of the bar when a second pass follows, so the bar
        // reaching 100% means the whole run is done and not just the transcript.
        // The split is a phase marker, not a measured estimate: pass two reports no
        // finer progress because the diarizer runs the whole buffer in one call.
        IProgress<double>? passOneProgress = chain && progress is not null
            ? new ScaledProgress(progress, 0.9)
            : progress;

        // Pass one: the selected model, through the role it serves.
        TranscriptionResult result = await RunSelectedModel(
            audioPath, modelPath, options, passOneProgress, liveText, phase, stepProgress, speech, cancellationToken).ConfigureAwait(false);

        // Pass two: an optional separate diarizer, for a transcriber that cannot
        // attribute speakers itself. It runs after pass one, with that model already
        // disposed, so only one model is in memory at a time. A run that already has
        // speaker turns — the model diarized, or a DIARIZE-role model — is left alone.
        if (chain && result.SpeakerSegments.Count == 0)
        {
            phase?.Report(TranscriptionPhase.Diarizing);
            stepProgress?.Report(new TranscriptionStep(TranscriptionPhase.Diarizing, 0, 1));
            progress?.Report(0.95);

            List<SpeakerSegmentResult> turns = await RunDiarizer(
                audioPath, options.DiarizerModel!, options, cancellationToken).ConfigureAwait(false);

            result.SpeakerSegments = turns;
            // Word-level when the transcriber reported word timings: a segment can
            // span a speaker change, and the words say which half is whose.
            result.Segments = SpeakerAssignment.Apply(result.Segments, turns, result.Words);
            stepProgress?.Report(new TranscriptionStep(TranscriptionPhase.Diarizing, 1, 1));
            progress?.Report(1.0);
        }
        else
        {
            // Nothing chained, or the model attributed speakers itself: pass one was
            // the whole run.
            progress?.Report(1.0);
        }

        return result;
    }

    /// <summary>Maps a pass's 0–1 progress onto a sub-range of the whole run.</summary>
    private sealed class ScaledProgress(IProgress<double> inner, double factor) : IProgress<double>
    {
        public void Report(double value) => inner.Report(value * factor);
    }

    /// <summary>
    /// Runs the selected model through the role it serves.
    /// </summary>
    /// <remarks>
    /// transcribe.cpp 0.4 gives the model roles; this runs the role the model serves
    /// instead of always creating an ASR session (which throws UNSUPPORTED_ROLE on a
    /// diarizer).
    /// </remarks>
    private static async Task<TranscriptionResult> RunSelectedModel(
        string audioPath,
        string modelPath,
        TranscriptionOptions options,
        IProgress<double>? progress,
        IProgress<string>? liveText,
        IProgress<TranscriptionPhase>? phase,
        IProgress<TranscriptionStep>? stepProgress,
        IReadOnlyList<SpeechInterval>? speech,
        CancellationToken cancellationToken)
    {
        // Named before the work starts, not after: loading a multi-gigabyte model
        // and decoding a long file through ffmpeg are both slow, and a bar that
        // sits at zero with no label reads as a hang. Neither step reports a
        // fraction, so the tab shows them as indeterminate.
        phase?.Report(TranscriptionPhase.Preparing);
        var model = LoadModel(modelPath, options.BackendRequest, options.Device);

        try
        {
            phase?.Report(TranscriptionPhase.ReadingAudio);
            using var audioSource = AudioLoader.Open(audioPath);
            TranscribeRole roles = model.Roles;
            phase?.Report(roles.HasFlag(TranscribeRole.Diarize)
                ? TranscriptionPhase.Diarizing
                : TranscriptionPhase.Transcribing);

            return await System.Threading.Tasks.Task.Run(
                () => roles.HasFlag(TranscribeRole.Asr)
                    ? RunAsr(model, roles, options, audioSource, progress, liveText, stepProgress, speech, cancellationToken)
                    : RunNonAsr(model, roles, options, audioSource, progress, cancellationToken),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            model.Dispose();
        }
    }

    /// <summary>
    /// Runs a DIARIZE-role model over the whole file and returns its speaker turns.
    /// </summary>
    /// <remarks>
    /// The role is offline over a whole buffer — upstream has no streaming
    /// diarization — so the whole file is read, as the role requires.
    /// </remarks>
    private static async Task<List<SpeakerSegmentResult>> RunDiarizer(
        string audioPath,
        string diarizerPath,
        TranscriptionOptions options,
        CancellationToken cancellationToken)
    {
        return await System.Threading.Tasks.Task.Run(() =>
        {
            var model = LoadModel(diarizerPath, options.BackendRequest, options.Device);

            try
            {
                if (!model.Roles.HasFlag(TranscribeRole.Diarize))
                {
                    throw new InvalidOperationException(
                        $"{diarizerPath} does not serve the DIARIZE role, so it cannot be used as a diarizer.");
                }

                using var audioSource = AudioLoader.Open(audioPath);
                using var session = model.CreateDiarizeSession();
                int total = (int)Math.Min(audioSource.LengthSamples, int.MaxValue);

                return session.Run(audioSource.ReadWindow(0, total), ct: cancellationToken).ToList();
            }
            finally
            {
                model.Dispose();
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs the opt-in VAD model over the file and returns its speech intervals.
    /// </summary>
    /// <remarks>
    /// The intervals, not the windows, are returned: <see cref="RunAsr"/> replans
    /// them with the window it is about to use, so an automatic window reduction
    /// after an out-of-memory failure still applies to the speech-only timeline.
    /// The VAD model is disposed before the ASR model is loaded, so only one model
    /// is in memory at a time.
    /// </remarks>
    private static async Task<IReadOnlyList<SpeechInterval>> PlanSpeechWindows(
        string audioPath,
        TranscriptionOptions options,
        CancellationToken cancellationToken)
    {
        return await System.Threading.Tasks.Task.Run(() =>
        {
            string vadPath = ModelStore.Resolve(options.VadModel!, quant: null, TextWriter.Null);
            using var model = LoadModel(vadPath, options.BackendRequest, options.Device);

            if (!model.Roles.HasFlag(TranscribeRole.Vad))
            {
                throw new InvalidOperationException(
                    $"{options.VadModel} does not serve the VAD role, so it cannot be used to skip silence.");
            }

            using var audioSource = AudioLoader.Open(audioPath);
            using var session = model.CreateVadSession();
            VadWindowing.VadRunResult run = VadWindowing.Run(
                session, audioSource, options.WindowSeconds, cancellationToken: cancellationToken);

            var intervals = run.Segments
                .Select(segment => new SpeechInterval(segment.StartSample, segment.EndSample))
                .ToArray();

            // Fail before the ASR model is loaded when the VAD found only clips too
            // short to transcribe; the planner is the same one the run will use.
            int total = (int)Math.Min(audioSource.LengthSamples, int.MaxValue);
            if (SpeechWindows.Plan(intervals, total, options.WindowSeconds * 1000).Count == 0)
            {
                throw new InvalidOperationException(
                    "The VAD model found no usable speech in this audio; nothing was transcribed.");
            }

            return (IReadOnlyList<SpeechInterval>)intervals;
        }, cancellationToken).ConfigureAwait(false);
    }

    private static TranscriptionResult RunAsr(
        Model model,
        TranscribeRole roles,
        TranscriptionOptions options,
        PcmSource audioSource,
        IProgress<double>? progress,
        IProgress<string>? liveText,
        IProgress<TranscriptionStep>? stepProgress,
        IReadOnlyList<SpeechInterval>? speech,
        CancellationToken cancellationToken)
    {
        var memory = new MemorySampler(model);

        int requestedSeconds = options.WindowSeconds;
        int windowSeconds = requestedSeconds;
        var attempted = new List<int> { windowSeconds };

        try
        {
            while (true)
            {
                // A new attempt starts a new measurement; a retry must not carry
                // the failed attempt's low point into the result it eventually
                // reports for the smaller window.
                memory.Sample();

                // Per attempt, because a retry starts the audio over: keeping the
                // partial lists would duplicate the text already collected.
                var words = new List<WordResult>();
                var speakerSegments = new List<SpeakerSegmentResult>();
                var merger = new TranscriptMerger();
                string detectedLanguage = string.Empty;
                bool wasAborted = false;
                bool wasTruncated = false;
                Status stopStatus = Status.Ok;
                bool retry = false;

                using var session = CreateSession(model, options);

                // TryPlan takes milliseconds, not seconds: passing the option
                // straight through made every value below 1000 mean a window
                // shorter than the 1 s overlap, so planning failed and the run
                // sent the whole file to the model in one call. That is what the
                // Window setting must not do.
                int windowMs = (int)Math.Min((long)windowSeconds * 1000, int.MaxValue);

                int totalSamples = (int)Math.Min(audioSource.LengthSamples, int.MaxValue);
                IReadOnlyList<AudioWindow> windows;
                bool planned;
                if (speech is not null)
                {
                    // The VAD found the speech; plan the ASR windows over those
                    // intervals, with the same padding/merge rules the CLI uses.
                    // Replanned per attempt so an automatic window reduction still
                    // applies to the speech-only timeline.
                    windows = SpeechWindows.Plan(speech, totalSamples, windowMs);
                    planned = windows.Count > 0;
                    if (!planned)
                    {
                        throw new InvalidOperationException(
                            "The VAD model found no usable speech in this audio; nothing was transcribed.");
                    }
                }
                else
                {
                    // TryPlan fails only when the window cannot advance past the
                    // overlap. The control refuses values that small, so reaching
                    // the fallback means the audio is empty; one run over it is
                    // correct.
                    planned = WindowPlanner.TryPlan(totalSamples, windowMs, out windows, out _);
                }

                if (planned)
                {
                    // Announced before the first window so the UI can say "window 1 of N"
                    // while the first one runs, not only after it finishes. The window
                    // rides on the step so a retry can tell the reader what it tries now.
                    stepProgress?.Report(new TranscriptionStep(
                        TranscriptionPhase.Transcribing, 0, windows.Count, windowSeconds));

                    int windowIndex = 0;
                    foreach (var window in windows)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var samples = audioSource.ReadWindow(window.OffsetSamples, window.LengthSamples);

                        Transcript transcript;
                        try
                        {
                            transcript = RunWindow(
                                session, samples, options, memory,
                                windowNumber: windowIndex + 1, windowCount: windows.Count,
                                cancellationToken: cancellationToken);
                        }
                        catch (TranscribeException ex) when (
                            CanLowerAfterFailure(ex, options, attempted, windowIndex))
                        {
                            int smaller = WindowAdjustment.Smaller(windowSeconds)!.Value;
                            windowSeconds = smaller;
                            attempted.Add(smaller);
                            memory.Reset();
                            retry = true;
                            break;
                        }

                        Collect(merger, window, transcript, words, speakerSegments);

                        detectedLanguage = transcript.DetectedLanguage;
                        wasAborted = transcript.WasAborted;
                        // Sticky: an earlier window that stopped early still makes
                        // the merged transcript incomplete, even if this one is Ok.
                        wasTruncated |= transcript.WasTruncated;
                        if (transcript.StatusCode != Status.Ok)
                        {
                            stopStatus = transcript.StatusCode;
                        }

                        windowIndex++;
                        progress?.Report((double)windowIndex / windows.Count);
                        stepProgress?.Report(new TranscriptionStep(
                            TranscriptionPhase.Transcribing, windowIndex, windows.Count, windowSeconds));

                        // One update per finished window: the offline run has no finer
                        // callback, so this is not word-by-word, and the window setting is
                        // what decides how often it changes.
                        liveText?.Report(merger.BuildText());

                        // Best effort, between windows: the peak inside a window is not
                        // observable without polling from another thread, so this is a
                        // lower bound on the run's footprint, not a peak measurement.
                        memory.Sample();
                    }
                }
                else
                {
                    stepProgress?.Report(new TranscriptionStep(
                        TranscriptionPhase.Transcribing, 0, 1, windowSeconds));
                    var samples = audioSource.ReadWindow(0, (int)Math.Min(audioSource.LengthSamples, int.MaxValue));

                    Transcript? transcript = null;
                    try
                    {
                        transcript = RunWindow(
                            session, samples, options, memory,
                            windowNumber: 1, windowCount: 1, cancellationToken: cancellationToken);
                    }
                    catch (TranscribeException ex) when (
                        CanLowerAfterFailure(ex, options, attempted, windowIndex: 0))
                    {
                        int smaller = WindowAdjustment.Smaller(windowSeconds)!.Value;
                        windowSeconds = smaller;
                        attempted.Add(smaller);
                        memory.Reset();
                        retry = true;
                    }

                    if (!retry)
                    {
                        var window = new AudioWindow(0, (int)Math.Min(audioSource.LengthSamples, int.MaxValue));
                        Collect(merger, window, transcript!, words, speakerSegments);

                        detectedLanguage = transcript!.DetectedLanguage;
                        wasAborted = transcript.WasAborted;
                        wasTruncated |= transcript.WasTruncated;
                        if (transcript.StatusCode != Status.Ok)
                        {
                            stopStatus = transcript.StatusCode;
                        }

                        progress?.Report(1.0);
                        stepProgress?.Report(new TranscriptionStep(
                            TranscriptionPhase.Transcribing, 1, 1, windowSeconds));
                        memory.Sample();
                    }
                }

                if (retry)
                {
                    continue;
                }

                return new TranscriptionResult
                {
                    Roles = roles,
                    FullText = merger.BuildText(),
                    Segments = merger.Segments.ToList(),
                    Words = words,
                    SpeakerSegments = speakerSegments,
                    DetectedLanguage = detectedLanguage,
                    WasAborted = wasAborted,
                    WasTruncated = wasTruncated,
                    StopStatus = stopStatus,
                    Duration = TimeSpan.FromSeconds(audioSource.LengthSamples / (double)WindowPlanner.SampleRate),
                    EffectiveWindowSeconds = windowSeconds,
                    RequestedWindowSeconds = windowSeconds == requestedSeconds ? null : requestedSeconds,
                    Memory = memory.Observation
                };
            }
        }
        catch (TranscribeException ex) when (attempted.Count > 1)
        {
            // The last attempt's error is the one the caller sees; say which
            // windows were already tried so the report is not a dead end.
            ex.Data["window attempts"] = string.Join(", ", attempted) + " s";
            throw;
        }
    }

    /// <summary>
    /// Whether an out-of-memory failure may be retried at a smaller window.
    /// </summary>
    /// <remarks>
    /// Only the first window may lower the window: after one window has produced
    /// text, restarting the audio would duplicate it. The failure must be the
    /// native out-of-memory status, the caller must have allowed the adjustment,
    /// and the halving must still be above
    /// <see cref="WindowAdjustment.MinimumSeconds"/>.
    /// </remarks>
    private static bool CanLowerAfterFailure(
        TranscribeException ex,
        TranscriptionOptions options,
        List<int> attempted,
        int windowIndex)
        => windowIndex == 0
        && ex.StatusCode == Status.ErrOom
        && options.AutoAdjustWindow
        && attempted.Count < WindowAdjustment.MaximumAttempts
        && WindowAdjustment.Smaller(attempted[^1]) is not null;

    /// <summary>
    /// Runs one window and, on a native out-of-memory failure, attaches what only
    /// the service knows — the actual device's live memory and the session limits —
    /// so the error report can name them instead of the requested policy.
    /// </summary>
    private static Transcript RunWindow(
        Session session,
        float[] samples,
        TranscriptionOptions options,
        MemorySampler memory,
        int windowNumber,
        int windowCount,
        CancellationToken cancellationToken)
    {
        try
        {
            return session.Run(samples, builder => Configure(builder, options), cancellationToken);
        }
        catch (TranscribeException ex) when (ex.StatusCode == Status.ErrOom)
        {
            memory.Sample();
            ex.Data["memory"] = memory.Observation;
            ex.Data["window"] = string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"{windowNumber} of {windowCount}");

            try
            {
                ex.Data["session limits"] = session.GetLimits();
            }
            catch (TranscribeException)
            {
                // The limits are a diagnostic, not a requirement for reporting.
            }

            throw;
        }
    }

    /// <summary>
    /// Runs a model that does not serve ASR, through the session its role names.
    /// The result is a readable report in <see cref="TranscriptionResult.FullText"/>
    /// (and speaker rows for a diarizer); the audio is read whole, because these
    /// roles have no windowing contract.
    /// </summary>
    private static TranscriptionResult RunNonAsr(
        Model model,
        TranscribeRole roles,
        TranscriptionOptions options,
        PcmSource audioSource,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        int sampleRate = WindowPlanner.SampleRate;
        int total = (int)Math.Min(audioSource.LengthSamples, int.MaxValue);

        if (roles.HasFlag(TranscribeRole.Diarize))
        {
            using var session = model.CreateDiarizeSession();
            var segments = session.Run(audioSource.ReadWindow(0, total), ct: cancellationToken);

            var lines = new List<string> { $"Diarization: {segments.Count} speaker segments." };
            lines.AddRange(segments.Select(s =>
                $"[{Fmt(s.Start)} -> {Fmt(s.End)}] Speaker {s.SpeakerId}"));

            return new TranscriptionResult
            {
                Roles = roles,
                Duration = TimeSpan.FromSeconds(total / (double)sampleRate),
                FullText = string.Join('\n', lines),
                SpeakerSegments = segments.ToList(),
            };
        }

        if (roles.HasFlag(TranscribeRole.Langid))
        {
            using var session = model.CreateLangIdSession();
            var info = model.GetLangIdInfo();

            // The model has a maximum clip; identify from the head of the audio
            // when the file is longer.
            long max = info.MaxAudioMs > 0 ? (long)info.MaxAudioMs * sampleRate / 1000 : total;
            var result = session.Run(
                audioSource.ReadWindow(0, (int)Math.Min(total, max)),
                p =>
                {
                    if (options.AllowedLanguages is { Length: > 0 })
                    {
                        p.WithAllowed(options.AllowedLanguages);
                    }
                },
                cancellationToken);

            var lines = new List<string>
            {
                $"Language identification: {result.Candidates.Count} candidates, allowed mass {result.AllowedMass:0.000}.",
            };
            lines.AddRange(result.Candidates.Select(c => $"{c.Code,-8} {c.Probability:0.000}  {c.Name}"));

            return new TranscriptionResult
            {
                Roles = roles,
                Duration = TimeSpan.FromSeconds(total / (double)sampleRate),
                DetectedLanguage = result.Candidates.Count > 0 ? result.Candidates[0].Code : string.Empty,
                FullText = string.Join('\n', lines),
                LangIdCandidates = result.Candidates.ToList(),
            };
        }

        if (roles.HasFlag(TranscribeRole.Vad))
        {
            using var session = model.CreateVadSession();
            var info = model.GetVadInfo();

            // Silero VAD is chunked-unbounded upstream: score it one window at a
            // time and merge the intervals instead of holding the whole file.
            var result = VadWindowing.Run(session, audioSource, options.WindowSeconds, progress, cancellationToken);

            var lines = new List<string>
            {
                $"Voice activity: {result.Segments.Count} speech segments, {result.FrameCount} frames.",
            };
            lines.AddRange(result.Segments.Select(s =>
            {
                double start = s.StartSample * 1000.0 / info.SampleRate;
                double end = s.EndSample * 1000.0 / info.SampleRate;
                return $"[{Fmt(TimeSpan.FromMilliseconds(start))} -> {Fmt(TimeSpan.FromMilliseconds(end))}] speech";
            }));

            return new TranscriptionResult
            {
                Roles = roles,
                Duration = TimeSpan.FromSeconds(total / (double)sampleRate),
                FullText = string.Join('\n', lines),
                VadSegments = result.Segments.ToList(),
            };
        }

        throw new InvalidOperationException(
            $"this model serves {roles}; the tab runs ASR, diarization, language identification or voice activity detection.");
    }

    private static string Fmt(TimeSpan t) => $"{t.Hours:00}:{t.Minutes:00}:{t.Seconds:00}.{t.Milliseconds / 100}";

    public async IAsyncEnumerable<Models.StreamUpdate> StreamTranscribeAsync(
        string modelPath,
        StreamOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var model = LoadModel(modelPath, options.BackendRequest, options.Device);

        try
        {
            using var session = model.CreateSession();
            using var stream = session.CreateStream();
            using var microphone = new MicrophoneCapture();

            // One utterance may be closed and a new one begun every
            // options.UtteranceSeconds of audio; zero leaves a single utterance
            // for the whole session. See UtteranceRotation for why.
            var rotation = new UtteranceRotation(options.UtteranceSeconds);

            await System.Threading.Tasks.Task.Run(() => stream.Begin(null, streamConfig => Configure(streamConfig, options)), cancellationToken);

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
                    yield return Compose(rotation, text, isFinal: false);

                    if (samples.Length > 0 && rotation.AddFed(samples.Length))
                    {
                        await RotateUtterance(stream, rotation, options, cancellationToken);
                    }

                    await System.Threading.Tasks.Task.Delay(50, cancellationToken);
                }

                var finalText = stream.GetCurrentText();
                yield return Compose(rotation, finalText, isFinal: true);

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

    /// <summary>
    /// Closes the current utterance and starts a new one on the same stream.
    /// </summary>
    /// <remarks>
    /// Finalize commits the utterance's remaining tentative text, reset clears
    /// the family's per-utterance state (the part that grows with the audio) and
    /// begin starts the next utterance reusing the stream's buffers. All three
    /// run off the UI thread, like <c>Feed</c>. The committed text is read
    /// between finalize and reset, while it is still there, and is then held by
    /// <paramref name="rotation"/> to prepend to the new utterance.
    /// </remarks>
    private static async System.Threading.Tasks.Task RotateUtterance(
        StreamSession stream,
        UtteranceRotation rotation,
        StreamOptions options,
        CancellationToken cancellationToken)
    {
        await System.Threading.Tasks.Task.Run(
            () =>
            {
                stream.Complete();
                rotation.Close(stream.GetCurrentText().CommittedText);
                stream.Reset();
                stream.Begin(null, streamConfig => Configure(streamConfig, options));
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// One update, with the text of the utterances already closed prepended so
    /// the display stays a single growing transcript across a rotation.
    /// </summary>
    private static Models.StreamUpdate Compose(
        UtteranceRotation rotation,
        StreamTextResult text,
        bool isFinal)
    {
        string closed = rotation.ClosedCommittedText;
        return new Models.StreamUpdate
        {
            FullText = closed + text.FullText,
            CommittedText = closed + text.CommittedText,
            TentativeText = text.TentativeText,
            IsFinal = isFinal,
        };
    }

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

    /// <summary>
    /// Runs at most one window of the audio with the selected settings and reports
    /// whether it held, without producing a transcript.
    /// </summary>
    /// <remarks>
    /// The allocation that fails in a real run happens when the native call builds
    /// the graph for its input; running the same window is what exercises it. It
    /// does not test later windows, the diarizer, or the decoder's output length —
    /// the button's tooltip and the page say so.
    /// </remarks>
    public async Task<MemoryProbeResult> ProbeMemoryAsync(
        string audioPath,
        string modelPath,
        TranscriptionOptions options,
        CancellationToken cancellationToken = default)
    {
        return await System.Threading.Tasks.Task.Run(() =>
        {
            Model model = LoadModel(modelPath, options.BackendRequest, options.Device);

            try
            {
                using var audioSource = AudioLoader.Open(audioPath);
                using var session = CreateSession(model, options);

                long windowSamples = Math.Max(1, options.WindowSeconds) * (long)WindowPlanner.SampleRate;
                int count = (int)Math.Min(audioSource.LengthSamples, Math.Min(windowSamples, int.MaxValue));
                if (count <= 0)
                {
                    return new MemoryProbeResult(false, false, null, "The audio is empty.");
                }

                var samples = audioSource.ReadWindow(0, count);
                var memory = new MemorySampler(model);
                memory.Sample();

                try
                {
                    session.Run(samples, builder => Configure(builder, options), cancellationToken);
                    memory.Sample();
                    return new MemoryProbeResult(true, false, memory.Observation, null);
                }
                catch (TranscribeException ex) when (ex.StatusCode == Status.ErrOom)
                {
                    memory.Sample();
                    return new MemoryProbeResult(false, true, memory.Observation, ex.Message);
                }
            }
            finally
            {
                model.Dispose();
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Resolves <paramref name="modelArgument"/> to a local file and loads it.
    /// </summary>
    /// <remarks>
    /// The argument may be a curated alias, a HuggingFace spec or a plain path,
    /// exactly as for the CLI: the view models bind an alias from the model
    /// list, and Model.Load only accepts a real file. Resolution blocks while it
    /// downloads a first-use model, so it runs on a worker thread.
    /// </remarks>
    private static Model LoadModel(
        string modelArgument,
        BackendRequest? backend,
        BackendDevice? device)
    {
        string resolved = ModelStore.Resolve(modelArgument, null, TextWriter.Null);
        EnforceWeightsFit(resolved, backend, device);
        return Model.Load(resolved, builder =>
        {
            if (backend != null)
                builder.WithBackend(backend.Value);
            if (device != null)
                builder.WithDevice(device);
        });
    }

    /// <summary>
    /// Refuses a model whose weight file alone cannot fit in the free memory of
    /// the device the request resolves to.
    /// </summary>
    /// <remarks>
    /// Only the certain case is refused: the compute graph's size is not exposed by
    /// the native API, so a run that fits its weights can still run out of memory
    /// later. When the device or its free figure is unknown, this says nothing and
    /// lets the load report the real failure.
    /// </remarks>
    private static void EnforceWeightsFit(string modelPath, BackendRequest? backend, BackendDevice? device)
    {
        long modelBytes;
        try
        {
            modelBytes = new FileInfo(modelPath).Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        IReadOnlyList<BackendDevice> devices;
        try
        {
            devices = Backends.EnumerateDevices();
        }
        catch (Exception ex) when (ex is TranscribeException or DllNotFoundException or EntryPointNotFoundException)
        {
            // Without a live device list there is nothing factual to compare.
            return;
        }

        // Prefer the live entry for a pinned device: its free figure is a snapshot.
        BackendDevice? pinned = device is null
            ? null
            : devices.FirstOrDefault(d => d.Handle == device.Handle && d.Handle != IntPtr.Zero)
                ?? devices.FirstOrDefault(d => !string.IsNullOrEmpty(d.DeviceId) && d.DeviceId == device.DeviceId)
                ?? device;

        BackendDevice? target = MemoryPreflight.Target(devices, backend, pinned);
        if (!MemoryPreflight.WeightsFit(modelBytes, target))
        {
            throw new InvalidOperationException(
                $"The model file is {MemoryPreflight.FormatBytes((ulong)modelBytes)}; "
                + $"{target!.Name} reports {MemoryPreflight.FormatBytes(target.MemoryFree)} free of "
                + $"{MemoryPreflight.FormatBytes(target.MemoryTotal)}. The weights alone do not fit on that device. "
                + "Pin a device with more free memory in Settings, or choose a smaller quantization.");
        }
    }

    /// <summary>
    /// Creates a session and applies the session-level tuning.
    /// </summary>
    /// <remarks>
    /// Backend and device are not set here: they belong to the model, and
    /// <see cref="LoadModel"/> applies them. <see cref="StreamOptions"/> has no
    /// session tuning at all, so streaming goes through the plain
    /// <c>CreateSession</c> overload.
    /// </remarks>
    private static Session CreateSession(
        Model model,
        TranscriptionOptions options)
        => model.CreateSession(sessionParams =>
        {
            if (options.Threads.HasValue)
                sessionParams.WithThreads(options.Threads.Value);
            if (options.KvType.HasValue)
                sessionParams.WithKvType(options.KvType.Value);
            if (options.ContextSize.HasValue)
                sessionParams.WithContextSize(options.ContextSize.Value);
        });

    /// <summary>
    /// Applies every set field of <paramref name="options"/> to one run.
    /// </summary>
    /// <remarks>
    /// The Whisper extension is attached only when at least one of its fields is
    /// set. The condition covers all eleven, not just the prompt and the
    /// temperature: a run configured with, say, only a seed used to build no
    /// extension at all and silently dropped it.
    /// </remarks>
    private static void Configure(RunParamsBuilder builder, TranscriptionOptions options)
    {
        builder
            .WithTimestamps(options.TimestampKind)
            .WithDiarize(options.DiarizeMode);

        if (options.Language != "auto")
            builder.WithLanguage(options.Language);

        if (options.PncMode.HasValue)
            builder.WithPnc(options.PncMode.Value);
        if (options.ItnMode.HasValue)
            builder.WithItn(options.ItnMode.Value);
        if (options.Task.HasValue)
            builder.WithTask(options.Task.Value);
        if (options.TargetLanguage != null)
            builder.WithTargetLanguage(options.TargetLanguage);
        if (options.SpecKDrafts.HasValue)
            builder.WithSpecKDrafts(options.SpecKDrafts.Value);
        if (options.KeepSpecialTags.HasValue)
            builder.WithKeepSpecialTags(options.KeepSpecialTags.Value);
        if (options.Vocabulary is { Length: > 0 })
            builder.WithVocabulary(options.Vocabulary);

        if (!HasWhisperExtension(options))
            return;

        var whisperExt = new WhisperExtBuilder();
        if (options.WhisperInitialPrompt != null)
            whisperExt.WithInitialPrompt(options.WhisperInitialPrompt);
        if (options.WhisperTemperature.HasValue)
            whisperExt.WithTemperature(options.WhisperTemperature.Value);
        if (options.WhisperTemperatureInc.HasValue)
            whisperExt.WithTemperatureInc(options.WhisperTemperatureInc.Value);
        if (options.WhisperCompressionRatioThold.HasValue)
            whisperExt.WithCompressionRatioThold(options.WhisperCompressionRatioThold.Value);
        if (options.WhisperLogprobThold.HasValue)
            whisperExt.WithLogprobThold(options.WhisperLogprobThold.Value);
        if (options.WhisperNoSpeechThold.HasValue)
            whisperExt.WithNoSpeechThold(options.WhisperNoSpeechThold.Value);
        if (options.WhisperSeed.HasValue)
            whisperExt.WithSeed(options.WhisperSeed.Value);
        if (options.WhisperMaxInitialTimestamp.HasValue)
            whisperExt.WithMaxInitialTimestamp(options.WhisperMaxInitialTimestamp.Value);
        if (options.WhisperConditionOnPrevTokens)
            whisperExt.WithConditionOnPrevTokens(options.WhisperConditionOnPrevTokens);
        if (options.WhisperMaxPrevContextTokens.HasValue)
            whisperExt.WithMaxPrevContextTokens(options.WhisperMaxPrevContextTokens.Value);

        builder.WithWhisperExt(whisperExt);
    }

    private static bool HasWhisperExtension(TranscriptionOptions options)
        => options.WhisperInitialPrompt != null
        || options.WhisperTemperature.HasValue
        || options.WhisperTemperatureInc.HasValue
        || options.WhisperCompressionRatioThold.HasValue
        || options.WhisperLogprobThold.HasValue
        || options.WhisperNoSpeechThold.HasValue
        || options.WhisperSeed.HasValue
        || options.WhisperMaxInitialTimestamp.HasValue
        || options.WhisperConditionOnPrevTokens
        || options.WhisperMaxPrevContextTokens.HasValue;

    private static void Configure(StreamParamsBuilder builder, StreamOptions options)
    {
        builder.WithCommitPolicy(options.CommitPolicy);

        if (options.StablePrefixAgreement.HasValue)
            builder.WithStablePrefixAgreement(options.StablePrefixAgreement.Value);

        if (options.MoonshineMinDecodeIntervalMs.HasValue)
        {
            var moonshineExt = new MoonshineExtBuilder();
            moonshineExt.WithMinDecodeIntervalMs(options.MoonshineMinDecodeIntervalMs.Value);
            builder.WithMoonshineExt(moonshineExt);
        }

        if (options.ParakeetAttContextRight.HasValue)
        {
            var parakeetExt = new ParakeetStreamExtBuilder();
            parakeetExt.WithAttContextRight(options.ParakeetAttContextRight.Value);
            builder.WithParakeetStreamExt(parakeetExt);
        }

        if (options.ParakeetLeftMs.HasValue || options.ParakeetChunkMs.HasValue || options.ParakeetRightMs.HasValue)
        {
            var parakeetBuffered = new ParakeetBufferedStreamExtBuilder();
            if (options.ParakeetLeftMs.HasValue)
                parakeetBuffered.WithLeftMs(options.ParakeetLeftMs.Value);
            if (options.ParakeetChunkMs.HasValue)
                parakeetBuffered.WithChunkMs(options.ParakeetChunkMs.Value);
            if (options.ParakeetRightMs.HasValue)
                parakeetBuffered.WithRightMs(options.ParakeetRightMs.Value);
            builder.WithParakeetBufferedStreamExt(parakeetBuffered);
        }

        if (options.VoxtralNumDelayTokens.HasValue || options.VoxtralMinDecodeIntervalMs.HasValue)
        {
            var voxtralExt = new VoxtralExtBuilder();
            if (options.VoxtralNumDelayTokens.HasValue)
                voxtralExt.WithNumDelayTokens(options.VoxtralNumDelayTokens.Value);
            if (options.VoxtralMinDecodeIntervalMs.HasValue)
                voxtralExt.WithMinDecodeIntervalMs(options.VoxtralMinDecodeIntervalMs.Value);
            builder.WithVoxtralExt(voxtralExt);
        }
    }

    /// <summary>
    /// Folds one window's transcript into the run.
    /// </summary>
    /// <remarks>
    /// The merger keeps segments and drops the ones the previous window already
    /// covered. Words and speaker rows are appended unfiltered and only shifted:
    /// the native layer emits them per window with no seam marker, so there is
    /// nothing here that can tell a repeated word from a new one, and a caller
    /// wanting gapless words has to reconcile them itself.
    /// </remarks>
    private static void Collect(
        TranscriptMerger merger,
        AudioWindow window,
        Transcript transcript,
        List<WordResult> words,
        List<SpeakerSegmentResult> speakerSegments)
    {
        merger.Add(window, transcript);

        foreach (var word in transcript.Words)
        {
            words.Add(TranscriptMerger.Shift(window, word));
        }

        foreach (var speaker in transcript.SpeakerSegments)
        {
            speakerSegments.Add(TranscriptMerger.Shift(window, speaker));
        }
    }

    /// <summary>
    /// Records the lowest free figure the model's device reports while a run is in
    /// progress. <see cref="Model.Device"/> queries the native device live, so a
    /// sample costs one interop call per window.
    /// </summary>
    /// <remarks>
    /// This is a lower bound, not a peak: the figure is read between windows, while
    /// the per-window compute buffer is not visible from here. It is still the
    /// number the next run is compared against, and it is a measurement rather than
    /// an estimate. A device that reports no free figure yields no observation.
    /// </remarks>
    private sealed class MemorySampler(Model model)
    {
        private string _deviceName = string.Empty;
        private ulong _total;
        private ulong _minimumFree = ulong.MaxValue;
        private bool _observed;

        public MemoryObservation? Observation => _observed
            ? new MemoryObservation(_deviceName, _total, _minimumFree, DateTimeOffset.Now)
            : null;

        /// <summary>
        /// Starts a new measurement. Used before a retry at a smaller window: the
        /// failed attempt's low point belongs to its error report, not to the run
        /// that eventually succeeds.
        /// </summary>
        public void Reset()
        {
            _deviceName = string.Empty;
            _total = 0;
            _minimumFree = ulong.MaxValue;
            _observed = false;
        }

        public void Sample()
        {
            try
            {
                BackendDevice? device = model.Device;
                if (device is null || device.MemoryFree == 0)
                {
                    return;
                }

                if (!_observed)
                {
                    _deviceName = device.Name;
                    _total = device.MemoryTotal;
                    _minimumFree = device.MemoryFree;
                    _observed = true;
                    return;
                }

                _total = Math.Max(_total, device.MemoryTotal);
                _minimumFree = Math.Min(_minimumFree, device.MemoryFree);
            }
            catch (Exception ex) when (ex is TranscribeException or ObjectDisposedException
                                          or DllNotFoundException or EntryPointNotFoundException)
            {
                // A measurement must never fail a run; without it the UI simply
                // has nothing to compare next time.
            }
        }
    }
}
