using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using TranscribeCppSharp;
using TranscribeCppSharp.Cli;
using TranscribeCppSharp.Interop;

const string DefaultModel = "moss-transcribe-diarize";

if (args.Contains("--list-models"))
{
    ModelStore.List();
    return 0;
}

if (args.Contains("--model-info"))
{
    return ModelStore.Info(ArgAfter("--model-info") ?? string.Empty) ? 0 : 1;
}

if (args.Length == 0 || args.Contains("--help") || args.Contains("-h"))
{
    Console.WriteLine("""
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
        """);
    return args.Length == 0 ? 1 : 0;
}

var audioPath = Path.GetFullPath(args[0]);

string modelPath;
try
{
    modelPath = ModelStore.Resolve(
        ArgAfter("--model")
        ?? (args.Length > 1 && !args[1].StartsWith("--") ? args[1] : DefaultModel),
        ArgAfter("--quant"));
}
catch (Exception ex) when (ex is IOException or HttpRequestException)
{
    // Unknown alias/spec, download failure, checksum mismatch: report and stop.
    Console.Error.WriteLine(ex.Message);
    return 1;
}

var lang = ArgAfter("--lang") ?? "en";
var chunkSeconds = int.TryParse(ArgAfter("--chunk"), out var cs) && cs > 0 ? cs : 300;
var diarize = args.Contains("--no-diarize") ? DiarizeMode.DiarizeModeOff : DiarizeMode.DiarizeModeOn;
var outPath = ArgAfter("--out") is { } o ? Path.GetFullPath(o) : null;
var outFormat = ArgAfter("--format") ?? "plain";
if (outFormat is not ("plain" or "vtt" or "json"))
{
    Console.Error.WriteLine($"unknown --format: {outFormat} (expected plain, vtt or json)");
    return 1;
}

if (!File.Exists(audioPath)) { Console.Error.WriteLine($"no such file: {audioPath}"); return 1; }

Backends.InitDefault();

var pcm = LoadPcm(audioPath);
Console.WriteLine($"audio : {pcm.Length:N0} samples = {pcm.Length / 16000d:F1}s @ 16kHz mono");

using var model = Model.Load(modelPath, p => p.WithBackend(BackendRequest.BackendCpu));
Console.WriteLine($"model : {model.Architecture}/{model.Variant}");
Console.WriteLine($"diarization supported: {model.Supports(Feature.FeatureDiarization)}");

using var session = model.CreateSession();
var limits = session.GetLimits();
var maxWindowMs = limits.EffectiveMaxAudioMs > 0
    ? (int)Math.Clamp(limits.EffectiveMaxAudioMs - 1000, 1, int.MaxValue)
    : int.MaxValue;
var windowMs = Math.Min(chunkSeconds * 1000, maxWindowMs);
Console.WriteLine($"window: {windowMs / 1000}s (model max audio {limits.EffectiveMaxAudioMs / 1000}s)");

const int overlapMs = 1000;
const int sampleRate = 16000;
var windowSamples = (int)((long)windowMs * sampleRate / 1000);
var overlapSamples = overlapMs * sampleRate / 1000;

var merged = new StringBuilder();
var finalSegments = new List<(double msStart, double msEnd, int speaker, string text)>();
long lastSegmentEndMs = -overlapMs;
int chunkIndex = 0;
var overall = Stopwatch.StartNew();

using (var outFile = outPath is null ? null : new StreamWriter(outPath, append: false, new UTF8Encoding(false)) { AutoFlush = true })
{
    if (outFile is not null && outFormat == "vtt")
    {
        WriteVttHeader(outFile, audioPath, TimeSpan.FromSeconds(pcm.Length / (double)sampleRate),
            $"{model.Architecture}/{model.Variant}", lang, diarize, windowMs / 1000);
    }
    else if (outFile is not null && outFormat == "json")
    {
        // nothing yet: the JSON document is written once at the end.
    }
    else if (outFile is not null)
    {
        outFile.WriteLine("# TranscribeCppSharp diarization transcript");
        outFile.WriteLine($"# source   : {audioPath}");
        outFile.WriteLine($"# audio    : {TimeSpan.FromSeconds(pcm.Length / (double)sampleRate):c}");
        outFile.WriteLine($"# model    : {model.Architecture}/{model.Variant}");
        outFile.WriteLine($"# lang     : {lang}   diarize: {(diarize == DiarizeMode.DiarizeModeOff ? "off" : "on")}   window: {windowMs / 1000}s");
        outFile.WriteLine($"# generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        outFile.WriteLine();
    }

    for (int offset = 0; ; chunkIndex++)
    {
        var len = Math.Min(windowSamples, pcm.Length - offset);
        var chunk = new float[len];
        Array.Copy(pcm, offset, chunk, 0, len);
        var chunkStartMs = (long)(offset / (double)sampleRate * 1000);

        Console.WriteLine($"\n=== window {chunkIndex + 1} @ {Ts(chunkStartMs)} ({len:N0} samples) ===");
        var windowSw = Stopwatch.StartNew();

        Transcript transcript;
        try
        {
            transcript = session.Run(chunk, r =>
            {
                r.WithDiarize(diarize);
                r.WithLanguage(lang);
            });
        }
        catch (TranscribeException ex)
        {
            Console.Error.WriteLine($"  transcription failed: {ex.Message}");
            return 1;
        }

        Console.WriteLine($"  lang   : {transcript.DetectedLanguage}   aborted: {transcript.WasAborted}  truncated: {transcript.WasTruncated}");
        Console.WriteLine($"  full   : {transcript.FullText.Trim()}");
        if (diarize != DiarizeMode.DiarizeModeOff || transcript.RawText.Length > 0)
        {
            Console.WriteLine($"  raw    : {transcript.RawText.Trim()}");
        }

        var windowSec = windowSw.Elapsed.TotalSeconds;
        var audioSec = len / (double)sampleRate;
        var processedSec = (offset + len) / (double)sampleRate;
        var remainingSec = pcm.Length / (double)sampleRate - processedSec;
        var rtf = windowSec / audioSec;
        Console.WriteLine($"  time   : {Dur(windowSec)}/ {audioSec:0.0}s audio (RTF {rtf:0.0}x) | done {Dur(processedSec)} / {Dur(pcm.Length / (double)sampleRate)} | ETA ~{Dur(remainingSec * rtf)}");

        foreach (var seg in transcript.Segments)
        {
            var start = seg.Start.TotalMilliseconds + chunkStartMs;
            var end = seg.End.TotalMilliseconds + chunkStartMs;
            if (start < lastSegmentEndMs - 500)
            {
                continue;
            }

            lastSegmentEndMs = (long)end;
            var text = Normalize(seg.Text);
            finalSegments.Add((start, end, seg.SpeakerId, text));

            if (outFile is not null && outFormat == "vtt")
            {
                outFile.WriteLine($"{VttTs(start)} --> {VttTs(end)}");
                outFile.WriteLine($"<v Speaker {seg.SpeakerId}>{text}</v>");
                outFile.WriteLine();
            }
            else if (outFile is not null && outFormat == "plain")
            {
                outFile.WriteLine($"[{Ts(start)} -> {Ts(end)}] Speaker {seg.SpeakerId}: {text}");
            }

            if (outFile is not null)
            {
                Console.WriteLine($"  -> [{Ts(start)} -> {Ts(end)}] Speaker {seg.SpeakerId}: {text}");
            }

            merged.AppendLine($"[{Ts(start)} -> {Ts(end)}] Speaker {seg.SpeakerId}: {text}");
        }

        if (offset + len >= pcm.Length)
        {
            break;
        }

        offset += windowSamples - overlapSamples;
    }

    if (outFile is not null)
    {
        if (outFormat == "json")
        {
            WriteJson(outFile, lang, finalSegments);
        }
        else if (outFormat == "vtt")
        {
            outFile.WriteLine($"NOTE done {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        }
        else
        {
            outFile.WriteLine();
            outFile.WriteLine($"# done: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        }

        Console.WriteLine($"\ntranscript written to: {outPath} ({outFormat})");
    }

    var totalSec = overall.Elapsed.TotalSeconds;
    Console.WriteLine($"\n=== summary ===");
    Console.WriteLine($"  total  : {Dur(totalSec)} (RTF {totalSec / (pcm.Length / (double)sampleRate):0.0}x) for {Dur(pcm.Length / (double)sampleRate)} audio");
    Console.WriteLine($"  windows: {chunkIndex + 1}");
    if (outPath is not null)
    {
        Console.WriteLine($"  file   : {outPath} ({outFormat})");
    }
}

Console.WriteLine("\n=== merged transcript (speaker-attributed) ===");
Console.WriteLine(merged.ToString());
return 0;

string ArgAfter(string name)
{
    for (int i = 0; i < args.Length - 1; i++)
    {
        if (args[i] == name && !args[i + 1].StartsWith("--"))
        {
            return args[i + 1];
        }
    }

    return null;
}

static string Ts(double totalMs)
{
    var t = TimeSpan.FromMilliseconds(totalMs);
    var tenths = t.Milliseconds / 100;
    return t.TotalHours >= 1
        ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}.{tenths:0}"
        : $"{(int)t.TotalMinutes:00}:{t.Seconds:00}.{tenths:0}";
}

static string Dur(double totalSec)
{
    var t = TimeSpan.FromSeconds(totalSec);
    return t.TotalHours >= 1
        ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}"
        : $"{(int)t.TotalMinutes:00}:{t.Seconds:00}";
}

static string VttTs(double totalMs)
{
    var t = TimeSpan.FromMilliseconds(totalMs);
    return $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}.{t.Milliseconds:000}";
}

static string Normalize(string s) => Regex.Replace(s.Trim(), @"\s+", " ", RegexOptions.None, TimeSpan.FromSeconds(1));

static void WriteVttHeader(StreamWriter w, string audioPath, TimeSpan duration, string model, string lang, DiarizeMode diarize, int windowSecs)
{
    w.WriteLine("WEBVTT");
    w.WriteLine($"NOTE source: {audioPath}");
    w.WriteLine($"NOTE audio {duration:c} model {model} lang {lang} diarize {(diarize == DiarizeMode.DiarizeModeOff ? "off" : "on")} window {windowSecs}s generated {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
    w.WriteLine();
}

static void WriteJson(StreamWriter w, string lang, List<(double msStart, double msEnd, int speaker, string text)> segments)
{
    var fullText = string.Join(" ", segments.Select(s => s.text));
    w.WriteLine("{");
    w.WriteLine($"  \"language\": {JsonSerializer.Serialize(lang)},");
    w.WriteLine($"  \"text\": {JsonSerializer.Serialize(fullText)},");
    w.WriteLine("  \"segments\": [");
    for (int i = 0; i < segments.Count; i++)
    {
        var (msStart, msEnd, speaker, text) = segments[i];
        var comma = i < segments.Count - 1 ? "," : "";
        w.WriteLine($"    {JsonSerializer.Serialize(new
        {
            start = Math.Round(msStart / 1000.0, 3),
            end = Math.Round(msEnd / 1000.0, 3),
            speaker,
            text
        })}{comma}");
    }

    w.WriteLine("  ]");
    w.WriteLine("}");
}

static float[] LoadPcm(string path)
{
    try
    {
        return PcmExtensions.ReadWavToPcm(path);
    }
    catch (InvalidDataException)
    {
        return DecodeWithFfmpeg(path);
    }
}

static string ResolveTool(string name)
{
    var candidates = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
        .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
        .SelectMany(dir => new[]
        {
            Path.Combine(dir, name),
            Path.Combine(dir, name + (OperatingSystem.IsWindows() ? ".exe" : string.Empty))
        });
    return Path.GetFullPath(candidates.FirstOrDefault(File.Exists)
        ?? throw new InvalidOperationException($"'{name}' not found in PATH."));
}

static float[] DecodeWithFfmpeg(string path)
{
    var psi = new ProcessStartInfo
    {
        FileName = ResolveTool("ffmpeg"),
        Arguments = $"-v error -i \"{path}\" -ar 16000 -ac 1 -f f32le -",
        RedirectStandardOutput = true,
        UseShellExecute = false,
    };

    using var proc = Process.Start(psi)
        ?? throw new InvalidOperationException("Failed to start ffmpeg.");

    using var stdout = proc.StandardOutput.BaseStream;
    using var buffer = new MemoryStream();
    stdout.CopyTo(buffer);
    proc.WaitForExit();

    if (proc.ExitCode != 0)
    {
        throw new InvalidDataException(
            "ffmpeg could not decode the audio. Install ffmpeg, or convert to a 16 kHz mono 16-bit WAV first.");
    }

    var bytes = buffer.ToArray();
    if (bytes.Length % sizeof(float) != 0)
    {
        throw new InvalidDataException("ffmpeg returned a truncated PCM stream.");
    }

    var pcm = new float[bytes.Length / sizeof(float)];
    for (int i = 0; i < pcm.Length; i++)
    {
        pcm[i] = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(i * sizeof(float)));
    }

    return pcm;
}