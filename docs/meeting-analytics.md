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
