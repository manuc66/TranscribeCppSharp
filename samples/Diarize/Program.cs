using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using TranscribeCppSharp;
using TranscribeCppSharp.Interop;

const string DefaultModel = "moss-transcribe-diarize-q4-k-m.gguf";

if (args.Length == 0 || args.Contains("--help") || args.Contains("-h"))
{
    Console.WriteLine("""
        Transcribe audio with speaker diarization (best quality on
        multi-speaker audio). Defaults: MOSS diarization model and forced
        English (robust on non-native speech).

        Usage: dotnet run --project samples/Diarize -- <audio> [model.gguf] [options]

          <audio>        WAV (16 kHz mono 16-bit) read directly; any other
                         format (ogg, mp3, m4a, …) is decoded with ffmpeg
                         (must be installed)
          [model.gguf]   default: test-models/moss-transcribe-diarize-q4-k-m.gguf
                         (fetch with WITH_DIARIZATION_MODEL=1 ./scripts/run-integration-tests.sh)

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
var modelPath = args.Length > 1 && !args[1].StartsWith("--")
    ? Path.GetFullPath(args[1])
    : Path.Combine(RepoRoot(), "test-models", DefaultModel);

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
if (!File.Exists(modelPath)) { Console.Error.WriteLine($"model not found: {modelPath}\n  fetch it with WITH_DIARIZATION_MODEL=1 ./scripts/run-integration-tests.sh"); return 1; }

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
}

Console.WriteLine("\n=== merged transcript (speaker-attributed) ===");
Console.WriteLine(merged.ToString());
return 0;

string ArgAfter(string name)
{
    for (int i = 1; i < args.Length - 1; i++)
    {
        if (args[i] == name && !args[i + 1].StartsWith("--"))
        {
            return args[i + 1];
        }
    }

    return null;
}

static string RepoRoot() => Path.GetFullPath(
    Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

static string Ts(double totalMs)
{
    var t = TimeSpan.FromMilliseconds(totalMs);
    var tenths = t.Milliseconds / 100;
    return t.TotalHours >= 1
        ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}.{tenths:0}"
        : $"{(int)t.TotalMinutes:00}:{t.Seconds:00}.{tenths:0}";
}

static string VttTs(double totalMs)
{
    var t = TimeSpan.FromMilliseconds(totalMs);
    return $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}.{t.Milliseconds:000}";
}

static string Normalize(string s) => Regex.Replace(s.Trim(), @"\s+", " ");

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

static float[] DecodeWithFfmpeg(string path)
{
    var psi = new ProcessStartInfo
    {
        FileName = "ffmpeg",
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