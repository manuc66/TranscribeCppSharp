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
        CancellationToken cancellationToken = default)
    {
        var model = LoadModel(modelPath, options.BackendRequest, options.Device);

        try
        {
            using var audioSource = AudioLoader.Open(audioPath);

            // transcribe.cpp 0.4 gives the model roles; this tab runs the role the
            // model serves instead of always creating an ASR session (which throws
            // UNSUPPORTED_ROLE on a diarizer).
            TranscribeRole roles = model.Roles;

            return await System.Threading.Tasks.Task.Run(
                () => roles.HasFlag(TranscribeRole.Asr)
                    ? RunAsr(model, roles, options, audioSource, progress, cancellationToken)
                    : RunNonAsr(model, roles, options, audioSource, progress, cancellationToken),
                cancellationToken);
        }
        finally
        {
            model.Dispose();
        }
    }

    private static TranscriptionResult RunAsr(
        Model model,
        TranscribeRole roles,
        TranscriptionOptions options,
        PcmSource audioSource,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        using var session = CreateSession(model, options);

        var words = new List<WordResult>();
        var speakerSegments = new List<SpeakerSegmentResult>();
        var merger = new TranscriptMerger();
        string detectedLanguage = string.Empty;
        bool wasAborted = false;
        bool wasTruncated = false;

        // TryPlan fails only when the window cannot advance past the
        // overlap. WindowSeconds is checked when it is set, so reaching
        // the fallback means the audio is empty or shorter than one
        // window; either way one run over the whole thing is correct.
        if (WindowPlanner.TryPlan(
            (int)Math.Min(audioSource.LengthSamples, int.MaxValue),
            options.WindowSeconds,
            out var windows,
            out _))
        {
            int windowIndex = 0;
            foreach (var window in windows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var samples = audioSource.ReadWindow(window.OffsetSamples, window.LengthSamples);
                var transcript = session.Run(samples, builder => Configure(builder, options), cancellationToken);
                Collect(merger, window, transcript, words, speakerSegments);

                detectedLanguage = transcript.DetectedLanguage;
                wasAborted = transcript.WasAborted;
                wasTruncated = transcript.WasTruncated;

                windowIndex++;
                progress?.Report((double)windowIndex / windows.Count);
            }
        }
        else
        {
            var samples = audioSource.ReadWindow(0, (int)Math.Min(audioSource.LengthSamples, int.MaxValue));
            var transcript = session.Run(samples, builder => Configure(builder, options), cancellationToken);
            var window = new AudioWindow(0, (int)Math.Min(audioSource.LengthSamples, int.MaxValue));
            Collect(merger, window, transcript, words, speakerSegments);

            detectedLanguage = transcript.DetectedLanguage;
            wasAborted = transcript.WasAborted;
            wasTruncated = transcript.WasTruncated;
            progress?.Report(1.0);
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
            WasTruncated = wasTruncated
        };
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
                    yield return new Models.StreamUpdate
                    {
                        FullText = text.FullText,
                        CommittedText = text.CommittedText,
                        TentativeText = text.TentativeText,
                        IsFinal = false
                    };

                    await System.Threading.Tasks.Task.Delay(50, cancellationToken);
                }

                var finalText = stream.GetCurrentText();
                yield return new Models.StreamUpdate
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

    public async Task<List<BatchItemResult>> BatchTranscribeAsync(
        IReadOnlyList<string> audioPaths,
        string modelPath,
        TranscriptionOptions options,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var model = LoadModel(modelPath, options.BackendRequest, options.Device);

        try
        {
            using var session = CreateSession(model, options);
            var results = new List<BatchItemResult>();

            for (int i = 0; i < audioPaths.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var audioPath = audioPaths[i];
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
        return Model.Load(resolved, builder =>
        {
            if (backend != null)
                builder.WithBackend(backend.Value);
            if (device != null)
                builder.WithDevice(device);
        });
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
}
