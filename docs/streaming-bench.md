---
title: Streaming benchmarks
nav_order: 11
---

# Streaming benchmarks

The wrapper ships no real-time numbers. Until now it had none: `cli.md` omits its
timing lines on purpose, and every streaming test in the suite is a
`SkippableFact` that returns early on `Status.ErrNotImplemented`, so nothing had
verified that a stream keeps up with live audio. The numbers below come from
`StreamingBench`, added to close that gap.

**They are one machine, not a specification.** Run the bench on your own
hardware before building a live pipeline. The bench now prints the load average
next to every result, because the first version of this page was measured on a
busy machine and had to be rewritten — see [Corrections](#corrections).

## What it measures

RTF alone answers the wrong question. A stream can average below 1.0 and still
be unusable if one feed takes longer than the audio it was given — the buffer
underruns at exactly that feed, and the transcript stutters. The bench reports:

| Metric | Why |
|--------|-----|
| **RTF** | total compute / audio duration. Below 1.0 to keep up on average. |
| **max feed** | the worst single `Feed()`, against that chunk's own audio duration. This is what underruns, not the average. |
| **time to first text** | wall time until `GetCurrentText()` is non-empty — the latency a user perceives at stream start. |
| **time to first commit** | wall time until `CommittedText` is non-empty — when the first stable word appears. |

> **The audio here is tiled, and that is the main threat to these results.**
> The source clip is repeated (`--repeat`) to accumulate enough context to see a
> trend. Tiled audio is degenerate: the model hears the same sentence over and
> over and keeps generating, so it decodes continuously with no silence to idle
> in. Real speech has pauses, and its token rate will be lower. How much of the
> growth below is context length and how much is just "a lot of tokens" is
> **not resolved** — it needs a real recording, which this repo does not have.
> Treat the absolute figures as an upper bound on cost, not a prediction for
> your audio.

## Setup these numbers came from

- Linux, 12 logical CPUs, 15 GiB RAM
- GPU: `Vulkan1` = Quadro P2000 (NVIDIA, 2016), which `--backend=auto` selected
- Model: `moonshine-streaming-tiny-Q8_0.gguf`, 50 462 816 bytes, sha256
  `930e4622ad3a24158b91406c30c977fa6a26b34cb32d6ac3e57cfb23383a869e`, fetched
  from `handy-computer/moonshine-streaming-tiny-gguf` at revision
  `f33fef628bc4d7ddb419384b1cf28ee83b662b06`
- Audio: `test-audio/jfk.wav`, 11.0 s, 16 kHz mono, tiled to length
- Chunk: 1000 ms, machine load 0.03–0.67 at start of each run

## Results

| Backend | decode interval | audio | RTF | max feed | crash |
|---------|-----------------|-------|-----|----------|-------|
| Vulkan | model default | 33 s | 0.218 | 553 ms | no |
| Vulkan | model default | 66 s | 0.510 | 3507 ms | no |
| Vulkan | 1000 ms | 88 s | *invalid* | 3529 ms | no (hit frame limit) |
| Vulkan | 1000 ms | 132 s | *invalid* | 3577 ms | no (hit frame limit) |
| Vulkan | 1000 ms | 198 s | *invalid* | 3228 ms | no (hit frame limit) |
| CPU | model default | 66 s | 0.681 | 1348 ms | no |
| CPU | 1000 ms | 66 s | 0.677 | 1407 ms | no |
| CPU | stable-prefix commit | 66 s | 0.667 | 1339 ms | no |
| CPU | 1000 ms | 77 s | 0.980 | 5145 ms | no |
| CPU | 1000 ms | 88 s | — | — | **yes, at feed 83** |

Rows marked *invalid* ran past the model's 81.9 s frame ceiling: audio beyond
that point is silently ignored, so the RTF divides compute by audio that was
never transcribed. Only the 33 s and 66 s rows are valid measurements of
processing speed. The max-feed column is still informative — it is the worst
single `Feed()` before the limit kicked in.

Three things follow, and the third is why this page exists.

### 1. Perceived latency is good, and stays good

First text at 34–69 ms, first committed word at 124–211 ms, across every run and
both backends. A user sees a committed word within a fifth of a second of
speaking. The incremental API delivers what it advertises.

### 2. The commit knobs do essentially nothing (on the 66 s that is valid)

The 88 s, 132 s and 198 s rows above are invalid, so any conclusion about
knobs on long audio is unsupported. On the 66 s that is valid, all three settings
land within noise of each other:

At 66 s on CPU, all three settings land within noise of each other:

| Setting | RTF | max feed |
|---------|-----|----------|
| model default | 0.681 | 1348 ms |
| `--min-decode-interval=1000` | 0.677 | 1407 ms |
| `--commit=stable-prefix` | 0.667 | 1339 ms |

Whatever dominates the cost is not the decode cadence. There is currently **no
knob on this wrapper that makes a stream keep up**; the earlier version of this
page claimed the decode interval was the lever, and that was wrong — see
[Corrections](#corrections).

### 3. The real finding: Moonshine streaming tiny has an 82-second ceiling

This is the finding that matters, and it came from re-measuring the 198 s run
on an idle machine. The earlier version of this section claimed a monotonic
growth plateauing at ~5.5x real time. That was an artefact of two things
stacked together, and it is wrong:

1. **The model stops processing at 81.9 s of audio.** Moonshine streaming tiny
   has a hard frame limit of 4096 frames, which is 81.9 s at 16 kHz. Audio fed
   after that is silently ignored — `Feed()` returns in 0 ms, and the transcript
   stops growing. The "plateau" was the model doing nothing. The bench now
   records the warning the native library emits:
   `input reached the model's 4096-frame limit (81.9 s of audio); later audio is
   ignored`. This is an architectural ceiling of this model, not a tuning problem.
2. **The degenerate tiled input drives the growth.** The model hears the same
   sentence 18 times and keeps generating tokens until it hits its 152-token
   budget, then drops repeating blocks. That is why the per-feed cost climbs: it
   is generating more and more before each commit, not because the KV cache is
   growing. Real speech with pauses would generate far fewer tokens.

**What the clean measurement actually shows** (first 83 feeds, the only ones
that do work):

| feeds (audio) | mean per feed |
|---------------|---------------|
| 1–20 (0–20 s) | 137 ms |
| 21–40 (20–40 s) | 573 ms |
| 41–60 (40–60 s) | 1313 ms |
| 61–82 (60–82 s) | 713 ms |

RTF on the processed portion: **0.679**, consistent with the 66 s run (0.681).
The growth is not monotonic — it peaks around 40–60 s then partially recovers —
and it is probably driven by token count, not context length. The 198 s run's
RTF of 0.289 is meaningless: it averages 57 s of compute over 198 s of audio,
115 of which were never processed.

**The 82-second ceiling is the actionable number.** A live stream on this model
must be reset or restarted before 82 s, or a different model must be chosen.
There is no configuration that extends it: `--context=4096` and `--context=8192`
were tried on the CPU path and did not change the limit.

On the CPU backend the growth has a worse ending. The same curve, from the run
that aborted:

| feeds | mean per feed |
|-------|---------------|
| 1–10 | 74 ms |
| 31–40 | 743 ms |
| 61–70 | 1166 ms |
| 71–73 | 1075 ms |
| 74–79 | 4508 ms |
| 80–82 | 1652 ms |
| 83 | **abort** |

**Feed 83 aborts the process at 82 s of accumulated audio** — exactly at the
model's frame limit. The abort is likely the native code failing to handle the
boundary cleanly rather than a genuine overflow:

```
GGML_ASSERT(i01 >= 0 && i01 < ne01) failed   ggml-cpu/ops.cpp:5015
```

An out-of-range KV-cache index, raised inside the native library rather than
returned as a status code. `StreamSession.Feed` never returns, `Complete()` is
never reached, and no `try`/`catch` in .NET intercepts it. The exit is SIGABRT.

Reproduced across five attempts on an idle machine, and also with
`--commit=finalize`. **Not** reproduced by raising the session context to 16384
or 32768, which failed identically, so a larger context window is not the fix.
The Vulkan backend did not abort at 198 s; it just got slower.

### 4. Memory grows with the stream, and ending the utterance bounds it

The pages above measure time. Memory is the other half, and it grows too — for
the same reason, the working set of the current utterance. On this machine (Linux
x64, CPU backend, the Moonshine model above), feeding one stream and sampling the
process RSS:

| Audio fed | one utterance | utterance closed every 30 s |
|-----------|---------------|-----------------------------|
| 10 s | 204 MiB | 204 MiB |
| 20 s | 237 MiB | 237 MiB |
| 30 s | 254 MiB | 255 MiB |
| 40 s | 284 MiB | 243 MiB |
| 66 s | 362 MiB | — |
| 80 s | 389 MiB, then SIGABRT | 251 MiB |
| 150 s | (aborted) | 258 MiB (5 rotations) |

Roughly **2.8 MiB per second of audio** (2.6–2.9 across the points above), and it
is not the managed heap: the .NET heap stayed flat at ~0.8 MiB throughout. Three
controls pin it to the audio duration and nothing else:

- **Feed size does not matter.** 1320 feeds of 800 samples and 66 feeds of
  16 000 samples reach the same RSS at 66 s.
- **Content does not matter.** Feeding digital silence grows it exactly as speech
  does, so it is not the decode output.
- **Reading text does not matter.** The same slope with and without
  `StreamGetText()` after every feed.

It is not a cache the caller can cap, either: the session reports
`effective_n_ctx = 0` and `max_kv_bytes = 0` for this family, so
`transcribe_session_params::n_ctx` is ignored — `WithContextSize(4096)` and
`WithContextSize(2048)` grow the same. The memory is released when the stream
ends (`StreamSession.Dispose` → session free): four Start/Stop cycles ended at
58–78 MiB, with no accumulation across them.

The lever is **ending the utterance**. The streaming API is per-utterance:
`transcribe_stream_reset` clears the family's per-utterance state, and
`transcribe_stream_begin` starts a new one reusing the stream's buffers. Closing
the utterance (finalize, then reset, then begin) stops the working set from
growing with the session; the numbers in the right-hand column are that code
running. It also sidesteps the ~82 s CPU abort above, because no single utterance
reaches it. The cost is the model's audio context across the boundary, at a
point of the caller's choosing. This is what the desktop app's **Utterance (s)**
field does (see [Desktop app](ui.md#streaming)); the wrapper itself takes no
position on it.

**One machine, one model, one backend.** Vulkan, other families and real
long-form audio were not measured; only `moonshine-streaming-tiny` on the CPU was.

## What this means for a live pipeline

- **Do not trust a short RTF on a short clip.** RTF went 0.218 → 2.705 purely by
  lengthening the stream on one machine. Measure over the length you will run.
- **There is no tuning knob that saves this.** Default, decode interval and
  stable-prefix commit are the same number. A stream that falls behind is not
  misconfigured.
- **A stream cannot run past 82 s on this model, full stop.** Moonshine
  streaming tiny stops processing at 81.9 s (4096 frames) and ignores the rest.
  The CPU abort at feed 83 (82 s) lands exactly on that boundary, which suggests
  the native code does not handle the limit cleanly — but even if it did not
  crash, the audio after 82 s would be silently dropped. A live pipeline must
  reset or restart the stream before 82 s, or use a model without this ceiling.
- **RTF on the valid range is ~0.68.** The 1.287 and 2.705 figures were
  averages over ignored audio and are meaningless. A 30-second demo looks fast;
  an 80-second stream is where this model stops, not where it slows down.
- **The CPU abort at ~82 s is real and reproducible** (five attempts, idle
  machine). It is upstream — transcribe.cpp 0.2.4 and ggml, not the wrapper —
  and no amount of wrapper code intercepts an uncatchable native abort. A live
  service needs process isolation: run the transcriber in a child process it can
  restart.
- **The cascade still pays.** The failure is per-stream, on one session, and the
  offline re-transcription pass
  ([Concurrency](concurrency.md)) is unaffected. If the live stage dies, the
  retained audio and the offline pass still produce a transcript — which is the
  strongest argument for the small-live / large-after design.

## Running it

```bash
dotnet run --project samples/StreamingBench -c Release -- \
  test-models/moonshine-streaming-tiny-Q8_0.gguf \
  test-audio/jfk.wav 1000 --repeat=18 --min-decode-interval=1000 --trace
```

`--help` lists the rest. The model must already be on disk; the bench takes a
path and does not fetch. To try real audio instead of a tiled clip, decode it
once with the CLI (`ffmpeg -i real.wav -ar 16000 -ac 1 real16k.wav`) — **that is
the measurement that would settle the tiling caveat above, and it has not been
done.**

## Corrections

An earlier revision of this page was measured while builds and the test suite
were running on the same 12-core box, and it was wrong in both directions. Kept
here because the mistake is the useful part:

- It claimed a decode interval cut the worst feed from 11 248 ms to 1614 ms and
  RTF from 1.224 to 0.783, on the evidence of one loaded pair of runs. On an
  idle machine the two configurations are indistinguishable (0.681 vs 0.677).
  The 11 248 ms figure was contention, not the model.
- It quoted the 33 s RTF as 0.346 with a 2586 ms worst feed. The idle-machine
  value is 0.218 with a 553 ms worst feed.
- The CPU abort at ~82 s, the RTF growth, and the lack of any useful knob all
  **survive** re-measurement. Those were real.
- **The 81.9 s frame ceiling was missed entirely in the first version.** The
  198 s run was read as "monotonic growth plateauing at ~5.5x" when it was
  actually the model hitting its 4096-frame limit and returning 0 ms. The bench
  now prints the native warning that announces this, so the limit is visible in
  the output rather than inferred. The "~5.5x real time" claim is withdrawn.
- **The 198 s run's RTF of 0.289 was meaningless.** It averaged 57 s of compute
  over 198 s of audio, 115 s of which were never processed. Only the 33 s and
  66 s rows are valid speed measurements.

The bench now prints the load average beside every result, and flushes each line,
so a run that aborts leaves evidence instead of an empty log.

## Unverified

- Every other streaming family in `models.json` (Parakeet, Nemotron, Voxtral,
  Sortformer) is untested. Only Moonshine was measured.
- Only a 1000 ms chunk was recorded. The default sweep tries 200/500/1000.
- Real long-form audio was never tested — see the tiling caveat at the top.
- Whether the growth is context length or token count is unresolved.
- The CPU abort is characterised by symptom and reproduction, not diagnosed.
- The memory figures in §4 are one model, one backend. The slope per family is
  unmeasured, and so is the accuracy cost of closing an utterance at a fixed
  interval — a word split across a boundary can be broken, and whether that hurts
  the transcript was not measured.