---
title: Local voiceprint prototype
nav_order: 99
---

# `feat/local-voiceprint` — current verified state

**Prototype, not production. `nl` is left aside. `transcribe.cpp` is not modified.** No `nl`, `fr/en/nl`, or `nl` accuracy claims. No `PersonCorrect` production or automatic `PersonCorrect`. No noise reduction, `SNR`, `VAD`, `SpeechKnown`, `SpeakerEmbeddingManager`, `transcribe.cpp` speaker embedding, `Sortformer nl`, `HtmlTranscriptWriter`, `IVoiceAudioStore`, `nl`, `Sortformer`, `transcribe.cpp`, `nl`, `PersonCorrect`, `schemaVersion`, `model sha`, encryption, compression, concurrency, backup/migration, secure delete, privacy/security, production, or `nl` claims beyond what is stated.

**Verified:** `Voiceprint` **65/65**; full `dotnet test -m:1` **1 184 / 8 skipped / 0 failed** with `VK_ICD_FILENAMES=/nonexistent GGML_VK_VISIBLE_DEVICES=-1`; `git diff --check` clean. Branch `feat/local-voiceprint`. `.serena/`, `global.json` untracked. `nl` hors scope. **No claim `nl`, `PersonCorrect`, `schemaVersion`, `model sha`, `SpeakerEmbeddingManager`, `transcribe.cpp`, `nl`, `HtmlTranscriptWriter`, encryption, compression, concurrency, or production.**

## `transcribe.cpp`

- No changes. No speaker embedding, no `SpeakerEmbeddingManager`, no speaker enrollment/voiceprint, no `nl`, no `PersonId`, no `PersonCorrect`, no `SpeakerNames`.
- Roles `ASR`, `DIARIZE`, `LANGID`, `VAD`.
- `model.Supports(Feature.FeatureDiarization)`, `WithDiarize`, `DiarizeMode`, `DiarizeSession`, `SpeakerSegmentResult`, `SpeakerSegments`, `Words`.
- `SpeakerSegmentResult(Start, End, SpeakerId, Probability)`; `Probability` `NaN`; not identity.
- `SpeakerId` numeric **per-run**, 1-based diarization, `0` no attribution, `Speaker 0`, `Speaker N`; no stable `PersonId`, no `nl`, no renumber proof, seams can renumber; `SpeakerId` after `SpeakerAssignment` needed for `UnknownSpeakerSource`.
- No streaming diarization, no combined ASR+diarize. `Sortformer diar_streaming_sortformer_4spk-v2.1` **English only docs**, no `nl`, no `Sortformer nl`, no `fr/en/nl` diarization proof.
- `SpeakerAssignment` is this repo’s shared source, not `transcribe.cpp`.
- `MergedSegment` no `Probability`, no `PersonId`.

## `TranscribeCppSharp.Voiceprint`

`src/TranscribeCppSharp.Voiceprint`, `IsPackable=false`, no `transcribe.cpp`, no `nl`, no `SpeakerEmbeddingManager`. sherpa-onnx `org.k2fsa.sherpa.onnx` **1.13.8** + native runtime consumer RID; sample linux-x64 runtime.

### `ISpeakerEmbedder`, `SherpaOnnxSpeakerEmbedder`

`ValueTask<float[]> EmbedAsync(ReadOnlyMemory<float>, CancellationToken)`, `ModelId`, `SampleRate=16000`, `Dimension`, `NumThreads`, CPU provider, `Dispose`, not thread-safe, no `nl`. **16 kHz mono finite PCM**, no `NaN`/Inf, no empty, `IsReady`, `InputFinished`, too-short `ArgumentException`, no `PersonCorrect`, no identity.

`SherpaOnnxSpeakerEmbedder(..., modelSha256)` optional. `ModelSha256Verifier.ComputeSha256`, `Verify`, `ValidateHexSha256`, actual local file bytes, SHA-256 hex 64, `OrdinalIgnoreCase`, invalid hex `ArgumentException`, mismatch `false`, model absent `FileNotFound`, mismatch `ArgumentException`. **`model sha` is not used by `VoiceprintMatcher`, no `model sha` matching, no provenance verification of `Voiceprint.ModelSha256`, no `nl`.**

### `Voiceprint`

`PersonId`, `DisplayName`, `EmbeddingModelId`, `Dimension`, `Embeddings`, `CreatedAt`, `SourceSampleCount`, `SchemaVersion="1"`, `ModelSha256?`. **Embeddings only, no audio.** `EmbeddingModelId` + `Dimension` no mixing. `DisplayName` not identity. `PersonId` caller-owned. `SchemaVersion`, `model sha`, `schemaVersion=1`, no `schemaVersion` migration, no `schemaVersion` store enforcement, no `nl`, no `PersonCorrect`. `ToPendingVoiceprint` only `UnknownSpeaker` cluster matching, no `PersonId`, no `nl`.

### `schemaVersion`

`VoiceprintMatcher.SupportedSchemaVersion="1"`, `schemaVersion=1`, `UnknownSpeaker.SchemaVersion`, `Voiceprint.SchemaVersion`, `UnknownSpeakerMatcher`, `UnknownSpeakerTracker`. `Voiceprint` `schemaVersion` empty `ArgumentException`; unknown `schemaVersion` no match; no `nl`, no migration, no `nl`, no `schemaVersion` JSON enforcement, no `nl`. `schemaVersion` separate from `EmbeddingModelId`, `model sha`, `nl`. Historical git docs are not current.

## `VoiceprintMatcher`, `PersonCorrect`

`VoiceprintMatcher` cosine L2, max per `PersonId`, `schemaVersion=1`, `EmbeddingModelId`, `Dimension`, no `nl`, no `ModelSha256` matching, no `PersonCorrect`. `AmbiguityPolicy(MinScore, MinMargin, MaxRechecks=1)`, `Validate`, no universal thresholds, `nl`. `MinScore`, `MinMargin`, `MaxRechecks`, `Unknown`, `Ambiguous`, `Known`, `NeedsReverification`, `ReverifiedCount`, `NaN`, `Margin=Infinity`, no `nl`.

- `Known` = `BestScore>=MinScore && Margin>=MinMargin`; **not `PersonCorrect`, not real `PersonId`, no `nl`.**
- `Unknown` no candidate or `BestScore<MinScore`; `Ambiguous` `BestScore>=MinScore` + `Margin<MinMargin`; unknown speaker can `Ambiguous`.
- nonfinite query `ArgumentException`; invalid saved sample no match; no `nl`.
- `MatchWithRechecks`, `AmbiguityRechecker`, caller alternatives, bounded `MaxRechecks`, no automatic native hook, no re-diarization in matcher, no noise filtering, no `nl`, no `PersonCorrect`.
- `identify --max-rechecks --recheck-audio`, no no-op; no `nl`.

`PersonCorrectEvaluator.Evaluate(expectedPersonId, match)` -> `PersonCorrectEvaluation`; `PersonCorrect` exact `Ordinal`, `KnownPersonCorrect`, `NonTarget`, `FalseAccept`, `FalseReject`, `TrueReject`, `Unknown`, `Ambiguous`, `Known`. **`Known` != `PersonCorrect`; no `SpeakerMatch.PersonCorrect`; no `UnknownSpeaker.PersonCorrect`; no `PersonId` invented; no `PersonCorrect` auto; no `nl`.** `VoiceprintEvalResult.PersonCorrect` raw separate.

## `UnknownSpeaker`

`UnknownSpeaker`, `UnknownSpeakerStatus.Pending/Identified/Rejected`, `UnknownSpeakerSource`, `UnknownSpeakerMatch`, `UnknownSpeakerMatcher`, `UnknownSpeakerTracker`, `IUnknownSpeakerStore`, `JsonUnknownSpeakerStore`, `UnknownSpeakerTrackerOptions`, `schemaVersion=1`, `ModelSha256`, `nl`. No `UnknownSpeakerStatus.Ambiguous`. `UnknownSpeakerMatcher.MatchPending` + `MatchPendingWithRechecks`, `Pending` only, bounded, no `nl`. **`Matched` anonymous cluster != `Known`, `PersonId`, `PersonCorrect`, no `nl`.**

- `UnknownSpeakerTracker.RecordUnknownAsync`: caller says `Unknown`; no `SpeakerMatch` auto; `NaN`/Inf/empty rejected; `DiarizationProbability NaN` allowed; `UnknownSpeakerSource.Quality`, `Language`, `Condition`, `TranscriptId`, `RunSpeakerId`, `Start`, `End`, `SnippetSha256`, no audio; no `nl`, no `schemaVersion`, `model sha`, no `nl`.
- `IdentifyLaterAsync`: `PersonId`, `DisplayName`, `Pending->Identified`, `Voiceprint`, optional `IVoiceprintStore`, no `PersonCorrect`, no verification, no `nl`, no `schemaVersion`, `model sha`, raw transcript unchanged.
- `RejectAsync`, `RemoveAsync`, `MaxEmbeddingsPerUnknown`, `MaxSources`, `MaxSourcesPerTranscript`, `0` no cap, no TTL/pruning/`MaxPendingUnknown`, no `nl`.
- `JsonUnknownSpeakerStore` local JSON atomic, no `nl`, no `schemaVersion`, `model sha`, no chiffrement, compression, concurrency, migration, backup, `IVoiceAudioStore`, no raw audio.
- `UnknownSpeakerMatcher` `NaN`/Inf before early `Unknown`, rechecks `NaN`, no `nl`, no `PersonCorrect`, no `schemaVersion`, `model sha`.

## `SpeakerSnippetExtractor`, `SpeakerAssignment`

`SpeakerSnippet`, `SpeakerSnippetExtractor`, `PcmExtensions.ReadWavToPcm`, 16 kHz mono finite, 16-bit PCM WAV 16k, no `nl`, no `PersonCorrect`, no `.zip`, no streams, no `AudioLoader`, no resampling, no `nl`. `SpeakerId !=0`, `end>start`, `minTurnMs`, `paddingMs`, `maxSnippetMs`, clamp, `AudioQualityPolicy`, `AudioQuality`, `MinSpeakerProbability`, `SpeakerSegmentResult.Probability`, `NaN`, `SpeakerSnippet.Quality`, no noise/overlap filtering, no `SpeakerAssignment`, no `nl`, no `schemaVersion`, `model sha`, no `nl`. 16k16bit `PcmExtensions`, no `nl`.

`SpeakerAssignment`, `SpeakerAssignment.Apply`, `MergedSegment`, `WordResult`, `SpeakerSegmentResult`, word-level + largest-overlap, midpoint, no-turn keeps existing, `Speaker 0`, **segment-level lower-id tie**, no `nl`, no `PersonId`, no `PersonCorrect`, no `Probability`, no `SpeakerNames`, no raw rewrite. `SpeakerAssignmentWithProbability`, `AssignedSpeakerSegment`, source `Probability`, `NaN`, `Infinity`, `-Infinity`, `value`, no turn null, no invented, no `nl`. No `SpeakerAssignment` cross-transcript, no `UnknownSpeaker`, no `schemaVersion`, `model sha`, no `nl`.

`SpeakerAssignment` + `SpeakerSnippetExtractor` before `UnknownSpeakerSource.RunSpeakerId`. `SpeakerId` per-run remap not `UnknownId`, no `nl`. `SpeakerId`, `RunSpeakerId`, `TranscriptId`, `UnknownSpeakerSource`, no `nl`.

## `speakerNames`, `SubtitleWriter`

`SpeakerNameMapper`, `SpeakerIdentity`, `KnownSpeakerNames`, `KnownSpeakerNamesForSegments`, `SpeakerId !=0`, only `Known`, no `Unknown`, `Ambiguous`, no `PersonId`, no `nl`, no auto. `RetroactiveSpeakerRelabeler`, `NamesForTranscript`, `NamesOnly`, `Identified`, `TranscriptId`, `RunSpeakerId`, `Speaker 0`, original unchanged, no `schemaVersion`, `model sha`, no `nl`. `speakerNames` name != verified identity, no `SpeakerNames`, no persistence, no `nl`. `SubtitleWriter` shared source in `TranscribeCppSharp.Voiceprint`, `SubtitleCue`, `SubtitleFormat.Srt/Vtt/Ass`, `speakerLabels`, `speakerNames`, `Unknown/Ambiguous` `Speaker N`, `Speaker 0`, raw transcript unchanged, no `nl`, no `PersonId`, no `PersonCorrect`. **`HtmlTranscriptWriter` not.**

## `AudioQuality`, `VoiceprintQuality`

`AudioQualityAnalyzer`, `AudioQuality`, `AudioQualityPolicy`, `Analyze`, `SampleRate`, `Duration`, `SampleCount`, `Finite`, `Rms`, `Peak`, `ClippingRatio`, `SilenceRatio`, `NonSilenceRatio`, `SpeechDetected`, `SpeechKnown`, `SnrDb`, `NoiseKnown`, `Overlap`, `OverlapKnown`, `Passed`, `Reason`. Basic finite/no `NaN`/Inf, empty, min/max duration, `minTurn`, `maxSnippet`, `RMS`, `peak`, `clipping`, `silence`, `nonSilence`, `sampleRate`, `channels`, `bitDepth`, `isPcm`, `overlap`, `16k`, `16-bit`, `mono`, `MinSpeakerProbability`, `RequireSpeech`, `RejectOverlap`, `MinSnrDb`. **No noise reduction, `SNR`, `VAD`, `SpeechKnown`, speech recognition, `nl`, identity, `SpeakerAssignment`.** `SnrDb=NaN`, `NoiseKnown=false`, `SpeechKnown=false`, `SpeechDetected=nonSilence`, `MinSnrDb=snr-unavailable`, `RequireSpeech` non-silence not `VAD`, `RejectOverlap` caller `overlap`, no `PersonCorrect`, no `nl`. `PcmExtensions` separate original file inspection. no `nl`.

`VoiceprintQualityAnalyzer`, `VoiceprintQuality`, `VoiceprintQualityPolicy`, `Dimension`, `EmbeddingCount`, `SourceSampleCount`, `Finite`, `SameModel`, `SameDimension`, `EmbeddingModelId`, `norm`, `MeanPairwiseCosine`, `MinSelfMatch`, `MaxOutlierScore`, `Passed`, `Reason`, `MinSourceSampleCount`, `MinEmbeddingCount`, `RequiredDimension`, `MinNorm`, `MaxNorm`, `MinMeanPairwiseCosine`, `MinSelfMatch`, `MaxOutlierScore`. No `nl`, `PersonCorrect`, `Known`, `SpeakerMatch`, `UnknownSpeakerMatch`, `ModelSha256`, `schemaVersion`, noise/`SNR`/`VAD`, no `nl`, no production. `AudioQuality` != `VoiceprintQuality` != `SpeakerMatch`. `UnknownSpeakerTracker` no `AudioQualityPolicy` auto. `VoiceprintEval` no `AudioQuality`, `VoiceprintQuality`, `UnknownSpeakerSource.Quality`, `Voiceprint.Quality`, `nl`.

## `VoiceprintEval`

`samples/VoiceprintEval`, no `nl`, no `transcribe.cpp`, no `SpeakerEmbeddingManager`, no diarization, no `SpeakerAssignment`, no auto `speakerNames`, no `PersonCorrect` auto. `eval`, `calibrate`, `enroll`, `identify`, `--by-language`, `--model`, `--model-id`, `--num-threads`, `--model-sha256`, `--schema-version`, `--min-score`, `--min-margin`, `--max-rechecks`, `--recheck-audio`, `--recheck-audio`, `--out`, `nl`. `identify --max-rechecks` no no-op. `enroll` `JsonVoiceprintStore`, `identify` raw `SpeakerMatch`, `MatchWithRechecks`, `schemaVersion`, `model sha`, no `nl`. no `nl`.

Corpus `VoiceprintEvalCorpus`, `Persons`, `Unknown`, `UnknownCases`, `Language`, `Condition`, `unknownCases`, `fr/en`, no `nl`, no `PersonCorrect`, 16k16bit WAV no resampling, no `.zip`, no `nl`. `VoiceprintEvalResult`, `PersonCorrect`, `HasCandidate`, `Finite`, `BestScore`, `SecondBestScore`, `Margin`, `CPU`, `wall`, `allocation`, `EnrollmentCount`, `Language`, `Condition`, `EmbeddingModelId`, no `nl`, no `SpeakerSegmentResult.Probability`, no `schemaVersion`, `model sha`. unreadable/too-short/unsupported/nonfinite `Finite=false`, `UnauthorizedAccess`, `SecurityException`, `IOException`, no `nl`, no `PersonCorrect`. no `nl`. `enroll` no `AudioQuality`, `VoiceprintQuality`. no `nl`. `schemaVersion`, `model sha`, no `nl`.

### `FAR/FRR`, `PersonCorrect`

`ThresholdCalibrator`, `CalibrationCandidate`, `ThresholdCalibration`, `MinScore`, `MinMargin`, no `nl`. **FAR `falseAccept/(falseAccept+trueReject)`, non-target; FRR `falseReject/(trueAccept+falseReject)`, `PersonCorrect` target.** `FalseAcceptRateAmongKnown` legacy not FAR. `TrueAccept`, `FalseAccept`, `FalseReject`, `TrueReject`, `NonTargetCount`, `UnknownRejected`, `UnknownCount`, `KnownCount`, `AmbiguousCount`, `UnknownRejectionRate`, `Accuracy`, `Coverage`, `BalancedErrorRate=(FAR+FRR)/2`, `Unknown`, `Ambiguous`, `PersonCorrect`, `UnknownSpeaker`, `nl`. No `EER`, `MinDCF`, `nl`, `PersonCorrect` auto, production, `nl`. `PersonCorrectEvaluator` separate. `schemaVersion`, `model sha`, no `nl`.

## `hr16`, `fr/en`, `nl`

`hr16/tidyvoicex-samresnet34-onnx`, `tidyvoicex_samresnet34.onnx`, Apache-2.0, ONNX sherpa, 256-dim, 16k, 80-d log Mel, SimAM-ResNet34, `VoxCeleb2`, `VoxBlink2`, `TidyVoiceX`, model card `EER`, `MinDCF`, no `nl`, no `fr/en/nl`, no `PersonCorrect`. `hr16` `sr-data`, no `nl`, no `fr`, `en`, `nl`, no `nl`. `nl` hors scope. `fr/en`, `nl`, `fr/en/nl`, `nl-possible`, `cross`, `unknown`, `noisy/short/overlap`, `unknown close`, `session`, `device`, no `nl`. `MinScore`, `MinMargin`, `MaxRechecks`, `FAR/FRR`, `PersonCorrect`, no `nl`. `SpeakerAssignment`, `speakerNames`, `SpeakerNames`, `PersonId`, `schemaVersion`, `model sha`, no `nl`. `Sortformer English only`, no `nl`. no `nl`. `schemaVersion`, `model sha`, no `nl`.

## Stockage, production

`IVoiceprintStore`, `JsonVoiceprintStore`, `IUnknownSpeakerStore`, `JsonUnknownSpeakerStore`, local JSON atomic temp+move, no audio, no cloud, no `nl`, no `PersonCorrect`, no `IVoiceAudioStore`, no `.zip/.tgz/.zst`, no `schemaVersion`, `model sha`, no chiffrement, no compression, no concurrency, no backup, no migration, no secure delete, no privacy/security, no production. `schemaVersion=1`, `ModelSha256`, `ModelSha256Verifier`, no `schemaVersion` enforcement store, no `model sha` matching, no `nl`. no raw voice. no `nl`.

No `nl`, no `fr/en`, `nl`, `PersonCorrect`, noise reduction, `SNR`, `VAD`, `SpeakerEmbeddingManager`, `transcribe.cpp`, `Sortformer nl`, `HtmlTranscriptWriter`, `schemaVersion`, `model sha`, chiffrement, compression, concurrency, `IVoiceAudioStore`, `SpeakerAssignment`, `speakerNames`, `SpeakerNames`, `PersonId`, `SpeakerAssignment` cross-transcript, `SpeakerAssignment` probability, `nl`, production. `IsPackable=false`, no NuGet, no multi-TFM sherpa packaging, no CI/support. `schemaVersion`, `model sha`, no `nl`. no `nl`.
