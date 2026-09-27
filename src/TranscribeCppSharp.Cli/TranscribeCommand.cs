// The `transcribe` command: the flow is argument handling (CliOptions), the
// windowing math (WindowPlanner), audio loading (AudioLoader) and the
// transcript formats (TranscriptWriter), with the orchestration below.
//
// Console and stderr are injected so the whole command is unit testable: the
// entry point generated from Program.cs cannot be called from a test.

using System.Diagnostics;
using System.Globalization;
using System.Text;
using TranscribeCppSharp;
using TranscribeCppSharp.Interop;

namespace TranscribeCppSharp.Cli;

internal static class TranscribeCommand
{
    /// <summary>The name the tool is installed as.</summary>
    internal const string ToolName = "transcribe";

    // @readme-begin cli-help
    internal const string HelpText = """
        Transcribe audio with a speech-to-text model. Supports every model family
        that transcribe.cpp supports (Whisper, Moonshine, Parakeet, Canary, GigaAM,
        Voxtral, Qwen3-ASR, MOSS diarization, …).

        Usage: transcribe <audio> [model] [options]

          <audio>        WAV (16 kHz mono 16-bit) read directly; any other
                         format (ogg, mp3, m4a, …) is decoded with ffmpeg
                         (must be installed)
          [model]        a model file path, a known alias (default:
                         moss-transcribe-diarize), or a HuggingFace spec
                         '<owner>/<repo>/<file.gguf>[@<revision>]'.
                         Aliases and any other supported GGUF are downloaded
                         from HuggingFace on first use and cached.

          --model <m>    same as the [model] argument
          --quant <q>    quantization for a known alias (e.g. Q4_K_M, Q5_K_M,
                         Q8_0, F16); default is per model (see --list-models)
          --list-models  list the known model aliases and exit
          --model-info <alias>  show details (revision, size, license) for one alias
          --backend <b>  compute backend: auto (default), cpu, cpu-accel,
                         metal, vulkan, cuda, rocm. 'auto' runs on the GPU
                         when one initializes (every discrete GPU is probed
                         before the integrated ones) and falls back to the CPU
                         otherwise; a forced backend fails instead of falling
                         back
          --device <n>   run on that exact device (index from --list-devices);
                         never falls back to another device
          --list-devices list the compute devices this build and machine can see
          --lang <code>  language code for the decoder (default: en)
          --chunk <sec>  max per-transcription window in seconds (default: 300);
                         long audio is split with 1 s overlap and deduplicated
          --no-diarize   disable speaker diarization
          --out <file>   write the transcript to a file; format controlled
                         by --format (plain/vtt/json)
          --format <fmt> transcript file format: plain (default, timestamped
                         lines), vtt (WebVTT, speaker cues) or json
                         (whisper-style segments)
          --help         show this help
        """;
    // @end cli-help

    /// <summary>
    /// Runs one command. Returns the process exit code: 0 on success, 1 on a
    /// reported error (a message on stderr, never a stack trace).
    /// </summary>
    internal static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        // Flags answered without touching the model or the network.
        if (Has(args, "--list-models"))
        {
            ModelStore.List(stdout);
            return 0;
        }

        if (Has(args, "--model-info"))
        {
            return ModelStore.Info(CliOptions.ValueAfter(args, "--model-info") ?? string.Empty, stdout, stderr) ? 0 : 1;
        }

        if (Has(args, "--list-devices"))
        {
            // Needs the backends registered, so it cannot be answered from the
            // manifest like --list-models. Handled before any model resolution so
            // it never triggers a download.
            Backends.InitDefault();
            stdout.WriteLine(DeviceSelection.FormatDevices(Backends.EnumerateDevices()));
            return 0;
        }

        if (args.Length == 0 || Has(args, "--help") || Has(args, "-h"))
        {
            stdout.WriteLine(HelpText);
            return args.Length == 0 ? 1 : 0;
        }

        if (!CliOptions.TryParse(args, out CliOptions? options, out string? parseError))
        {
            stderr.WriteLine(parseError);
            return 1;
        }

        string modelPath;
        try
        {
            modelPath = ModelStore.Resolve(options.ModelSpec, options.Quant, stderr);
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException)
        {
            // Unknown alias/spec, download failure, checksum mismatch: report and stop.
            stderr.WriteLine(ex.Message);
            return 1;
        }

        if (!File.Exists(options.AudioPath))
        {
            stderr.WriteLine($"no such file: {options.AudioPath}");
            return 1;
        }

        Backends.InitDefault();

        var devices = Backends.EnumerateDevices();
        DeviceChoice? choice = DeviceSelection.Resolve(devices, options.Backend, options.DeviceIndex, out var deviceError);
        if (choice is null)
        {
            stderr.WriteLine(deviceError);
            return 1;
        }

        // A forced backend that this build/machine does not have fails here rather
        // than silently landing on another one: the upstream returns ERR_BACKEND, and
        // quietly using the CPU would misreport what actually ran.
        if (choice.Device is null && options.Backend != BackendRequest.BackendAuto && !Backends.BackendAvailable(options.Backend))
        {
            stderr.WriteLine($"--backend {options.BackendName} is not available in this build or on this machine.");
            stderr.WriteLine(devices.Count == 0 ? "No compute device is registered." : DeviceSelection.FormatDevices(devices));
            return 1;
        }

        float[] pcm;
        try
        {
            pcm = AudioLoader.Load(options.AudioPath);
        }
        catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or IOException)
        {
            // A file that is neither a readable WAV nor decodable by ffmpeg, or
            // no ffmpeg at all: one line, not a .NET stack trace.
            stderr.WriteLine(ex.Message);
            return 1;
        }

        stdout.WriteLine($"audio : {Fmt.Samples(pcm.Length)} samples = {pcm.Length / (double)WindowPlanner.SampleRate:F1}s @ 16kHz mono");

        using var model = Model.Load(modelPath, p =>
        {
            p.WithBackend(choice.Backend);
            if (choice.Device is not null)
            {
                p.WithDevice(choice.Device);
            }
        });
        string modelLabel = $"{model.Architecture}/{model.Variant}";
        stdout.WriteLine($"model : {modelLabel}");
        stdout.WriteLine($"compute: {DescribeCompute(choice, model)}");
        stdout.WriteLine($"diarization supported: {model.Supports(Feature.FeatureDiarization)}");

        using var session = model.CreateSession();
        var limits = session.GetLimits();
        int maxWindowMs = limits.EffectiveMaxAudioMs > 0
            ? (int)Math.Clamp(limits.EffectiveMaxAudioMs - WindowPlanner.OverlapMs, 1, int.MaxValue)
            : int.MaxValue;
        int windowMs = (int)Math.Min((long)options.ChunkSeconds * 1000, maxWindowMs);
        stdout.WriteLine($"window: {windowMs / 1000}s (model max audio {limits.EffectiveMaxAudioMs / 1000}s)");

        if (!WindowPlanner.TryPlan(pcm.Length, windowMs, out var windows, out var planError))
        {
            stderr.WriteLine(planError);
            return 1;
        }

        var merged = new StringBuilder();
        var lines = new List<TranscriptLine>();
        long lastSegmentEndMs = -WindowPlanner.OverlapMs;
        var overall = Stopwatch.StartNew();

        using (var outFile = options.OutPath is null
            ? null
            : new StreamWriter(options.OutPath, append: false, new UTF8Encoding(false)) { AutoFlush = true })
        {
            if (outFile is not null)
            {
                TranscriptWriter.WriteHeader(
                    outFile, options.Format, options.AudioPath,
                    TimeSpan.FromSeconds(pcm.Length / (double)WindowPlanner.SampleRate),
                    modelLabel, options.Language, options.Diarize, windowMs / 1000, DateTime.Now);
            }

            double audioTotalSec = pcm.Length / (double)WindowPlanner.SampleRate;
            for (int chunkIndex = 0; chunkIndex < windows.Count; chunkIndex++)
            {
                AudioWindow window = windows[chunkIndex];
                int offset = window.OffsetSamples;
                int len = window.LengthSamples;
                var chunk = new float[len];
                Array.Copy(pcm, offset, chunk, 0, len);
                long chunkStartMs = window.StartMs;

                stdout.WriteLine($"\n=== window {chunkIndex + 1} @ {Fmt.Ts(chunkStartMs)} ({Fmt.Samples(len)} samples) ===");
                var windowSw = Stopwatch.StartNew();

                Transcript transcript;
                try
                {
                    transcript = session.Run(chunk, r =>
                    {
                        r.WithDiarize(options.Diarize);
                        r.WithLanguage(options.Language);
                    });
                }
                catch (TranscribeException ex)
                {
                    stderr.WriteLine($"  transcription failed: {ex.Message}");
                    return 1;
                }

                stdout.WriteLine($"  lang   : {transcript.DetectedLanguage}   aborted: {transcript.WasAborted}  truncated: {transcript.WasTruncated}");
                stdout.WriteLine($"  full   : {transcript.FullText.Trim()}");
                if (options.Diarize != DiarizeMode.DiarizeModeOff || transcript.RawText.Length > 0)
                {
                    stdout.WriteLine($"  raw    : {transcript.RawText.Trim()}");
                }

                double windowSec = windowSw.Elapsed.TotalSeconds;
                double audioSec = len / (double)WindowPlanner.SampleRate;
                double processedSec = (offset + len) / (double)WindowPlanner.SampleRate;
                double remainingSec = pcm.Length / (double)WindowPlanner.SampleRate - processedSec;
                double rtf = windowSec / audioSec;
                stdout.WriteLine($"  time   : {Fmt.Dur(windowSec)}/ {audioSec:0.0}s audio (RTF {rtf:0.0}x) | done {Fmt.Dur(processedSec)} / {Fmt.Dur(audioTotalSec)} | ETA ~{Fmt.Dur(remainingSec * rtf)}");

                foreach (var seg in transcript.Segments)
                {
                    // Drop a segment that starts well before the end of the last
                    // kept one: it comes from the overlap with the previous window.
                    var start = seg.Start.TotalMilliseconds + chunkStartMs;
                    var end = seg.End.TotalMilliseconds + chunkStartMs;
                    if (start < lastSegmentEndMs - 500)
                    {
                        continue;
                    }

                    lastSegmentEndMs = (long)end;
                    var text = Fmt.Normalize(seg.Text);
                    var line = new TranscriptLine(start, end, seg.SpeakerId, text);
                    lines.Add(line);

                    if (outFile is not null)
                    {
                        TranscriptWriter.WriteSegment(outFile, options.Format, line);
                        stdout.WriteLine($"  -> [{Fmt.Ts(start)} -> {Fmt.Ts(end)}] Speaker {seg.SpeakerId}: {text}");
                    }

                    merged.AppendLine(CultureInfo.CurrentCulture, $"[{Fmt.Ts(start)} -> {Fmt.Ts(end)}] Speaker {seg.SpeakerId}: {text}");
                }
            }

            if (outFile is not null)
            {
                TranscriptWriter.WriteFooter(outFile, options.Format, options.Language, lines, DateTime.Now);
                stdout.WriteLine($"\ntranscript written to: {options.OutPath} ({FormatName(options.Format)})");
            }

            double totalSec = overall.Elapsed.TotalSeconds;
            stdout.WriteLine("\n=== summary ===");
            stdout.WriteLine($"  total  : {Fmt.Dur(totalSec)} (RTF {totalSec / audioTotalSec:0.0}x) for {Fmt.Dur(audioTotalSec)} audio");
            stdout.WriteLine($"  windows: {windows.Count}");
            if (options.OutPath is not null)
            {
                stdout.WriteLine($"  file   : {options.OutPath} ({FormatName(options.Format)})");
            }
        }

        stdout.WriteLine("\n=== merged transcript (speaker-attributed) ===");
        stdout.WriteLine(merged.ToString());
        return 0;
    }

    /// <summary>The --format spelling, as accepted and as echoed back.</summary>
    internal static string FormatName(TranscriptFormat format) => format switch
    {
        TranscriptFormat.Vtt => "vtt",
        TranscriptFormat.Json => "json",
        _ => "plain",
    };

    private static bool Has(string[] args, string name)
    {
        foreach (string arg in args)
        {
            if (string.Equals(arg, name, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    // Reports the backend the model ACTUALLY landed on (transcribe_model_backend
    // plus the resolved device), not the one that was requested, so "GPU active"
    // is a fact on screen and not an assumption. When the default auto selection
    // ends up on the CPU, that is spelled out: it means no GPU initialized here.
    internal static string DescribeCompute(DeviceChoice choice, Model model)
    {
        string actual = string.IsNullOrEmpty(model.Backend) ? "unknown" : model.Backend;
        BackendDevice? device = model.Device;
        string where = device is null ? string.Empty : $" on {DeviceSelection.Describe(device)}";
        // Only for an automatic choice: an explicit --device 2 (the CPU) is what
        // the user asked for and must not be reported as a fallback.
        string note = choice.Device is null
            && choice.Backend == BackendRequest.BackendAuto
            && string.Equals(device?.Kind, "cpu", StringComparison.OrdinalIgnoreCase)
                ? " (no GPU initialized, CPU fallback)"
                : string.Empty;

        return $"{actual}{where} [requested: {choice.Request}]{note}";
    }
}
