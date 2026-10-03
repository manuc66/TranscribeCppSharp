---
title: Long audio and diarization
nav_order: 7
---

# Long audio and speaker diarization

## Long audio

Audio longer than one window is split with a 1 s overlap and deduplicated;
`--chunk` sets the window size in seconds (default 300, lowered automatically if
the model reports a smaller maximum). A window must be longer than the overlap,
so `--chunk 1` is rejected rather than looping.

Audio is read one window at a time rather than loaded whole — the measured memory
effect is on [Audio input](audio-input.md).

## Diarization in the tool

Diarization is **on by default** and the default model is `moss-transcribe-diarize`
(667 MB), which attributes each segment to a speaker. Use `--no-diarize` to turn
it off. The `diarization supported:` line tells you whether the model in use can
attribute speakers at all: on the other models (Whisper, Parakeet, …) the native
library prints a warning and every segment is reported as `Speaker 0`. The
attribution then appears in the merged transcript and, per format, as
`Speaker N:`, `<v Speaker N>` or `"speaker":N`.

## Diarization in the library

Diarization is a **run-time** toggle, not a model-level setting: you ask for it
per run, and the model decides whether it can do it. Four entry points:

**1. Check whether the model can attribute speakers.** Do this first — it is the
only way to know before running:

```csharp
if (model.Supports(Feature.FeatureDiarization))
{
    Console.WriteLine("this model attributes speakers");
}
```

**2. Ask for it on a run.** The speakers then land on `Segments[].SpeakerId` and
in `SpeakerSegments` (speaker turns with their own times, which are a different
view of the same thing):

```csharp
var transcript = session.Run(pcm, r => r.WithDiarize(DiarizeMode.DiarizeModeOn));
foreach (var turn in transcript.SpeakerSegments)
{
    Console.WriteLine($"speaker {turn.SpeakerId}: {turn.Start} -> {turn.End}");
}
```

`DiarizeModeOff` asks for no attribution, and `DiarizeModeDefault` is the library
default (OFF for every family). With MOSS, the raw decode keeps the model's
inline markers (`[0.26][S01] …`) and `FullText` has them stripped, so the
transcript stays clean either way.

**3. Ask for it in batch.** `Batch.Run` takes the same run-params callback and
each `BatchResult` carries its own `SpeakerSegments`.

**4. Ask for it from the CLI.** On by default with the diarization model; see
[above](#diarization-in-the-tool).

What you get on a model that cannot: the upstream logs a warning and proceeds
with the model's default behavior — no exception, and every `SpeakerId` is 0.
Passing a non-DEFAULT mode to such a model is accepted, not rejected.

## Not available: streaming

`transcribe_stream_params` has no `diarize` field in transcribe.cpp v0.3.0, so
the streaming API cannot request speaker attribution; we do not invent one. The
alias list does contain a streaming diarization model
(`diar_streaming_sortformer_4spk-v2.1`, whose stream extension the wrapper
exposes), but we have not verified what its stream results contain, and the
native stream result carries no speaker rows we can read.

## What is verified, and what CI does not run

Verified end to end with MOSS Transcribe-Diarize Q4_K_M on the JFK sample:
`Supports` is true, ON attributes speaker 1 on every segment, OFF gives the same
text with no attribution, batch carries the same speaker segments, and the two
speakers of a two-voice sample come back as speakers 1 and 2. The tests are in
`tests/TranscribeCppSharp.Interop.Tests/DiarizationTests.cs`.

Those tests need the ~617 MB MOSS asset, so they are **opt-in and skipped in
CI** — a deliberate trade (a 617 MB download per runner, cached but invalidated
whenever the integration script changes). Run them with:

```bash
WITH_DIARIZATION_MODEL=1 ./scripts/run-integration-tests.sh
```

Until you do, nothing in CI would notice a regression that emptied
`SpeakerSegments` or broke the `SpeakerId` mapping.
