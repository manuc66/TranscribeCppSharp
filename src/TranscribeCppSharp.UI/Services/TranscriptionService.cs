using System.Runtime.CompilerServices;
using TranscribeCppSharp.Audio;
using TranscribeCppSharp.Interop;
using TranscribeCppSharp.Models;
using TranscribeCppSharp.SharedSource;
using TranscribeCppSharp.UI.Models;

namespace TranscribeCppSharp.UI.Services;

public partial class TranscriptionService : ITranscriptionService
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
            // The token reaches the download too: cancelling a run that is still
            // fetching the VAD model should stop the transfer, not let it finish
            // before the cancellation is noticed.
            string vadPath = ModelStore.Resolve(options.VadModel!, quant: null, TextWriter.Null, cancellationToken: cancellationToken);
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


}
