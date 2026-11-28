# MeetingAnalyticsEval

Corpus runner for the real meeting pipeline:

```text
corpus -> ASR -> Sortformer -> MeetingAnalyzer -> MeetingSpeakerResolver
       -> PersonCorrect / FAR / FRR / unknown rejection / CPU
       -> results.json + summary.json/csv + thresholds.json
```

No `nl`, no production threshold, no raw transcript rewrite.

## Corpus

```json
{
  "meetings": [
    {
      "audio": "meeting-fr.wav",
      "language": "fr",
      "condition": "clean",
      "expectedSpeakers": {
        "1": "alice",
        "2": "bob"
      }
    },
    {
      "audio": "meeting-fr-unknown.wav",
      "language": "fr",
      "condition": "noisy",
      "expectedSpeakers": {
        "1": "alice",
        "2": null
      }
    }
  ]
}
```

Paths resolve relative to the manifest. Keys are **per-run numeric `SpeakerId`** in arrival order;
inspect a first run before annotating. Missing keys are unlabeled and excluded from metrics.
`null` means expected unknown. No per-utterance language/condition; no `nl`.

## Run

```bash
dotnet run --project samples/MeetingAnalyticsEval -- \
  --corpus corpus.json \
  --asr-model whisper-tiny.gguf \
  --diarizer-model diar_streaming_sortformer_4spk-v2.1-Q8_0.gguf \
  --speaker-model tidyvoicex_samresnet34.onnx \
  --model-id tidyvoicex_samresnet34@hr16-ONNX-sherpa-1.13.8 \
  --model-sha256 <sha256> \
  --voiceprints voiceprints.json \
  --unknowns unknown-speakers.json \
  --out-dir eval-out \
  --min-score 0.5 --min-margin 0 --max-rechecks 1 --num-threads 1
```

WAV input: 16-bit PCM, 16 kHz, mono/stereo. linux-x64 native packages are sample-only.

## Metrics

- `PersonCorrect`, target/non-target counts;
- `TrueAccept`, `FalseAccept`, `FalseReject`, `TrueReject`;
- standard `FAR = falseAccept / (falseAccept + trueReject)`;
- standard `FRR = falseReject / (trueAccept + falseReject)`;
- unknown rejection, ambiguous count, accuracy, balanced error, coverage;
- resolve wall/CPU and CPU per audio second;
- per-speaker raw scores/margins/rechecks/`UnknownId`.

`MeetingEvalCalibrator` sweeps `MinScore` and `MinMargin`; `MaxRechecks` is fixed per run.
The best candidate is provisional. A corpus with only positive examples can select `MinScore=-1`;
that is not a usable production threshold.

## Actual E2E runs on this branch

Same 10 s `sr-data` two-speaker mix, `whisper-tiny`, Sortformer Q8_0, `hr16` speaker model.

### Known

Enrolled `fangjun` and `leijun`; `MinScore=0.5`, `MinMargin=0`:

- 2 speakers, 2 evaluated, 2 PersonCorrect;
- FAR 0, FRR 0, accuracy 1, coverage 1;
- resolve CPU ~111.9 ms/audio s;
- best calibrated threshold `-1 / 0` because there are no negative examples — **not production**.

### Unknown

Empty voiceprint store; both expected unknown:

- 2 speakers, 2 true rejects, 2 unknown rejected;
- unknown rejection 1, FAR 0, coverage 0;
- 2 pending `UnknownSpeaker` clusters persisted.

### Ambiguous

Duplicate identical `fangjun` voiceprint + `MinMargin=0.1`:

- speaker 1 `Ambiguous`, margin 0;
- speaker 2 Known;
- FAR 0, FRR 0.5, ambiguous 1, coverage 0.5.

These prove the **E2E eval paths execute**, not `fr/en/nl`, `nl`, production accuracy,
diarization accuracy, or calibrated thresholds.
