# MeetingAnalyticsSmoke

Real end-to-end pipeline:

```text
WAV 16 kHz
-> ASR (Session.Run, diarize OFF)
-> Sortformer (DiarizeSession.Run, whole buffer)
-> SpeakerAssignment
-> MeetingAnalyzer
-> MeetingSpeakerResolver + SherpaOnnxSpeakerEmbedder
-> analytics JSON/CSV/Markdown + transcript.vtt
```

No `transcribe.cpp` changes. No raw transcript rewrite. `transcript.vtt` is a separate export.
No `nl`, no `PersonCorrect`, no production threshold.

## Requirements

- 16-bit PCM WAV, 16 kHz, mono/stereo
- ASR GGUF
- DIARIZE-role GGUF (Sortformer)
- sherpa-onnx speaker embedding ONNX (not bundled)
- optional `voiceprints.json` from `VoiceprintEval enroll`
- linux-x64 native packages in this sample; replace for other RIDs

## Run

```bash
dotnet run --project samples/MeetingAnalyticsSmoke -- \
  --audio two-speakers.wav \
  --asr-model whisper-tiny.gguf \
  --diarizer-model diar_streaming_sortformer_4spk-v2.1-Q8_0.gguf \
  --speaker-model tidyvoicex_samresnet34.onnx \
  --model-id tidyvoicex_samresnet34@hr16-ONNX-sherpa-1.13.8 \
  --model-sha256 <sha256> \
  --voiceprints voiceprints.json \
  --unknowns unknown-speakers.json \
  --out-dir out \
  --min-score 0.5 --min-margin 0 --max-rechecks 1 --num-threads 1
```

Thresholds shown are **smoke-test only**, not calibrated production values.

## Actual run on this branch

- audio: 10 s, two concatenated `sr-data` speakers (`fangjun`, `leijun`)
- ASR: `whisper-tiny`
- diarizer: `diar_streaming_sortformer_4spk-v2.1-Q8_0`
- speaker: `hr16/tidyvoicex-samresnet34-onnx`, SHA256
  `93e35d944e90d50a8d3768b06695b1384aae43880eac05a966006a7bb1127a0a`
- voiceprints: enrolled from `sr-data` enrollment WAVs
- result: **2 speakers, 2 Known, 0 Unknown, 0 Ambiguous**
- speech `6.56 s`, silence `3.44 s`, overlap `0 s`, speaker changes `1`
- outputs written: `analytics.json`, `analytics.csv`, `analytics.md`, `transcript.vtt`

This proves the **pipeline executes end-to-end with real models**. It does **not** prove
`fr/en/nl`, `nl`, identity accuracy, diarization accuracy, threshold quality, or production readiness.
`whisper-tiny` emitted no word timings, so words/minute remained zero.
