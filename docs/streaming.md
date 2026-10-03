---
title: Real-time streaming
nav_order: 9
---

# Real-time streaming

`StreamSession` turns a live audio source into a growing transcript: you feed it
chunks as they arrive and read the current text at any point. The wrapper exposes
the upstream stream extension for the models that have one.

<!-- @readme streaming-transcription -->
```csharp
stream.Begin();
int chunkSize = 16000; // 1 second
for (int i = 0; i < pcm.Length; i += chunkSize)
{
    int length = Math.Min(chunkSize, pcm.Length - i);
    var chunk = pcm.AsSpan(i, length);
    stream.Feed(chunk);
}
stream.Complete();
var text = stream.GetCurrentText();
```
<!-- @end streaming-transcription -->

`Begin()` resets the stream, `Feed()` submits one chunk of 16 kHz mono float PCM
and returns a `StreamUpdateResult`, `Complete()` flushes what is buffered and also
returns a `StreamUpdateResult`, and `GetCurrentText()` returns a `StreamTextResult`
carrying the current text as *full*, *committed* and *tentative*. Reading the
current text before `Complete()` gives a partial transcript — that is the point of
the incremental API, and it is the reason the CLI does not expose streaming
([what the CLI does not do](cli.md#what-the-cli-does-not-do)).

## Before you build on it

Set a decode interval or a commit policy. With the model default, the worst
single `Feed()` measured 11 seconds against a one-second chunk — the stream
re-decodes too often to keep up. `StreamParamsBuilder.WithCommitPolicy` and the
per-family extension builders are the levers, and
[Streaming benchmarks](streaming-bench.md) has the measured effect of each.

Also measured, and not what you would hope: RTF degrades as a stream grows
(0.35 → 1.28 on one machine, purely by lengthening the clip), and on the CPU
backend a stream of about 88 seconds of audio aborts the process inside ggml.
That abort is not catchable from .NET. Read the page before assuming a stream
can run for minutes.

## Two limits worth knowing

- **No speaker attribution.** `transcribe_stream_params` has no `diarize` field
  in transcribe.cpp v0.3.0, so a stream cannot request it; we do not invent one.
  The full reasoning is under [Not available: streaming](diarization.md#not-available-streaming).
- **Not thread-safe.** A `StreamSession` is a view over a `Session` and shares
  its state — see [Concurrency](concurrency.md#thread-safety).

## Ordering and disposal

A stream keeps native state (including a KV cache) for the session's lifetime.
Dispose the `StreamSession` before its `Model`, and do not rely on the GC
finalizer for cleanup — see [Dispose discipline](concurrency.md#dispose-discipline).
