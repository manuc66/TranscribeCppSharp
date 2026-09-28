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
hardware before building a live pipeline. The setup below is old and slow on
purpose-by-accident, so treat the absolute values as a floor, not a ceiling.

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

The source clip is tiled (`--repeat`) to accumulate enough context to see the
per-feed cost trend. **That audio is degenerate.** It is not a substitute for a
real recording of the length you intend to stream.

## Setup these numbers came from

- Linux, 12 vCPU, 15 GiB RAM
- CPU: unknown model name; GGML resolved `libggml-cpu-haswell.so`
- GPU: `Vulkan1` = Quadro P2000 (NVIDIA, 2016) — selected by `--backend=auto`
- Model: `moonshine-streaming-tiny-Q8_0.gguf`, 50 462 816 bytes, sha256
  `930e4622ad3a24158b91406c30c977fa6a26b34cb32d6ac3e57cfb23383a869e`, fetched
  from `handy-computer/moonshine-streaming-tiny-gguf` at revision
  `f33fef628bc4d7ddb419384b1cf28ee83b662b06`
- Audio: `test-audio/jfk.wav`, 11.0 s, 16 kHz mono
- Chunk: 1000 ms

## Results

| Backend | decode interval | audio | RTF | max feed | crash |
|---------|-----------------|-------|-----|----------|-------|
| Vulkan | model default | 66 s | 0.569 | 3956 ms | no |
| Vulkan | 1000 ms | 88 s | 0.661 | 3511 ms | no |
| Vulkan | 1000 ms | 132 s | 1.280 | 3535 ms | no |
| CPU | model default | 66 s | 1.224 | 11 248 ms | no |
| CPU | 1000 ms | 66 s | 0.783 | 1614 ms | no |
| CPU | 1000 ms | 77 s | 1.297 | 6794 ms | no |
| CPU | 1000 ms | 88 s | — | — | **yes** |

Three things follow, and the third is the reason this page exists.

### 1. Perceived latency is good

First text at 37–74 ms, first committed text at 134–221 ms. A user sees a
committed word within a fifth of a second of speaking. The incremental API
delivers what it advertises.

### 2. The decode interval is the only real lever

`--min-decode-interval=1000` cut the worst feed from **11 248 ms to 1614 ms** and
RTF from 1.224 to 0.783 on the same 66 s of audio. It does not change
correctness — the transcript was identical — it only stops the model re-decoding
on every feed. `--commit=stable-prefix` performed similarly (RTF 0.740,
max feed 1578 ms). Without one of these two knobs the stream is not viable:
the default re-decodes too often to finish a chunk before the next arrives.

### 3. RTF degrades with stream length, and on CPU it crashes

This is the finding that matters, and it is the opposite of "streaming works".

Per-feed latency **climbs and does not come back down**. On the Vulkan run at
1000 ms chunks, 1 s of audio started costing 37 ms and settled around 700–1000 ms
— approaching its own chunk budget, on the same hardware that began at RTF 0.35.
Extending the same run to 132 s pushed RTF to **1.280: slower than real time**,
because the early cheap feeds no longer offset the late expensive ones. The
growth tracks accumulated context, not elapsed time, so a long live session
falls further behind the longer it runs.

On CPU, worse: RTF 1.224 at 66 s — already behind — and at 88 s of accumulated
audio the process **dies with `GGML_ASSERT(i01 >= 0 && i01 < ne01) failed`** in
`ggml-cpu/ops.cpp:5015`, an out-of-range KV-cache index. This is an abort inside
the native library, not a status code, so `StreamSession.Feed` never returns and
nothing in .NET catches it. A wrapped `try`/`catch` will not save a host process.

Reproduced with `--min-decode-interval=1000` at 88 s, with
`--commit=finalize`, and on Vulkan at 132 s. **Not** reproduced by raising
`--context` to 16384 or 32768, which failed identically, so a larger context
window is not the fix.

## What this means for a live pipeline

- **Set a decode interval or a commit policy.** There is no default that keeps up.
- **Do not trust a short RTF on a short clip.** RTF on this machine went 0.35 → 1.28
  purely by lengthening the stream. Measure over the length you will actually run.
- **A stream cannot currently be trusted to run for minutes on CPU.** An
  unrecoverable native abort is a hard blocker for a long-lived service, and it
  is upstream — `transcribe.cpp` 0.2.4, not the wrapper. Until it is fixed
  upstream, a live service needs process isolation: run the transcriber in a
  child process it can restart, because `Environment.FailFast`-grade failure
  cannot be intercepted.
- **Streaming is still worth it for the cascade.** The failure mode above is
  per-stream, on one session. The offline re-transcription pass
  ([Concurrency](concurrency.md)) is unaffected, which is the same reason a
  small-model-live / large-model-after design is resilient: if the live stage
  dies, the retained audio and the offline pass still produce a transcript.

## Running it

```bash
dotnet run --project samples/StreamingBench -c Release -- \
  test-models/moonshine-streaming-tiny-Q8_0.gguf \
  test-audio/jfk.wav 1000 --repeat=66 --min-decode-interval=1000 --trace
```

`--help` lists the rest. Get the model with
`tools/UpdateModelManifest` + the CLI's model store, or the pinned HuggingFace
URL in `models.json` — the bench takes a plain path and does not fetch.

## Unverified

- Every other streaming family in `models.json` (Parakeet, Nemotron, Voxtral,
  Sortformer) is untested here. Only Moonshine was measured.
- Only the 1000 ms chunk size appears in the table. The default sweep tries
  200/500/1000, but only 1000 ms was recorded before the CPU crash made longer
  runs unreliable.
- The per-feed growth is observed, not explained. Whether it is KV-cache growth,
  attention cost, or a leak in the streaming state is upstream's to answer.
- The crash is characterised by symptom and reproduction, not diagnosed.
