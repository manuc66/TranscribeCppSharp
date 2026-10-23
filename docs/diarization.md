---
title: Long audio and diarization
nav_order: 7
---

# Long audio and speaker diarization

## Long audio

Audio longer than one window is split with a 1 s overlap and deduplicated;
`--chunk` sets the window size in seconds (default 300, lowered automatically if
the model reports a smaller maximum). A window must be longer than the overlap,
so `--chunk 1` is rejected rather than looping. The seam keeps a segment that
starts up to 500 ms before the previous one ended, rather than dropping the audio
after it, so that much can be transcribed twice — the tolerance and its reason
are on `TranscriptMerger.SeamToleranceMs`.

Audio is read one window at a time rather than loaded whole — the measured memory
effect is on [Audio input](audio-input.md).

## Diarization in the tool

Diarization is **on by default** and the default model is `moss-transcribe-diarize`
(668 MB), which attributes each segment to a speaker. Use `--no-diarize` to turn
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

## Diarization models and the DIARIZE role

transcribe.cpp 0.4 gives diarization a **role** of its own: a model whose product
is speaker turns (Sortformer) serves `TranscribeRole.Diarize`, not
`TranscribeRole.Asr`, and no longer runs through the ASR session.
`Model.CreateSession` on such a model throws with `UNSUPPORTED_ROLE`; use
`Model.CreateDiarizeSession`, passing `SortformerDiarizeExtBuilder` for the
latency/quality preset. `Model.Roles` tells you which a model is before you try.

The speaker-attributing **ASR** models (MOSS, granite, multitalker parakeet) are
unchanged: they still attribute speakers through the run-time toggle above.

There is **no streaming diarization**: upstream has no stream entry point for the
DIARIZE role, and the streaming Sortformer extension that existed in 0.3 was
removed in 0.4. `transcribe_stream_params` has no `diarize` field in transcribe.cpp
v0.4.0 either, so the streaming API cannot request speaker attribution; we do not
invent one.

## Combining a diarizer with a transcriber

The DIARIZE-role models (Sortformer) produce speaker turns and no text, so their use is to be
paired with a transcriber that cannot attribute speakers itself. transcribe.cpp has **no
combined entry point**: you run the two models separately and join the results.

The app does exactly that. On the Transcription tab, choosing **Speech to text + speakers** is
the one request for attribution. A model that diarizes itself satisfies it in one pass; a model
that cannot shows a **Diarizer model** picker, offering the downloaded DIARIZE-role models and
preselecting the only one. With one chosen, the run is two passes: the transcriber over the
audio, then the diarizer over the same audio, then a join. The join is **word-level when the
transcriber reports word timings**: each word takes the diarizer turn that overlaps it most,
and the segment takes the speaker of most of its words (ties go to the longer speech, then the
lower id). A segment with no words, or whose words overlap no turn, falls back to the
segment-level rule: the turn with the largest overlap. A segment overlapping no turn keeps
speaker 0. The two passes run one after the other, so only one model is in memory at a time.

Two honest limits:

- **The join is ours, not the library's**, and it is heuristic: the majority-word rule and the
  largest-overlap fallback are choices, not measurements. Nothing here claims they are verified
  against a reference alignment.
- **The diarizer is offline and whole-buffer.** Sortformer reads the whole file at once and
  the app loads it in memory for that pass; the transcriber's windowing does not apply to it.

The wrapper exposes the two halves: `Model.CreateSession` with `WithDiarize` for an ASR model
that can diarize, and `Model.CreateDiarizeSession` for one that only does. Pairing them is the
caller's job.

### What the tab says before the run

The **Speech to text + speakers** goal is offered only for a transcriber that reports at least
segment timestamps. A model that emits no timings cannot be joined — there is nothing to
overlap a speaker turn with — so the tab shows a note in the speaker card instead of a control
that would silently leave every segment as `Speaker 0`. (37 of the 73 ASR aliases in the pinned
catalog declare no timestamps.) The text-only goal has no speaker card at all.

A plan line states the outcome in one sentence, before the run. With a self-diarizing model it
says the model does it in one pass; with a dedicated model it names both — for example *"Two
passes: whisper-tiny, then diar_streaming_sortformer_4spk-v2.1 over the same audio. The second
pass adds speaker turns, not text."* — and a warning appears when the diarizer's catalog record
does not declare the chosen language. Sortformer's record declares **English only**, so a
French transcription shows that warning before the run rather than after it. The warning quotes
the pinned catalog, which is upstream's statement; this project has not measured the diarizer
on French audio.

If the speaker goal is chosen and no diarizer is downloaded, the run button is disabled, the
reason names the Models tab, and **Get a diarizer…** opens it filtered to the diarization role.
That is deliberate: the alternative would be a run that comes back with every segment as
`Speaker 0`, which is the surprise this control exists to remove.

During the second pass the status says **Diarizing…** and the bar is indeterminate: the
diarizer runs the whole buffer in one call and reports no finer progress, so the card says that
rather than holding a percentage nobody measured. Reaching 100% means the whole run is done,
not just the transcript. The split between the two passes is a phase marker, not a measured
estimate of how long each pass takes.

## What is verified, and what CI does not run

Verified end to end with MOSS Transcribe-Diarize Q4_K_M on the JFK sample:
`Supports` is true, ON attributes speaker 1 on every segment, OFF gives the same
text with no attribution, batch carries the same speaker segments, and the two
speakers of a two-voice sample come back as speakers 1 and 2. The tests are in
`tests/TranscribeCppSharp.Interop.Tests/DiarizationTests.cs`.

Those tests need the ~668 MB MOSS asset, so they are **opt-in and skipped in
CI** — a deliberate trade (a 668 MB download per runner, cached but invalidated
whenever the integration script changes). Run them with:

```bash
WITH_DIARIZATION_MODEL=1 ./scripts/run-integration-tests.sh
```

Until you do, nothing in CI would notice a regression that emptied
`SpeakerSegments` or broke the `SpeakerId` mapping.
