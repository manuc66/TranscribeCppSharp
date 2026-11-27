# VoiceprintEval

Local **enroll + identify evaluation harness**. No diarization, no re-verification, no UI, no cloud.

## Requirements

- sherpa-onnx speaker embedding model (not bundled)
- sherpa-onnx native runtime for target RID
- **16-bit PCM WAV, 16 kHz**, mono or stereo (downmixed); **no resampling** in this harness
- `TranscribeCppSharp.PcmExtensions.ReadWavToPcm`

This sample includes linux-x64 runtime only. Other platforms replace package.

## Corpus

```json
{
  "persons": [
    {
      "id": "alice",
      "displayName": "Alice",
      "language": "fr",
      "condition": "clean",
      "enrollment": ["alice-fr-a.wav", "alice-en-a.wav", "alice-nl-a.wav"],
      "test": ["alice-fr-test.wav", "alice-en-test.wav", "alice-nl-test.wav"]
    },
    {
      "id": "bob",
      "displayName": "Bob",
      "language": "en",
      "condition": "noisy",
      "enrollment": ["bob-en-a.wav", "bob-fr-a.wav"],
      "test": ["bob-en-test.wav", "bob-fr-test.wav", "bob-nl-test.wav"]
    }
  ],
  "unknown": ["stranger.wav", "short.wav", "overlap.wav"],
  "unknownCases": [
    { "path": "unknown-fr.wav", "language": "fr", "condition": "clean" },
    { "path": "unknown-en.wav", "language": "en", "condition": "noisy" },
    { "path": "unknown-nl.wav", "language": "nl", "condition": "clean" },
    { "path": "short-nl.wav", "language": "nl", "condition": "short" }
  ]
}
```

Paths resolve relative to manifest. `language` per known person / `unknownCases`; `condition` output.
`fr`, `en`, `nl`, `nl-possible`, `cross`, `""`; not validated. `unknown` plain strings unlabeled.
No per-utterance `testLanguage`. `eval` builds transient voiceprints; no audio stored.
No diarization/turn extraction. Unreadable/too-short/unsupported/nonfinite -> `Finite=false`, `HasCandidate=false`.
`corpus.fr-en-nl.example.json` template. **No `nl` proof.**

## Commands

```bash
dotnet run --project samples/VoiceprintEval -- eval \
  --model /local/model.onnx \
  --model-id tidyvoicex_samresnet34@hr16-ONNX-sherpa-1.13.8 \
  --manifest corpus.json \
  --out results.json \
  --num-threads 1

dotnet run --project samples/VoiceprintEval -- calibrate \
  --results results.json --out thresholds.json

dotnet run --project samples/VoiceprintEval -- calibrate \
  --results results.json --out thresholds-by-language.json --by-language
```

`dotnet run` syntax varies by SDK; built DLL invocation also works. WAVs must already be 16 kHz 16-bit PCM.
`--model-id` default `filename@sherpa-onnx-1.13.8`; pin explicitly.

## Recommended `fr` + `en` + `nl` candidate

**`hr16/tidyvoicex-samresnet34-onnx`**, `tidyvoicex_samresnet34.onnx`, ~101 MB, **Apache-2.0**,
ONNX + sherpa-onnx, **cross-lingual/multilingual**, **256-d**, **16 kHz**, **80-d log Mel**,
`VoxCeleb2` + `VoxBlink2` + `TidyVoiceX`; model card `EER 3.07%`, `MinDCF 0.82`, **not measured here**,
**not `fr/en/nl` proof**. `hr16` tested on `sr-data`, not `fr/en/nl`. `wespeaker_en...` not sufficient for `nl`.

## Outputs

`eval`: `bestScore`, `secondBestScore`, `margin`, `personCorrect`, `hasCandidate`, `finite`, `dimension`,
`durationMs`, `pcmSamples`, `embeddingWallMs`, `embeddingCpuMs`, `embeddingAllocatedBytes`, `enrollmentCount`,
`language`, `condition`, `embeddingModelId`; summary CPU/wall totals + `embeddingCpuMsPerAudioSecond`.
Raw top-1; no `SpeakerMatch.Decision`. no `skipped`. no `SpeakerSegmentResult.Probability`.

`calibrate`: sweep `MinScore` + `MinMargin`, `trueAccept`, `falseAccept`, `falseReject`, `unknownRejected`,
`FAR/FRR`, unknown rejection, accuracy, `balancedErrorRate`, `coverage`, `ambiguousCount`, `language`;
best provisional candidate; global + `byLanguage`. **No automatic production threshold.**
`VoiceprintMatcher` actual output uses `Known/Unknown/Ambiguous` + `NeedsReverification`.
Calibration outputs thresholds; apply manually. no `--by-condition`, no `--by-cross-lingual`.

See `docs/voiceprint.md` for full findings, diarization, CPU, storage, `fr/en/nl`, model candidates.
