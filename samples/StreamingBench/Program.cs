// Real-time factor bench for a transcribe.cpp streaming model.
//
// Measures what a live pipeline actually needs, which RTF alone does not say:
//   - RTF                 : total compute / audio duration. Must be < 1 to keep up.
//   - max feed latency    : the WORST single feed, against the chunk's own audio
//                           duration. A single commit spike longer than the
//                           chunk it is fed from underruns the buffer, even at
//                           a good average RTF.
//   - time to first text  : wall time until GetCurrentText() returns anything.
//                           This is the latency a user perceives at stream start.
//   - time to first commit: wall time until CommittedText is non-empty. This is
//                           when the first stable word appears.
//
// Nothing here is a benchmark claim until it is run; the output IS the claim.
//
// This exists because the wrapper had no real-time number at all. `docs/cli.md`
// deliberately omits the timing lines, and every streaming test in the suite is
// a SkippableFact that returns early on Status.ErrNotImplemented, so nothing
// verified that a stream keeps up with live audio. Run this on your own
// hardware before designing a live pipeline around it; the numbers in
// docs/streaming-bench.md are one machine and are not portable.
//
// The --repeat flag tiles the source clip. That is enough to build up enough
// context to see the per-feed cost trend, but it is degenerate audio and is
// NOT a substitute for a real recording of the length you intend to stream.

using System.Diagnostics;
using System.Globalization;
using System.Text;
using TranscribeCppSharp;
using TranscribeCppSharp.Interop;
using StreamingBench;

const int SampleRate = 16000;

// Chunk sizes to try when none are given: sub-second for a responsive UI, and
// one second to compare against the models' own internal framing.
var defaultChunks = new List<int> { 200, 500, 1000 };

// Flush every line. One of the things this bench exists to observe is a native
// abort, and an abort discards whatever the runtime had buffered — without this
// a crashing run produces an empty log and no evidence at all.
Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), Encoding.UTF8) { AutoFlush = true });

if (args.Length < 2)
{
    Console.Error.WriteLine("usage: StreamingBench <model.gguf> <audio.wav> [chunkMs,chunkMs,...] [options]");
    Console.Error.WriteLine("  --repeat=N                 tile the source clip N times to build context");
    Console.Error.WriteLine("  --min-decode-interval=MS   Moonshine ext: ms between decodes");
    Console.Error.WriteLine("  --commit=auto|finalize|stable-prefix");
    Console.Error.WriteLine("  --context=N                session KV context size");
    Console.Error.WriteLine("  --threads=N                session inference threads");
    Console.Error.WriteLine("  --backend=auto|cpu|vulkan|metal");
    Console.Error.WriteLine("  --trace                    print every per-feed latency");
    return 2;
}

var modelPath = args[0];
var audioPath = args[1];
var chunkSizes = new List<int>();
int repeat = 1;
int? minDecodeInterval = null;
int? threads = null;
string? backend = null;
var trace = false;
string? commit = null;
int? contextSize = null;

foreach (var a in args.Skip(2))
{
    if (a.StartsWith("--repeat=", StringComparison.Ordinal))
    {
        repeat = int.Parse(a["--repeat=".Length..], CultureInfo.InvariantCulture);
    }
    else if (a.StartsWith("--min-decode-interval=", StringComparison.Ordinal))
    {
        minDecodeInterval = int.Parse(a["--min-decode-interval=".Length..], CultureInfo.InvariantCulture);
    }
    else if (a.StartsWith("--threads=", StringComparison.Ordinal))
    {
        threads = int.Parse(a["--threads=".Length..], CultureInfo.InvariantCulture);
    }
    else if (a.StartsWith("--context=", StringComparison.Ordinal))
    {
        contextSize = int.Parse(a["--context=".Length..], CultureInfo.InvariantCulture);
    }
    else if (a.StartsWith("--commit=", StringComparison.Ordinal))
    {
        commit = a["--commit=".Length..];
    }
    else if (a == "--trace")
    {
        trace = true;
    }
    else if (a.StartsWith("--backend=", StringComparison.Ordinal))
    {
        backend = a["--backend=".Length..];
    }
    else if (a.Contains(','))
    {
        foreach (var part in a.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            chunkSizes.Add(int.Parse(part, CultureInfo.InvariantCulture));
        }
    }
    else
    {
        chunkSizes.Add(int.Parse(a, CultureInfo.InvariantCulture));
    }
}

if (chunkSizes.Count == 0)
{
    chunkSizes = defaultChunks;
}

// Pin the thread count when asked, so a run is comparable with the next one.
if (threads is { } t)
{
    ThreadPool.SetMinThreads(t + 4, t + 4);
    Console.WriteLine($"thread pool min = {t + 4}");
    if (backend is null)
    {
        backend = "cpu";
    }
}

if (!File.Exists(modelPath))
{
    Console.Error.WriteLine($"model not found: {modelPath}");
    return 2;
}

if (!File.Exists(audioPath))
{
    Console.Error.WriteLine($"audio not found: {audioPath}");
    return 2;
}

var pcm = PcmExtensions.ReadWavToPcm(audioPath);
if (pcm.Length == 0)
{
    Console.Error.WriteLine("audio is empty");
    return 2;
}

// Tile the source so a short clip still gives a stable RTF over enough chunks.
var total = pcm.Length * repeat;
var tiled = new float[total];
for (var i = 0; i < repeat; i++)
{
    Array.Copy(pcm, 0, tiled, i * pcm.Length, pcm.Length);
}

var audioSeconds = (double)total / SampleRate;
Console.WriteLine($"model      : {Path.GetFileName(modelPath)}");
Console.WriteLine($"audio      : {Path.GetFileName(audioPath)} x{repeat} = {audioSeconds:F2}s @ {SampleRate}Hz mono");
Console.WriteLine($"backend    : {backend ?? "auto"}");
Console.WriteLine($"min decode : {(minDecodeInterval is null ? "(model default)" : minDecodeInterval + "ms")}");
Console.WriteLine($"machine    : {Environment.ProcessorCount} logical CPUs, load {LoadAverage()}, utc {DateTime.UtcNow:yyyy-MM-dd HH:mm}");
Console.WriteLine($"             (a loaded machine inflates every figure below; compare runs only at a similar load)");
Console.WriteLine();

var swLoad = Stopwatch.StartNew();
using var model = Model.Load(modelPath, p =>
{
    p.WithBackend(backend switch
    {
        null => BackendRequest.BackendAuto,
        "cpu" => BackendRequest.BackendCpu,
        "vulkan" => BackendRequest.BackendVulkan,
        "metal" => BackendRequest.BackendMetal,
        _ => throw new ArgumentException($"unknown backend '{backend}'"),
    });
});
swLoad.Stop();

var caps = model.GetCapabilities();
Console.WriteLine($"load       : {swLoad.ElapsedMilliseconds} ms");
Console.WriteLine($"arch       : {model.Architecture} / {model.Variant}");
Console.WriteLine($"backend    : {model.Backend}");
Console.WriteLine($"device     : {model.Device?.Name ?? "(none)"}");
Console.WriteLine($"supportsStream : {caps.SupportsStreaming}");
Console.WriteLine($"nativeSampleRate: {caps.NativeSampleRate} Hz   <-- must be 16000");
Console.WriteLine($"maxTimestampKind: {caps.MaxTimestampKind}   <-- gate 3 for any time-based UI");
Console.WriteLine($"maxAudioMs      : {caps.MaxAudioMs}");
Console.WriteLine();

Console.WriteLine("chunk   feeds   wall      audio    RTF    maxFeed   budget  verdict   firstTxt  firstCommit");
Console.WriteLine("------  -----   -------   ------  -----   -------   ------  --------  --------  -----------");

var overallOk = true;
foreach (var chunkMs in chunkSizes)
{
    var result = RunOne(model, tiled, chunkMs, minDecodeInterval, threads, trace, commit, contextSize);
    overallOk &= result.UnderBudget;

    var chunkSeconds = chunkMs / 1000.0;
    Console.WriteLine(
        $"{chunkMs,6} ms  {result.Feeds,5}  {result.Wall,6:F2} s  {audioSeconds,6:F2} s  {result.Rtf,5:F3}  " +
        $"{result.MaxFeedMs,6:F1} ms  {chunkSeconds * 1000,6:F0} ms  {result.Verdict,-8}  " +
        $"{result.FirstTextMs,7:F0} ms  {result.FirstCommitMs,10:F0} ms");
}

Console.WriteLine();
Console.WriteLine(overallOk
    ? "Every configured chunk kept up with its own audio duration (max feed < budget)."
    : "AT LEAST ONE CHUNK SIZE COULD NOT KEEP UP: max feed exceeded the chunk's audio duration.");
return overallOk ? 0 : 1;

static Result RunOne(Model model, float[] pcm, int chunkMs, int? minDecodeInterval, int? threads, bool doTrace, string? commit, int? contextSize)
{
    const int SampleRate = 16000;
    var chunkSamples = chunkMs * SampleRate / 1000;
    if (chunkSamples <= 0)
    {
        throw new ArgumentException("chunk size produced zero samples");
    }

    using var session = model.CreateSession(p =>
    {
        if (threads is { } n)
        {
            p.WithThreads(n);
        }

        if (contextSize is { } cs)
        {
            p.WithContextSize(cs);
        }
    });
    using var stream = session.CreateStream();

    stream.Begin(streamConfig: sp =>
    {
        if (minDecodeInterval is { } mdi)
        {
            sp.WithMoonshineExt(new MoonshineExtBuilder().WithMinDecodeIntervalMs(mdi));
        }

        if (commit is not null)
        {
            sp.WithCommitPolicy(commit switch
            {
                "auto" => StreamCommitPolicy.StreamCommitAuto,
                "finalize" => StreamCommitPolicy.StreamCommitOnFinalize,
                "stable-prefix" => StreamCommitPolicy.StreamCommitStablePrefix,
                _ => throw new ArgumentException($"unknown commit policy '{commit}'"),
            });
        }
    });

    var result = new Result();
    var swFeed = Stopwatch.StartNew();
    var maxFeed = 0.0;
    var feeds = 0;
    var trace = new List<double>();

    for (var offset = 0; offset < pcm.Length; offset += chunkSamples)
    {
        var length = Math.Min(chunkSamples, pcm.Length - offset);
        var swFeedOne = Stopwatch.StartNew();
        var update = stream.Feed(pcm.AsSpan(offset, length));
        swFeedOne.Stop();

        var ms = swFeedOne.Elapsed.TotalMilliseconds;
        trace.Add(ms);
        if (ms > maxFeed)
        {
            maxFeed = ms;
        }

        feeds++;

        // Emit the per-feed latency as it happens rather than at the end. A
        // native abort discards the rest of the run, and the interesting feed
        // is the one that aborted — which a trace printed on exit never shows.
        if (doTrace)
        {
            Console.WriteLine($"  feed {feeds,4} @ {offset / SampleRate,5:F1}s  {ms,7:F1} ms");
        }

        // Poll the text the way a live UI would, and record when it first
        // becomes non-empty. Polling costs a native call, so it is part of the
        // cost and is included in the wall time on purpose.
        if (double.IsNaN(result.FirstTextMs) || double.IsNaN(result.FirstCommitMs))
        {
            var text = stream.GetCurrentText();
            if (double.IsNaN(result.FirstTextMs) && text.FullText.Length > 0)
            {
                result.FirstTextMs = swFeed.Elapsed.TotalMilliseconds;
            }

            if (double.IsNaN(result.FirstCommitMs) && text.CommittedText.Length > 0)
            {
                result.FirstCommitMs = swFeed.Elapsed.TotalMilliseconds;
            }
        }

        _ = update;
    }

    var swFinal = Stopwatch.StartNew();
    var final = stream.Complete();
    swFinal.Stop();
    var textFinal = stream.GetCurrentText();

    swFeed.Stop();

    var audioSeconds = (double)pcm.Length / SampleRate;
    result.Feeds = feeds;
    result.Wall = swFeed.Elapsed.TotalSeconds;
    result.Rtf = result.Wall / audioSeconds;
    // The final flush is bounded by the audio still buffered, not by the chunk,
    // so it is reported but excluded from the per-chunk budget check.
    result.MaxFeedMs = maxFeed;
    result.UnderBudget = maxFeed < chunkMs;

    var over = maxFeed - chunkMs;
    result.Verdict = result.UnderBudget
        ? "ok"
        : $"OVER +{over:F0}ms";

    Console.WriteLine($"  [{chunkMs}ms] maxFeed {maxFeed:F1}ms (budget {chunkMs}ms) | " +
                      $"finalize {swFinal.ElapsedMilliseconds}ms | isFinal={final.IsFinal} " +
                      $"| words={stream.CommittedWordCount} segs={stream.CommittedSegmentCount} rev={stream.Revision}");
    Console.WriteLine($"  [{chunkMs}ms] committed: {Truncate(textFinal.CommittedText, 90)}");
    Console.WriteLine($"  [{chunkMs}ms] tentative: {Truncate(textFinal.TentativeText, 90)}");

    if (doTrace)
    {
        Console.WriteLine($"  [{chunkMs}ms] per-feed ms: {string.Join(" ", trace.Select(x => x.ToString("F0", CultureInfo.InvariantCulture)))}");
    }

    Console.WriteLine();

    return result;
}

static string Truncate(string s, int n) =>
    s.Length <= n ? s : s[..n] + " ...";

// Printed next to the results so a figure can be judged against the load it was
// taken under. A real-time factor measured on a busy box is not a property of
// the model, and that is exactly the mistake these numbers exist to prevent.
static string LoadAverage() =>
    File.Exists("/proc/loadavg")
        ? string.Join(" ", File.ReadAllText("/proc/loadavg").Split(' ')[..3])
        : "unavailable";

// Top-level statements cannot live inside a namespace, so the aggregated result
// stays at the end of the global statements as the one type this file needs.
namespace StreamingBench
{
    sealed class Result
    {
        public int Feeds;
        public double Wall;
        public double Rtf;
        public double MaxFeedMs;
        public bool UnderBudget;
        public string Verdict = "?";
        public double FirstTextMs = double.NaN;
        public double FirstCommitMs = double.NaN;
    }
}
