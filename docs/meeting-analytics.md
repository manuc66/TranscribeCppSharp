---
title: Anonymous meeting analytics
nav_order: 100
---

# Anonymous meeting analytics

`src/TranscribeCppSharp.MeetingAnalytics` is an **additive, per-recording analytics** prototype.
It consumes `SpeakerSegmentResult` / `WordResult`; it does **not** change `transcribe.cpp`,
does not extract voiceprints, does not identify people, and does not require `nl`.

## What it computes

- per `SpeakerId`: talk time (union of that speaker's turns), talk share, turn count,
  average/longest turn, first/last speech, assigned words, words/minute;
- recording: duration, union speech time, silence, longest silence, overlap, overlap ratio,
  dominant speaker, talk-share standard deviation, speaker changes;
- events: pairwise overlaps, silence gaps, heuristic interruptions, heuristic backchannels;
- exports: JSON, CSV, Markdown.

## API

```csharp
MeetingAnalytics analytics = MeetingAnalyzer.Analyze(
    transcript.SpeakerSegments,
    transcript.Words,
    MeetingAnalyticsOptions.Default);

string json = MeetingAnalyticsJson.Write(analytics);
string csv = MeetingAnalyticsCsv.Write(analytics);
string markdown = MeetingAnalyticsMarkdown.Write(analytics);
```

Or `MeetingAnalyzer.Analyze(transcript, options)`. Duration is inferred from the latest
turn/word unless `RecordingDuration` is supplied; a longer supplied duration wins.

## Honest limits

- `SpeakerId` is **per recording**, not `PersonId`, not cross-meeting identity.
- `Speaker 0`, negative-start and reversed/empty turns are ignored and counted.
- `SpeakerSegmentResult.Probability` is not used as identity and may be `NaN`.
- Talk time is unioned per speaker; recording speech is unioned across speakers.
- Pairwise overlap events can produce multiple rows for a multi-speaker overlap;
  summary overlap time is the union of overlap intervals.
- Silence is a gap in the union of attributed speech. Leading/trailing gaps are included
  when at least one valid turn exists; with no valid turns, total silence equals duration
  and no silence event is emitted.
- Words are assigned to the speaker turn with largest overlap; ties go to lower `SpeakerId`.
  Words with no overlap are not assigned.
- Interruptions/backchannels are **heuristics**: later turn starts after minimum earlier speech;
  a short later turn ending before the earlier turn is classified as a possible backchannel.
  No `PersonCorrect`, no acoustic proof, no emotion/intent inference.
- Talk share can sum above 100% when speakers overlap because it is talk time / recording duration.
- No `nl`, no biometrics, no `UnknownSpeaker`, no voiceprint, no `SpeakerEmbeddingManager`,
  no `SpeakerAssignment` text join, no UI, no production thresholds.
- `MeetingAnalyticsOptions.Default` thresholds are provisional; calibrate for the domain.

## Tests

`tests/TranscribeCppSharp.MeetingAnalytics.Tests`: pure timeline tests for union talk time,
silence, overlap, invalid turns, empty input, word assignment, words/minute, interruption vs
backchannel, speaker changes, silence/overlap thresholds, transcript overload, JSON/CSV/Markdown.

## Optional voiceprint resolution

`TranscribeCppSharp.MeetingAnalytics.Voiceprint` is an **opt-in integration** with
`TranscribeCppSharp.Voiceprint`. Core analytics remains anonymous.

```csharp
var resolver = new MeetingSpeakerResolver(
    embedder,
    voiceprintStore,
    unknownSpeakerStore,
    new MeetingSpeakerResolutionOptions(
        IdentityPolicy: new AmbiguityPolicy(0.75, 0.10, MaxRechecks: 1),
        PersistUnknownSpeakers: true,
        MaxSnippetsPerSpeaker: 3,
        AudioQuality: new AudioQualityPolicy(MinRms: 0.01)));

MeetingSpeakerResolution resolved = await resolver.ResolveAsync(
    pcm16kMono,
    transcript.SpeakerSegments,
    transcript.Words,
    transcriptId: "meeting-1",
    language: "fr",
    condition: "clean");

MeetingAnalytics labelled = resolved.Analytics;
Dictionary<int, string> knownNames = resolved.SpeakerNames;
```

- `MeetingSpeakerLabel` = `SpeakerId`, optional `PersonId`, `DisplayName`,
  `Anonymous/Known/Unknown/Ambiguous`, optional `UnknownId`.
- `MeetingAnalytics.WithSpeakerLabels`; statistics unchanged.
- JSON/CSV/Markdown include labels; anonymous output remains `Speaker N`.
- `MeetingSpeakerResolver` uses `SpeakerSnippetExtractor`, `ISpeakerEmbedder`,
  `VoiceprintMatcher.MatchWithRechecks`, `UnknownSpeakerTracker`, `SpeakerNameMapper`.
- Rechecks are bounded by `AmbiguityPolicy.MaxRechecks`; only the longest snippets are considered.
- Unknown/ambiguous speakers can be persisted as pending `UnknownSpeaker`; `SpeakerNames`
  contains **Known only**. Unknown labels are not `PersonId`.
- `AudioQualityPolicy` can reject snippets before embedding.
- No snippet -> no embedding, no invented `UnknownId`.
- No raw transcript rewrite, no `SpeakerAssignment`, no `SubtitleWriter`, no `PersonCorrect`,
  no `nl`, no production identity guarantee.
- `ModelSha256` is optional and propagated to `UnknownSpeakerTracker`; model-file verification
  remains the embedder's responsibility.

## Real end-to-end smoke

`samples/MeetingAnalyticsSmoke` runs the full pipeline with real models:

```text
WAV -> ASR -> Sortformer -> SpeakerAssignment -> MeetingAnalyzer
    -> MeetingSpeakerResolver -> JSON/CSV/Markdown + transcript.vtt
```

Actual run on this branch:

- 10 s two-speaker `sr-data` mix (`fangjun`, `leijun`);
- `whisper-tiny` ASR;
- `diar_streaming_sortformer_4spk-v2.1-Q8_0` diarizer;
- `hr16/tidyvoicex-samresnet34-onnx` speaker embedder;
- enrolled `sr-data` voiceprints;
- `MinScore=0.5`, `MinMargin=0`, `MaxRechecks=1` — smoke only;
- **2 speakers, 2 Known, 0 Unknown, 0 Ambiguous**;
- speech `6.56 s`, silence `3.44 s`, overlap `0 s`, speaker changes `1`;
- `analytics.json`, `analytics.csv`, `analytics.md`, `transcript.vtt` produced.

This proves the real pipeline runs. It does **not** prove `fr/en/nl`, `nl`, identity/diarization
accuracy, production thresholds, or word-rate accuracy (`whisper-tiny` emitted no word timings).
See `samples/MeetingAnalyticsSmoke/README.md`.

## `MeetingAnalyticsEval`

`samples/MeetingAnalyticsEval` runs the real pipeline over an annotated corpus and computes
`PersonCorrect`, target/non-target counts, `FAR/FRR`, unknown rejection, ambiguous count,
accuracy, balanced error, coverage, resolve wall/CPU and CPU per audio second.
`MeetingEvalCalibrator` sweeps `MinScore`/`MinMargin`; `MaxRechecks` is fixed per run.

Corpus `expectedSpeakers` keys are per-run numeric `SpeakerId`; `null` means expected unknown.
Missing keys are unlabeled and excluded. No per-utterance language/condition, no `nl`.

Actual E2E runs on the 10 s `sr-data` mix with `whisper-tiny`, Sortformer Q8_0 and `hr16`:

- **Known**: 2/2 PersonCorrect, FAR 0, FRR 0, accuracy 1, coverage 1;
  resolve CPU ~111.9 ms/audio s. Calibration selects `-1/0` because no negatives — not production.
- **Unknown**: empty voiceprint store; 2 true rejects, unknown rejection 1, FAR 0,
  coverage 0; 2 pending `UnknownSpeaker` clusters persisted.
- **Ambiguous**: duplicate identical `fangjun` voiceprint + `MinMargin=0.1`;
  one Ambiguous, one Known, FAR 0, FRR 0.5, ambiguous 1, coverage 0.5.

These prove E2E eval paths execute. They do not prove `fr/en/nl`, `nl`, production accuracy,
diarization accuracy, or calibrated thresholds. See `samples/MeetingAnalyticsEval/README.md`.
