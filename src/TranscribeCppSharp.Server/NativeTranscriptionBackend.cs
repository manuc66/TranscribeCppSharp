namespace TranscribeCppSharp.Server;

using System.Globalization;
using TranscribeCppSharp.Audio;
using TranscribeCppSharp.Interop;
using TranscribeCppSharp.Models;
using TranscribeCppSharp.SharedSource;

/// <summary>
/// The real backend: loads one model (plus an optional diarizer), then runs every
/// request through it, one at a time.
/// </summary>
/// <remarks>
/// <para>
/// The serialization is not a design choice. transcribe.cpp allows one run per
/// model at a time across all of its sessions (see the concurrency page of the
/// library docs), so concurrent HTTP requests sharing a model would be a data
/// race the wrapper cannot make safe. A semaphore turns that into a queue.
/// </para>
/// <para>
/// The windowing here is the same math the CLI uses (<see cref="WindowPlanner"/>
/// and <see cref="TranscriptMerger"/>): one window in memory at a time, a 1 s
/// overlap, and the seam deduplicated. It is duplicated as a loop, not as
/// arithmetic; consolidating the three loops (CLI, GUI, server) is a separate
/// change.
/// </para>
/// </remarks>
internal sealed class NativeTranscriptionBackend : ITranscriptionBackend
{
    private readonly ServerOptions options;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly object stateLock = new();
    private Model? model;
    private Model? diarizerModel;
    private bool disposed;

    /// <summary>Creates a backend that loads the model on its first request.</summary>
    internal NativeTranscriptionBackend(ServerOptions options)
        => this.options = options;

    /// <inheritdoc/>
    public string ModelId => options.Model;

    /// <inheritdoc/>
    public bool IsModelLoaded
    {
        get
        {
            lock (stateLock)
            {
                return model is not null;
            }
        }
    }

    /// <inheritdoc/>
    public async Task<TranscriptionOutput> TranscribeAsync(TranscriptionRequest request)
    {
        await gate.WaitAsync(request.CancellationToken).ConfigureAwait(false);
        try
        {
            // The native call blocks its thread for the whole file. Task.Run keeps
            // that off the request thread; the cancellation token reaches
            // session.Run below, which is what actually aborts the native work.
            return await System.Threading.Tasks.Task.Run(() => Run(request), CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            model?.Dispose();
            model = null;
            diarizerModel?.Dispose();
            diarizerModel = null;
        }
        finally
        {
            gate.Release();
            gate.Dispose();
        }
    }

    private TranscriptionOutput Run(TranscriptionRequest request)
    {
        request.CancellationToken.ThrowIfCancellationRequested();

        Model loaded = EnsureModel();
        Model.ModelCapabilities capabilities = loaded.GetCapabilities();

        using PcmSource pcm = AudioLoader.Open(request.AudioPath);
        long totalSamples = pcm.LengthSamples;
        if (totalSamples == 0)
        {
            throw new InvalidOperationException("the uploaded audio decodes to no samples");
        }

        if (totalSamples > int.MaxValue)
        {
            throw new InvalidOperationException(
                $"the uploaded audio is {totalSamples / (double)WindowPlanner.SampleRate / 3600:0.0} h; "
                + "the window planner counts samples in a 32-bit integer, so this server accepts at most ~37 h");
        }

        int maxWindowMs;
        using (var limitsSession = loaded.CreateSession())
        {
            Session.SessionLimitsInfo limits = limitsSession.GetLimits();
            maxWindowMs = limits.EffectiveMaxAudioMs > 0
                ? (int)Math.Clamp(limits.EffectiveMaxAudioMs - WindowPlanner.OverlapMs, 1, int.MaxValue)
                : int.MaxValue;
        }

        int windowMs = (int)Math.Min((long)options.ChunkSeconds * 1000, maxWindowMs);

        // A configured diarizer is a second pass, so the ASR model is not asked
        // for attribution it may not support. Explicit configuration wins over
        // the model's own capability, which is what makes the result predictable.
        bool askModelForSpeakers = request.Diarize && options.Diarizer is null;

        FileTranscript transcript = WindowRetry.Run(
            Math.Max(1, windowMs / 1000),
            seconds => TranscribeWindows(
                loaded, capabilities, pcm, totalSamples, seconds * 1000, request, askModelForSpeakers),
            (failed, next) => Console.Error.WriteLine(
                $"transcribe-server: a {failed} s window did not fit in memory (native status {Status.ErrOom}); "
                + $"restarting the file at {next} s, nothing from the failed attempt is kept"));

        if (request.Diarize && options.Diarizer is not null)
        {
            IReadOnlyList<SpeakerSegmentResult> turns = RunDiarizer(pcm, request.CancellationToken);
            transcript = transcript with
            {
                Segments = SpeakerAssignment.Apply(transcript.Segments, turns),
            };
        }

        return ToOutput(transcript, request.Diarize);
    }

    /// <summary>One attempt: plans the windows, runs them, merges the result.</summary>
    private static FileTranscript TranscribeWindows(
        Model loaded,
        Model.ModelCapabilities capabilities,
        PcmSource pcm,
        long totalSamples,
        int windowMs,
        TranscriptionRequest request,
        bool askModelForSpeakers)
    {
        if (!WindowPlanner.TryPlan((int)totalSamples, windowMs, out IReadOnlyList<AudioWindow> windows, out string? planError))
        {
            throw new InvalidOperationException(planError ?? "the audio could not be split into windows");
        }

        using var session = loaded.CreateSession();

        // Word timestamps are asked for only when the model reports it can
        // produce them. Asking anyway is how a caller ends up with a 200 and an
        // empty list that looks like a server bug.
        bool wantWords = request.WantWords && capabilities.MaxTimestampKind >= TimestampKind.TimestampsWord;

        var merger = new TranscriptMerger();
        var words = new List<WordResult>();
        string language = string.Empty;

        foreach (AudioWindow window in windows)
        {
            float[] chunk = pcm.ReadWindow(window.OffsetSamples, window.LengthSamples);
            Transcript transcript = session.Run(
                chunk,
                r =>
                {
                    r.WithTask(request.Task);
                    r.WithDiarize(askModelForSpeakers ? DiarizeMode.DiarizeModeOn : DiarizeMode.DiarizeModeOff);
                    if (!string.IsNullOrWhiteSpace(request.Language))
                    {
                        r.WithLanguage(request.Language);
                    }

                    if (wantWords)
                    {
                        r.WithTimestamps(TimestampKind.TimestampsWord);
                    }
                },
                request.CancellationToken);

            merger.Add(window, transcript);

            if (wantWords)
            {
                foreach (WordResult word in transcript.Words)
                {
                    words.Add(TranscriptMerger.Shift(window, word));
                }
            }

            if (language.Length == 0)
            {
                language = transcript.DetectedLanguage;
            }
        }

        // Some families report no detected language (whisper-tiny does not, in a
        // measured run). Falling back to the language the caller pinned is what
        // the GUI does too: it is the language the transcript was produced in,
        // not a guess.
        if (language.Length == 0 && !string.IsNullOrWhiteSpace(request.Language))
        {
            language = request.Language;
        }

        return new FileTranscript(
            merger.BuildText(),
            language,
            totalSamples / (double)WindowPlanner.SampleRate,
            merger.Segments,
            words);
    }

    /// <summary>Maps the merged result to the wire shapes.</summary>
    private static TranscriptionOutput ToOutput(FileTranscript transcript, bool diarized)
        => new(
            transcript.Text,
            transcript.Language,
            transcript.DurationSeconds,
            transcript.Segments
                .Select((segment, index) => new TranscriptionSegment(
                    index,
                    segment.Start.TotalSeconds,
                    segment.End.TotalSeconds,
                    segment.Text.Trim(),
                    diarized ? segment.SpeakerId : null))
                .ToList(),
            transcript.Words
                .Select(word => new TranscriptionWord(
                    word.Text,
                    word.Start.TotalSeconds,
                    word.End.TotalSeconds))
                .ToList());

    /// <summary>
    /// Runs the configured DIARIZE-role model over the whole file.
    /// </summary>
    /// <remarks>
    /// The role is offline over a whole buffer: upstream has no streaming
    /// diarization, and speaker ids are per run, so the file cannot be windowed
    /// without renumbering speakers at every seam. That means this pass holds the
    /// whole file in memory, on top of both models; the docs say so.
    /// </remarks>
    private IReadOnlyList<SpeakerSegmentResult> RunDiarizer(PcmSource pcm, CancellationToken cancellationToken)
    {
        Model diarizer = EnsureDiarizer();
        using var session = diarizer.CreateDiarizeSession();
        int total = (int)Math.Min(pcm.LengthSamples, int.MaxValue);
        IReadOnlyList<SpeakerSegmentResult> turns = session.Run(
            pcm.ReadWindow(0, total),
            ct: cancellationToken);

        Console.Error.WriteLine(
            $"transcribe-server: diarizer {options.Diarizer} returned {turns.Count} speaker turn(s) for the whole file");
        return turns;
    }

    private Model EnsureModel()
    {
        lock (stateLock)
        {
            if (model is not null)
            {
                return model;
            }
        }

        Model loaded = Model.Load(ResolvePath(options.Model, "model"), p => p.WithBackend(Backend()));
        lock (stateLock)
        {
            model = loaded;
        }

        return loaded;
    }

    private Model EnsureDiarizer()
    {
        lock (stateLock)
        {
            if (diarizerModel is not null)
            {
                return diarizerModel;
            }
        }

        string spec = options.Diarizer
            ?? throw new InvalidOperationException("no diarizer is configured");

        Model loaded = Model.Load(ResolvePath(spec, "diarizer"), p => p.WithBackend(Backend()));
        TranscribeRole roles = loaded.Roles;
        if (!roles.HasFlag(TranscribeRole.Diarize))
        {
            loaded.Dispose();
            throw new ModelUnavailableException(
                $"'{spec}' does not serve the DIARIZE role (roles: {roles}); "
                + "use a diarizer such as diar_streaming_sortformer_4spk-v2.1");
        }

        lock (stateLock)
        {
            diarizerModel = loaded;
        }

        return loaded;
    }

    private BackendRequest Backend()
        => ServerOptions.TryParseBackendName(options.Backend, out BackendRequest backend)
            ? backend
            : throw new ModelUnavailableException($"unknown backend '{options.Backend}'");

    private string ResolvePath(string spec, string label)
    {
        if (File.Exists(spec))
        {
            return Path.GetFullPath(spec);
        }

        if (!options.AllowDownloads)
        {
            throw new ModelUnavailableException(
                $"'{spec}' is not a file, and this server was started without --allow-download. "
                + $"Pass a path to a {label} GGUF file, fetch the alias first with the transcribe CLI, "
                + "or restart with --allow-download to let the first request fetch it.");
        }

        // ModelStore reports download progress and provenance on the writer. The
        // server has no console user waiting, but the text is kept out of the
        // HTTP response and written to stderr so a failed fetch can be diagnosed.
        var log = new StringWriter(CultureInfo.InvariantCulture);
        try
        {
            return ModelStore.Resolve(spec, quant: null, log);
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or InvalidOperationException)
        {
            throw new ModelUnavailableException(
                $"'{spec}' could not be fetched: {ex.Message} (details on stderr)", ex);
        }
        finally
        {
            string details = log.ToString();
            if (details.Length > 0)
            {
                Console.Error.WriteLine(details);
            }
        }
    }

    /// <summary>The merged result, before it is mapped to the wire shapes.</summary>
    private sealed record FileTranscript(
        string Text,
        string Language,
        double DurationSeconds,
        IReadOnlyList<MergedSegment> Segments,
        IReadOnlyList<WordResult> Words);
}
