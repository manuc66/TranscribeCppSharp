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
| Vulkan | 1000 ms | 88 s | 0.664 | 3529 ms | no |
| Vulkan | 1000 ms | 132 s | 1.287 | 3577 ms | no |
| Vulkan | 1000 ms | 198 s | 2.705 | 7331 ms | no |
| CPU | model default | 66 s | 0.681 | 1348 ms | no |
| CPU | 1000 ms | 66 s | 0.677 | 1407 ms | no |
| CPU | stable-prefix commit | 66 s | 0.667 | 1339 ms | no |
| CPU | 1000 ms | 77 s | 0.980 | 5145 ms | no |
| CPU | 1000 ms | 88 s | — | — | **yes, at feed 83** |

Three things follow, and the third is why this page exists.

### 1. Perceived latency is good, and stays good

First text at 34–69 ms, first committed word at 124–211 ms, across every run and
both backends. A user sees a committed word within a fifth of a second of
speaking. The incremental API delivers what it advertises.

### 2. The commit knobs do essentially nothing

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

### 3. Per-feed cost grows with stream length, saturating far above real time

This is the finding that matters. Per-feed latency, Vulkan backend, 198 s run,
mean per band of 19 feeds:

| feeds (audio) | mean per feed |
|---------------|---------------|
| 1–19 (0–19 s) | 104 ms |
| 20–38 (19–38 s) | 461 ms |
| 39–57 (38–57 s) | 726 ms |
| 58–76 (57–76 s) | 932 ms |
| 77–95 (76–95 s) | 1306 ms |
| 96–114 (95–114 s) | 2221 ms |
| 115–133 (114–133 s) | 3396 ms |
| 134–152 (133–152 s) | 4786 ms |
| 153–171 (152–171 s) | 5924 ms |
| 172–190 (171–190 s) | 5773 ms |
| 191–198 (190–198 s) | 5509 ms |

The cost rises 55x from the first band to the last, then **levels off around
5 500 ms** — about 5.5x slower than the audio it is fed. That plateau is the
part that matters: it is a floor, not a startup transient. On this hardware a
Moonshine streaming model cannot process live audio faster than roughly 5.5x
real time, no matter how the stream is chunked or tuned.

RTF therefore climbs with duration — 0.218 at 33 s, 0.510 at 66 s, 1.287 at
132 s, 2.705 at 198 s — and **a short clip flatters it completely**. A
30-second demo looks like RTF 0.2. A two-minute meeting does not.

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

A second degradation regime opens at feed 74 (~73 s) at 4–5x the plateau cost,
and then **feed 83 aborts the process at 82 s of accumulated audio**:

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

## What this means for a live pipeline

- **Do not trust a short RTF on a short clip.** RTF went 0.218 → 2.705 purely by
  lengthening the stream on one machine. Measure over the length you will run.
- **There is no tuning knob that saves this.** Default, decode interval and
  stable-prefix commit are the same number. A stream that falls behind is not
  misconfigured.
- **A stream cannot currently be trusted to run for minutes on CPU**, and on
  this hardware cannot run faster than ~5.5x real time on either backend. The
  CPU abort is upstream — transcribe.cpp 0.2.4 and ggml, not the wrapper — and
  no amount of wrapper code intercepts an uncatchable native abort. A live
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

The bench now prints the load average beside every result, and flushes each line,
so a run that aborts leaves evidence instead of an empty log.

## Unverified

- Every other streaming family in `models.json` (Parakeet, Nemotron, Voxtral,
  Sortformer) is untested. Only Moonshine was measured.
- Only a 1000 ms chunk was recorded. The default sweep tries 200/500/1000.
- Real long-form audio was never tested — see the tiling caveat at the top.
- Whether the growth is context length or token count is unresolved.
- The CPU abort is characterised by symptom and reproduction, not diagnosed.