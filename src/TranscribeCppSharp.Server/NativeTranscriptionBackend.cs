namespace TranscribeCppSharp.Server;

using System.Globalization;
using TranscribeCppSharp.Audio;
using TranscribeCppSharp.Interop;
using TranscribeCppSharp.Models;

/// <summary>
/// The real backend: loads one model, then runs every request through it, one at
/// a time.
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

        using var session = loaded.CreateSession();
        Session.SessionLimitsInfo limits = session.GetLimits();
        int maxWindowMs = limits.EffectiveMaxAudioMs > 0
            ? (int)Math.Clamp(limits.EffectiveMaxAudioMs - WindowPlanner.OverlapMs, 1, int.MaxValue)
            : int.MaxValue;
        int windowMs = (int)Math.Min((long)options.ChunkSeconds * 1000, maxWindowMs);

        if (!WindowPlanner.TryPlan((int)totalSamples, windowMs, out IReadOnlyList<AudioWindow> windows, out string? planError))
        {
            throw new InvalidOperationException(planError ?? "the audio could not be split into windows");
        }

        // Word timestamps are asked for only when the model reports it can
        // produce them. Asking anyway is how a caller ends up with a 200 and an
        // empty list that looks like a server bug.
        bool wantWords = request.WantWords && capabilities.MaxTimestampKind >= TimestampKind.TimestampsWord;

        var merger = new TranscriptMerger();
        var words = new List<TranscriptionWord>();
        string language = string.Empty;

        foreach (AudioWindow window in windows)
        {
            float[] chunk = pcm.ReadWindow(window.OffsetSamples, window.LengthSamples);
            Transcript transcript = session.Run(
                chunk,
                r =>
                {
                    r.WithTask(request.Task);
                    r.WithDiarize(request.Diarize ? DiarizeMode.DiarizeModeOn : DiarizeMode.DiarizeModeOff);
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
                    WordResult shifted = TranscriptMerger.Shift(window, word);
                    words.Add(new TranscriptionWord(
                        shifted.Text,
                        shifted.Start.TotalSeconds,
                        shifted.End.TotalSeconds));
                }
            }

            if (language.Length == 0)
            {
                language = transcript.DetectedLanguage;
            }
        }

        List<TranscriptionSegment> segments = merger.Segments
            .Select((segment, index) => new TranscriptionSegment(
                index,
                segment.Start.TotalSeconds,
                segment.End.TotalSeconds,
                segment.Text.Trim(),
                request.Diarize ? segment.SpeakerId : null))
            .ToList();

        // Some families report no detected language (whisper-tiny does not, in a
        // measured run). Falling back to the language the caller pinned is what
        // the GUI does too: it is the language the transcript was produced in,
        // not a guess.
        if (language.Length == 0 && !string.IsNullOrWhiteSpace(request.Language))
        {
            language = request.Language;
        }

        return new TranscriptionOutput(
            merger.BuildText(),
            language,
            totalSamples / (double)WindowPlanner.SampleRate,
            segments,
            words);
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

        string path = ResolveModelPath();
        if (!ServerOptions.TryParseBackendName(options.Backend, out BackendRequest backend))
        {
            throw new ModelUnavailableException($"unknown backend '{options.Backend}'");
        }

        Model loaded = Model.Load(path, p => p.WithBackend(backend));
        lock (stateLock)
        {
            model = loaded;
        }

        return loaded;
    }

    private string ResolveModelPath()
    {
        if (File.Exists(options.Model))
        {
            return Path.GetFullPath(options.Model);
        }

        if (!options.AllowDownloads)
        {
            throw new ModelUnavailableException(
                $"'{options.Model}' is not a file, and this server was started without --allow-download. "
                + "Pass a path to a GGUF file, fetch the alias first with the transcribe CLI, "
                + "or restart with --allow-download to let the first request fetch it.");
        }

        // ModelStore reports download progress and provenance on the writer. The
        // server has no console user waiting, but the text is kept out of the
        // HTTP response and written to stderr so a failed fetch can be diagnosed.
        var log = new StringWriter(CultureInfo.InvariantCulture);
        try
        {
            return ModelStore.Resolve(options.Model, quant: null, log);
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or InvalidOperationException)
        {
            throw new ModelUnavailableException(
                $"'{options.Model}' could not be fetched: {ex.Message} (details on stderr)", ex);
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
}
