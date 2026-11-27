---
title: Local voiceprint prototype
nav_order: 99
---

# Local voiceprint prototype (`feat/local-voiceprint`)

## Executive summary

**Goal.** Save a user's local **voiceprint**, transcribe local audio, attribute anonymous
speakers, identify speakers as `Known / Unknown / Ambiguous`, prefer a **multilingual /
cross-lingual** speaker model for **`fr` + `en` + possibly `nl`**, re-verify ambiguous
speakers in a **bounded** way, and avoid excessive CPU.

**Current answer.** This is **not a feature of `transcribe.cpp` or `TranscribeCppSharp`**.
The repository does **not** expose speaker embeddings, speaker enrollment, speaker
reference audio, speaker recognition, speaker re-identification, saved voiceprints, or
automatic ambiguity re-verification. The branch adds an **additive local prototype**:

```text
TranscribeCppSharp (transcribe.cpp, no native changes)
    +
src/TranscribeCppSharp.Voiceprint
    +
sherpa-onnx ONNX speaker embedding + local matcher + local store
```

It is **not proof of speaker identity**, **not calibrated production**, **not UI**, **not
streaming**, **not diarization-integrated**, **not a compressed voice container**, and
**no `fr/en/nl` accuracy has been measured**.

## What `transcribe.cpp` / `TranscribeCppSharp` already provides

- `Model.Supports(Feature.FeatureDiarization)`.
- `session.Run(pcm, r => r.WithDiarize(DiarizeMode.DiarizeModeOn))`; `DiarizeModeOff`;
  `DiarizeModeDefault` = OFF for every family.
- `transcript.Segments`, `transcript.SpeakerSegments`, `transcript.Words`, `FullText`, `RawText`.
- `SpeakerSegmentResult(Start, End, SpeakerId, Probability)`.
- `SegmentResult.SpeakerId`; **numeric `SpeakerId`, 1-based; `0` = no attribution**.
- `DiarizeSession.Run` -> `IReadOnlyList<SpeakerSegmentResult>`; `DiarizeSession.DiarizeInfo`.
- `model.CreateDiarizeSession()`, `DiarizeParamsBuilder`, `SortformerDiarizeExtBuilder`,
  `SortformerPreset`.
- `DiarizeInfo.SampleRate`, `DiarizeInfo.MaxSpeakers`.
- `model.Roles`, `model.GetDiarizeInfo()`, `model.Supports(...)`.
- `Batch.Run` can carry per-result speaker segments.

### Four `transcribe.cpp` roles only

`ASR`, `DIARIZE`, `LANGID`, `VAD`. **No `SPEAKER_EMBEDDING` role.** `transcribe.cpp`
has **no speaker embedding output**, **no speaker enrollment**, **no speaker reference
audio**, **no speaker recognition**, **no voiceprint store**, **no speaker re-identification**,
**no automatic speaker-ambiguity re-verification**, **no speaker-name persistence**.

### Anonymous speaker ids, not identity

- `SpeakerId` is **per-run**, **not a stable person id**, **not `PersonId`**, **not a name**.
- `Speaker 0`, `Speaker N`, `Speaker N:`, `<v Speaker N>`, `"speaker":N`.
- Non-diarized models, unsupported models, no attribution, no speaker markers: **`Speaker 0`**.
- `SpeakerId != 0` indicates a present speaker row; no attribution = `0`.
- `transcribe.cpp` docs say `speaker_id` **1-based** for diarization.
- `DiarizeInfo.MaxSpeakers` is a **model maximum**, not speaker count control.
- `Sortformer` model `diar_streaming_sortformer_4spk-v2.1` has a **4 speaker** ceiling in
  name; actual value comes from `MaxSpeakers`.
- `SpeakerId` **can renumber** across windows/seams/runs. `TranscriptMerger` shifts time
  and removes overlap but **does not remap speaker ids**. `Sortformer` whole buffer avoids
  split renumbering. **No cross-file speaker identity.**
- Numeric ids `1`, `2`, ... **cannot replace voiceprint matching**.
- `SpeakerNames` / `speakerNames` in app/subtitle exports are **display names**, **not
  verified identity**, **not voiceprint matching**.

### Diarization confidence is not identity confidence

- `SpeakerSegmentResult.Probability` / native `p` = **attribution confidence when model
  produces one**, otherwise **`NaN`**.
- `NaN` must not be treated as `0`, `1`, known, unknown, or accurate.
- `float.NaN` diarization probability is allowed in `SpeakerSnippetExtractor`.
- `SpeakerSnippet.DiarizationProbability` preserves original `Probability`.
- **Diarization `Probability` != speaker identity score.**
- `VoiceprintMatcher` **does not use** `SpeakerSegmentResult.Probability`.
- `VoiceprintEval` **does not use** sherpa-onnx speaker `Probability` or diarization `Probability`.
- `SpeakerMatch.BestScore`, `SecondBestScore`, `Margin` are **identity match scores**.
- `SpeakerMatch.Decision`, `PersonCorrect`, `Unknown`, `Ambiguous` are separate.
- `TokenResult.Probability` is **ASR token probability**, not speaker identity.

### Diarization models / two passes

- Speaker-attributing ASR models: `moss-transcribe-diarize`, `granite-speech-4.1-2b-plus`
  (speaker-attribution task), `multitalker-parakeet-streaming-0.6b-v1`.
- `moss-transcribe-diarize` CLI default in docs; `Speaker 0` on models that cannot attribute.
- `DIARIZE`-role models: `diar_streaming_sortformer_4spk-v2.1` (Sortformer); no ASR session.
- Sortformer produces **speaker turns, no text**. **Offline**, whole buffer, no streaming.
- **No combined ASR + diarization entry point.** Run separately and join.
- App two-pass: transcriber windows -> diarizer whole buffer -> join; **sequential**;
  one model in memory at a time. **CPU still additive.**
- `transcribe_stream_params` has no `diarize`; **no streaming diarization** in `transcribe.cpp`
  0.4.2. No `transcribe_stream` diarize flag. The app does not invent one.
- **No `SpeakerAssignment` inside `VoiceprintMatcher`.**
- `SpeakerAssignment` is **not** speaker identity.

### `SpeakerAssignment` two-pass join

`TranscribeCppSharp.SharedSource.SpeakerAssignment`:

```csharp
SpeakerAssignment.Apply(segments, turns, words)
```

- `MergedSegment(Start, End, Text, SpeakerId)`.
- word-level first when `WordResult` available; midpoint inside segment.
- word speaker = turn with largest overlap; segment speaker = speaker of most words;
  ties -> longer speech -> lower id.
- fallback segment-level largest overlap.
- no words / no overlap -> segment-level; segment with no turn keeps existing id (`0` if no diarization).
- **heuristic, not verified alignment**; no accuracy claim.
- **`SpeakerAssignment` does not propagate `SpeakerSegmentResult.Probability`.**
- output `MergedSegment` has **no probability field**.
- If re-verification needs confidence, **keep original `SpeakerSegments`** before `SpeakerAssignment`.
- `VoiceprintEval` does **not** run `SpeakerAssignment`.
- final naming can feed `speakerNames`, but **name != verified identity**.

### `transcribe.cpp` speaker-name output

No speaker renaming in `transcribe.cpp`. `transcribe.cpp` `Speaker N` numeric. `SubtitleWriter`
and `HtmlTranscriptWriter` accept caller `speakerNames`. App `SpeakerNames` in-memory +
keeps name if same numeric id after rerun; **not persisted voiceprint**. `TranscribeCppSharp.Voiceprint`
does not integrate `SubtitleWriter` / `HtmlTranscriptWriter`. **Raw transcript is not rewritten.**

## What branch `feat/local-voiceprint` adds

### `src/TranscribeCppSharp.Voiceprint`

Additive project; **no native `transcribe.cpp` changes**; project references `TranscribeCppSharp`.

#### `ISpeakerEmbedder`

```csharp
ValueTask<float[]> EmbedAsync(ReadOnlyMemory<float> pcm16kMono, CancellationToken ct = default);
```

- `ModelId`, `SampleRate`, `Dimension`.
- local speaker embedding; **16 kHz mono finite PCM**.
- no cloud; **no `transcribe.cpp` speaker embedding**.

#### `SherpaOnnxSpeakerEmbedder`

- sherpa-onnx CPU ONNX `SpeakerEmbeddingExtractor`.
- `org.k2fsa.sherpa.onnx` **1.13.8**.
- native runtime consumer RID; sample linux-x64 **`org.k2fsa.sherpa.onnx.runtime.linux-x64` 1.13.8**.
- `Provider = "cpu"`, `NumThreads`, `Debug=0`.
- `ModelId` pinned separately; **`ModelId != filename`**, caller must pin version/quantization/SHA.
- `Dimension => extractor.Dim`.
- `SampleRate = 16000`.
- `CreateStream`, `AcceptWaveform`, `InputFinished`, `IsReady`, `Compute`.
- **too-short snippet -> `ArgumentException`**.
- **NaN/Inf PCM/embedding -> `ArgumentException`**.
- input copied to `float[]`; no `ReadOnlyMemory` native API.
- `CancellationToken` checked at boundaries; **native compute not safely cancellable mid-call**.
- **not thread-safe**; one embedder, sequential or external locking.
- `Dispose`; extractor disposed.
- model weights **not bundled**; **no `Native transcribe` model**; sherpa runtime separate.
- `modelPath` local `.onnx`.
- `model card` examples may say `provider=cuda`; **this implementation uses CPU**. CPU/NPU/GPU requires sherpa build + packaging + benchmark.

#### `Voiceprint`

```csharp
Voiceprint(
 string PersonId, string DisplayName, string EmbeddingModelId,
 int Dimension, IReadOnlyList<float[]> Embeddings,
 DateTimeOffset CreatedAt, int SourceSampleCount)
```

- **embeddings only; no audio**.
- `PersonId` caller-owned stable identity; **not numeric `SpeakerId`**.
- `DisplayName` export label; **not verified identity**.
- `EmbeddingModelId` must match; **no mixing models**.
- `Dimension` must match; incompatible model/dim ignored.
- `CreatedAt`, `SourceSampleCount`.
- no `Language` field on `Voiceprint`; language split at eval/profile design if needed.
- no `condition`, `session`, `device`, `enrollmentLanguage`, `testLanguage`, `CrossLingual` fields.
- no audio reference; no raw voice.

#### `JsonVoiceprintStore`

- **local JSON**, `System.Text.Json` web options, indented.
- **no cloud**, **no audio**, **no encryption**, **no compression**, **not concurrency-safe**.
- `LoadAsync`, `AddOrUpdateAsync`, `RemoveAsync`.
- `AddOrUpdateAsync` replaces same `PersonId`, `Ordinal`.
- `RemoveAsync` returns bool.
- **atomic temp + `File.Move(overwrite:true)`**; no concurrent-writer protection; no schema migration.
- path + `.tmp`.
- `CancellationToken`.
- **`VoiceprintEval` does not use `JsonVoiceprintStore`**; `VoiceprintEval` builds transient voiceprints.
- no `IVoiceprintStore` yet.
- `VoiceprintEval` not persistence proof.

#### `VoiceprintMatcher`

- **cosine similarity**, L2 normalization, max score per person across enrollment embeddings.
- **same `EmbeddingModelId` + `Dimension` + embedding length** only.
- `float.IsFinite`, `norm <= 0 -> -1`, `NaN -> ArgumentException`.
- order stable `PersonId` tie `Ordinal`.
- `margin = bestScore - secondBestScore`; one saved person => `double.PositiveInfinity`.
- no candidate / no saved matching model => `SpeakerMatch.Unknown`, scores `NaN`.
- `PersonCorrect` **not** truth; matcher can output `Known` for wrong person if score+margin pass.
- **no `transcribe.cpp` id use**.

`AmbiguityPolicy`:

```csharp
AmbiguityPolicy(MinScore, MinMargin, MaxRechecks = 1)
```

- caller-calibrated; **no universal default `MinScore` / `MinMargin`**; `Validate`.
- `MinScore [-1,1]`; `MinMargin [0,2]`; `MaxRechecks >= 0`.
- `Known` = `BestScore >= MinScore && Margin >= MinMargin`.
- `Ambiguous` = has candidate + `BestScore >= MinScore` + `Margin < MinMargin`.
- `Unknown` = no candidate or `BestScore < MinScore`.
- `Unknown` still can be rechecked; `NeedsReverification`.
- `SpeakerMatch.Decision`, `PersonId`, `DisplayName`, `BestScore`, `SecondBestScore`,
  `Margin`, `ReverifiedCount`.
- `NeedsReverification => Decision != Known`.
- `SpeakerMatch.Unknown`.
- **`Known` score threshold != verified real person**; separate evaluation required.
- **unknown speaker can have `Ambiguous`, not necessarily `Unknown`.**

#### `MatchWithRechecks`

```csharp
VoiceprintMatcher.MatchWithRechecks(
 embedding, embeddingModelId, voiceprints, policy, recheckEmbeddings)
```

- `Match` = no rechecks.
- recheck embeddings **caller supplied**.
- `MaxRechecks`; **no re-diarization, no native re-verification hook, no automatic policy execution**.
- current combination chooses best result, **not full voting/fusion**.
- `ReverifiedCount`.
- caller decides re-crop, drop overlap/short, re-run diarizer, recompute embedding,
  alternative snippet, re-map ids, bound count, else `Unknown/Ambiguous`.
- **no streaming recheck**.
- `SpeakerSnippetExtractor` can supply snippets; caller can embed again.

#### `SpeakerSnippetExtractor`

```csharp
SpeakerSnippetExtractor.Extract(
 ReadOnlySpan<float> pcm16kMono, IReadOnlyList<SpeakerSegmentResult> turns,
 int sampleRate=16000, int minTurnMs=0, int paddingMs=0, int maxSnippetMs=0)
```

- output `SpeakerSnippet(RunSpeakerId, Start, End, Pcm16kMono, DiarizationProbability)`.
- `SpeakerId != 0`; `turn.End > turn.Start`.
- `minTurnMs`, `paddingMs`, `maxSnippetMs`; padding clamped; `maxSnippetMs=0` no cap.
- `sampleRate > 0`, no resampling; **caller must provide 16 kHz mono finite PCM**.
- `Probability` preserved, `NaN` allowed.
- no text/word/language/condition/overlap filtering inside; caller filters.
- no `SpeakerAssignment`; no identity.
- no PCM validation here; embedder validates finite.

## `samples/VoiceprintSmoke`

Real inference smoke test. `VoiceprintSmoke model.onnx a1.wav a2.wav b1.wav`.

- `SherpaOnnxSpeakerEmbedder`.
- `PcmExtensions.ReadWavToPcm`.
- `modelId = wespeaker_en_voxceleb_resnet34_LM@sherpa-onnx-1.13.8`.
- output `sim_aa`, `sim_ab`, `margin`, `dim`, `finite`, samples, wall time.
- **success criterion `sim_aa > sim_ab` + finite**, **not accuracy**.
- sherpa-onnx **1.13.8**, linux-x64, CPU `NumThreads=1`.
- no diarization, no `VoiceprintMatcher`, no store, no recheck, no UI.
- `sim_ab` can be high; no `MinScore` inferred.

### Measured `wespeaker_en...` smoke run (not accuracy)

`wespeaker_en_voxceleb_resnet34_LM.onnx`:

- **SHA256 `e9848563da86f263117134dfd7ad63c92355b37de492b55e325400c9d9c39012`**.
- source sherpa-onnx release `speaker-recongition-models` (note upstream spelling).
- `csukuangfj/sr-data`: `fangjun-sr-1`, `fangjun-sr-2`, `leijun-sr-1`.
- **`dim=256`, finite**.
- `sim_aa=0.933205`.
- `sim_ab=0.827290`.
- `margin=0.105914`.
- **three embeddings 471 ms wall clock**, not CPU, not benchmark.
- proves local inference works; **not `fr`, `nl`, unknown, noisy, overlap, diarization, CPU, identity accuracy**.

## `samples/VoiceprintEval`

`VoiceprintEval` = local **`enroll` + `identify` + petit test annoté + `eval` + `calibrate`**.
**No diarization, no re-verification, no UI, no cloud, no `VoiceprintStore`.**
**No `SpeakerAssignment`, no `speakerNames`.**

### `VoiceprintEvalRunner`

- `eval` reads manifest, builds **transient `Voiceprint`** per person from enrollment embeddings.
- `modelId`, `numThreads`.
- uses `SherpaOnnxSpeakerEmbedder`, `VoiceprintMatcher.Match` with raw `MinScore=-1`, `MinMargin=0`, `MaxRechecks=0`.
- raw top-1 + scores output; **calibration separate**.
- **no raw `SpeakerMatch.Decision` output**; `PersonCorrect`, `HasCandidate`, `Finite`.
- no persistence.
- no `SpeakerSnippetExtractor`; no diarization.
- no `SpeakerAssignment`.
- `PcmExtensions.ReadWavToPcm`.
- **unknown short/noisy etc `unknownCases`.**
- **not `SpeakerSnippet` input.**
- `Condition`, `Language`.

### Audio requirements (`VoiceprintEval`)

- **16-bit PCM WAV** (`AudioFormat=1`), **16 kHz**, **`SampleRate=16000`**, 16-bit, no resampling.
- mono or **stereo downmixed** by `PcmExtensions.ReadWavToPcm`.
- `PcmExtensions.ReadWavToPcm` returns `float[]`.
- **No `AudioLoader`**, no `.zip`, no streams, no arbitrary sample rate in this harness.
- pre-convert `ffmpeg -i in.wav -ar 16000 -ac 1 -sample_fmt s16 out16k.wav`.
- `SherpaOnnxSpeakerEmbedder` itself needs **16 kHz mono finite**.
- `PcmExtensions` code requires `SampleRate=16000`; **docs saying resample on `ReadWavToPcm`
  are misleading relative to actual `ValidateFormat`.**
- no automatic silence/noise/overlap trim in `VoiceprintEval`; only `SpeakerSnippetExtractor`
  clamps/filters in diarized flow.
- `PcmExtensions.ReadWavToPcm` `float` PCM output can contain nonfinite? Embedding rejects; `Finite=false`.

### Commands

```bash
dotnet build samples/VoiceprintEval/VoiceprintEval.csproj

dotnet samples/VoiceprintEval/bin/Debug/net10.0/VoiceprintEval.dll eval \
  --model /local/model.onnx \
  --model-id tidyvoicex_samresnet34@hr16-ONNX-sherpa-1.13.8 \
  --manifest corpus.json \
  --out results.json \
  --num-threads 1

dotnet samples/VoiceprintEval/bin/Debug/net10.0/VoiceprintEval.dll calibrate \
  --results results.json --out thresholds.json

dotnet samples/VoiceprintEval/bin/Debug/net10.0/VoiceprintEval.dll calibrate \
  --results results.json --out thresholds-by-language.json --by-language
```

- `eval`: `--model`, `--model-id`, `--manifest`, `--out`, `--num-threads`.
- `--model-id` default `Path.GetFileNameWithoutExtension(model)@sherpa-onnx-1.13.8`; **pin explicitly**.
- `calibrate`: `--results`, `--out`, `--by-language`.
- `dotnet run` syntax SDK-dependent; built DLL invocation known.
- no `--language`, no `--by-condition`, no `--by-cross-lingual`, no `--min-*`, no `--max-rechecks`.
- no `--store`, no `--format`, no `--resample`.

### Corpus schema

`VoiceprintEvalCorpus`:

```json
{
  "persons": [],
  "unknown": [],
  "unknownCases": []
}
```

`VoiceprintEvalPerson`:

```json
{
  "id": "alice",
  "displayName": "Alice",
  "language": "fr",
  "condition": "clean",
  "enrollment": ["alice-fr-a.wav", "alice-en-a.wav", "alice-nl-a.wav"],
  "test": ["alice-fr-test.wav", "alice-en-test.wav", "alice-nl-test.wav"]
}
```

- `id`, `displayName`, `enrollment`, `test`.
- `language` **per known person**, default `""`.
- `condition` **per known person**, default `""`.
- `enrollment` list WAVs, required non-empty; paths relative to manifest.
- `test` list WAVs; paths relative.
- no per-utterance `testLanguage`, no per-utterance `condition`, no `enrollmentLanguage`.
- `unknown` list plain string paths; no `language`; no condition.
- `unknownCases`:

```json
{ "path": "unknown-nl.wav", "language": "nl", "condition": "clean" }
```

- `VoiceprintEvalUnknownCase.path/language/condition`.
- `unknownCases` unknown speakers; **no speaker id truth**.
- `unknown` + `unknownCases`.
- no `condition` for plain `unknown`.
- no `session`, `device`, `microphone`, `noise` fields.
- `language` values not validated; use `fr`, `en`, `nl`, `nl-possible`, `fr-en`, `cross`, `""`, etc.
- `condition` values not validated; `clean`, `noisy`, `short`, `overlap`, `cross`, etc.
- **no `nl` inclusion automatic.**
- `corpus.fr-en-nl.example.json` template.
- **same person `fr/en/nl` + cross-lingual** recommended; **not enforced**.
- `PersonCorrect` uses exact `PersonId`, not `Language`.
- no `Voiceprint.Language`.

### `VoiceprintEvalResult`

Raw `eval` row:

- `test`, `expectedPersonId`, `actualPersonId`, `personCorrect`, `hasCandidate`, `finite`.
- `embeddingModelId`, `dimension`, `pcmSamples`, `durationMs`.
- `bestScore`, `secondBestScore`, `margin`.
- `embeddingWallMs`, `embeddingCpuMs`, `embeddingAllocatedBytes`.
- `enrollmentCount`.
- `language`, `condition`.
- no `skipped`; unreadable/unsupported/too-short/nonfinite -> `finite=false`, `hasCandidate=false`, NaN scores.
- no diarization `probability`.
- no `condition` calibration use.
- no `recheck`.
- `NaN`/`Infinity` JSON allowed `JsonNumberHandling.AllowNamedFloatingPointLiterals`.
- `actualPersonId` raw top1 even if below threshold; **not `Known`.**
- `personCorrectRate` not `FAR/FRR`; calibration does.

### `VoiceprintEvalSummary`

- `total`, `knownPersonTests`, `unknownTests`, `personCorrectCount`, `unknownCandidateCount`.
- `embeddingsTotal`, `audioSecondsTotal`.
- `embeddingWallMsTotal`, `embeddingCpuMsTotal`, `embeddingAllocatedBytesTotal`.
- `personCorrectRate`, `unknownCandidateRate`.
- `meanBestScore`, `meanMargin`.
- `embeddingWallMsPerAudioSecond`, `embeddingCpuMsPerAudioSecond`.
- `unknownCandidateRate` raw has candidate; **not unknown rejection**; `unknownRejected` in calibration.
- `embeddingsTotal` counts `finite`.
- no `unknownRejected` raw summary.
- no `CPU total` outside embedding; no process total CPU; no total wall; no peak memory. `hr16` `sr-data` numbers below; no `fr/en/nl`.

### `calibrate` output

`ThresholdCalibrator` sweep `MinScore` + `MinMargin`, not `VoiceprintMatcher` mutation.

`CalibrationCandidate`:

- `minScore`, `minMargin`.
- `total`, `trueAccept`, `falseAccept`, `falseReject`, `unknownRejected`, `unknownCount`, `knownCount`, `ambiguousCount`.
- `falseAcceptRate`, `falseRejectRate`, `unknownRejectionRate`, `accuracy`, `balancedErrorRate`, `coverage`.
- known correct = `known && PersonCorrect`.
- `FAR` = false accepts / known predicted; `FRR` = false rejects / expected known.
- unknown rejection = unknown not known / unknown total.
- **no automatic production threshold; output `best` provisional.**
- `CalibrationNote`.
- no `Condition` output; `Language`.
- no `CalibrateByCondition`.
- no diarization `Probability`.
- no `ReverifiedCount`; no `SpeakerAssignment`.
- no `CrossLingual` metric explicit; `language` + `condition` + `PersonCorrect`.
- no `nl` proof.

`ThresholdCalibration`:

- `language`, `best`, `candidateCount`, `candidates`, `note`.
- global `language=""`.
- `calibrate --by-language`.
- `ThresholdCalibrationByLanguage(global, byLanguage)`.
- `CalibrateByLanguage` groups `Language`, `Ordinal`, `""` unlabeled.
- **separate `fr`, `en`, `nl`, `nl-possible`, `cross`, etc.**
- per-language output does not create tested `nl`.
- no session/device.

### `VoiceprintEval` learnings

- `VoiceprintEval` no diarization; no recheck; no UI.
- `VoiceprintMatcher.MatchWithRechecks` exists but **not used by `VoiceprintEval`.**
- `VoiceprintMatcher` `Unknown/Ambiguous` not eval output; calibration infers `Known`.
- no `sherpa-onnx SpeakerEmbeddingManager`.
- no `SpeakerEmbeddingExtractorConfig` C# direct; wrapped.
- no `language` in `Voiceprint`.
- `condition` output only.
- no `Calibration by condition`.
- `MinScore` not inferred automatically on run.
- no `fr/en/nl` measured with `hr16`.
- no `nl`.
- no `nl-possible` default.

## `fr` + `en` + possibly `nl`

### Current `wespeaker_en...` is not enough

Initial `wespeaker_en_voxceleb_resnet34_LM` smoke test used English `sr-data`. It proves
local inference, **not French, Dutch, cross-lingual, unknown speaker, noisy, overlap, or
multilingual identity**. **`wespeaker_en...` name means English**; `VoxCeleb` provenance
alone is not proof for `fr/en/nl`. Current `VoiceprintEval` can point `--model` to another
ONNX, but `wespeaker_en` should not be default for target.

### Best current candidate for `fr/en/nl`

**`hr16/tidyvoicex-samresnet34-onnx`**

- Hugging Face ONNX + **sherpa-onnx compatible**.
- file `tidyvoicex_samresnet34.onnx`, ~**101 MB**.
- **Apache-2.0**.
- base `areffarhadi/Resnet34-tidyvoiceX-ASV`.
- **SimAM-ResNet34 + Attentive Statistical Pooling (ASP)**.
- **256-dim embeddings**.
- **16 kHz**, **80-dimensional log Mel filterbank** internally.
- **multilingual / cross-lingual speaker verification**, not `fr`-only.
- training `VoxCeleb2`, `VoxBlink2`, `TidyVoiceX`.
- `TidyVoiceX` derived Mozilla Common Voice; **40 languages**, **4474 speakers**, **321711 utterances**, **457 hours** in model card.
- same- and cross-language trials.
- model card `TidyVoice` dev **EER 3.07%**, **MinDCF 0.82**; **not measured on your audio; not `fr/en/nl` proof**.
- upstream challenge `TidyVoice 2026`, `TidyVoiceX`, `TidyVoice2026`.
- sherpa-onnx example `provider=cuda` in card; **our CPU provider**.
- `modelId` suggestion `tidyvoicex_samresnet34@hr16-ONNX-sherpa-1.13.8`.
- **tested in this session on English `sr-data`, not `fr/en/nl`**; model **not committed**; **no `nl` result**.
- `VoiceprintEval` `hr16` `sr-data` numbers below; no `fr/en/nl`.
- **no universal seuil** from `EER`.

Download shape:

```bash
curl -L --fail \
 -o /tmp/tidyvoicex_samresnet34.onnx \
 https://huggingface.co/hr16/tidyvoicex-samresnet34-onnx/resolve/main/tidyvoicex_samresnet34.onnx
```

### Other candidates seen; not chosen/measured

**`wespeaker_en_voxceleb_resnet34_LM.onnx`** (initial): sherpa-onnx WeSpeaker ResNet34-LM,
**256-d**, English `en`, `VoxCeleb`, ~25 MB `wespeaker` / ~26 MB collection. measured smoke.
**`onnx-community/wespeaker-voxceleb-resnet34-LM`**: ONNX, **cc-by-4.0**, empty README;
inspect ONNX IO. **`Wespeaker/wespeaker-voxceleb-resnet34-LM`**: official `voxceleb_resnet34_LM.onnx`.
**`pyannote/wespeaker-voxceleb-resnet34-LM`**, **`hbredin/...`**, **`cstr/wespeaker-resnet34-lm-GGUF`**:
not chosen; **GGUF not sherpa ONNX speaker-embedding proof**. **`wespeaker-voxceleb-resnet293-LM`** seen.

**`csukuangfj/speaker-embedding-models`**: sherpa-onnx ONNX collection, no clear model card/licence;
includes `wespeaker_en_voxceleb_resnet34(_LM)`, `wespeaker_en_voxceleb_CAM++(_LM)`,
`wespeaker_zh_cnceleb...`, `3dspeaker_speech_campplus_sv_en_voxceleb_16k`,
`3dspeaker_speech_campplus_sv_zh-cn_16k-common`, `3dspeaker_speech_campplus_sv_zh_en_16k-common_advanced`,
`3dspeaker_speech_eres2net*`, `nemo_en_titanet_*`, `tidyvoicex` mirrors. Verify provenance/license.

**CAM++ candidates**: `wespeaker_en_voxceleb_CAM++`, `wespeaker_en_voxceleb_CAM++_LM`,
`welcomyou/campplus-3dspeaker-200k-onnx`, `Luigi/campplus-zh-en-onnx`, `Maxkuo/...`, `echo-hello/...`.
**Chinese+English `zh+en`, not `fr/nl` proof.** CAM++ can be lighter/faster CPU but quality/CPU unmeasured.
**Known sherpa issue: CAM++ output can depend on input length modulo 2.0 s (`ceil_mode` pooling).**
No `wespeaker_en_voxceleb_CAM++_LM` `fr/nl` proof. **not default.**

**TitaNet candidates**: `nvidia/speakerverification_en_titanet_large` (NeMo, ~23M, CC-BY-4.0,
**English** `en_titanet`, 192-dim speaker embedding, speaker verification/diarization; not ONNX native),
`nemo_en_titanet_large/small` sherpa models, `Recogment/titanet-large-onnx` (**unofficial mirror**,
ONNX 192-dim ~97 MB, CC-BY-4.0, canonical NVIDIA, NeMo preprocessing), `nvidia` + ONNX export.
**No `fr/nl` proof.** NVIDIA warns no warranty.

**SpeechBrain**: `speechbrain/spkrec-ecapa-voxceleb`, **Apache-2.0**, ECAPA-TDNN, VoxCeleb,
cosine; PyTorch; model card **EER 0.80% VoxCeleb1-test cleaned**; no warranty other datasets;
**no ONNX native**; `spkrec-ecapa-tdnn`, `spkrec-xvect-voxceleb`, `...mel-spec`, `vedk00` ONNX mirrors.
**no `nl`.**

**Microsoft**: `microsoft/wavlm-base-plus-sv`, `microsoft/wavlm-large-sv`, `microsoft/unispeech-sat`;
not chosen/measured; no `nl` proof.

**Pyannote**: `pyannote/embedding`, `pyannote/speaker-diarization-3.1`, `pyannote/segmentation-3.0`,
`pyannote/wespeaker-voxceleb-resnet34-LM`; **not speaker identity by itself**; diarization separate;
license/gating verify. `transcribe.cpp` does not expose `pyannote` speaker embeddings.

**TidyVoice extras**: `areffarhadi/W2V-large-tidylang`, `areffarhadi/w2v-bert-TidyVoice`,
`areffarhadi/tidyM-pairs`, `TidyVoiceX`, `TidyVoice`; research. **not ONNX proof.**

**`sherpa-onnx` runtime**: `k2-fsa/sherpa-onnx`, C API + **C# `org.k2fsa.sherpa.onnx`**,
`SpeakerEmbeddingExtractor`, `OnlineStream`, `SpeakerEmbeddingExtractorConfig`,
`SherpaOnnxCreateSpeakerEmbeddingExtractor`, `SherpaOnnxCreateSpeakerEmbeddingManager`.
**`SpeakerEmbeddingManager`** `Add`, `AddList`, `Remove`, `Search`, `Verify`, `Contains`,
`GetAllSpeakers`, `NumSpeakers`, threshold; **not used** in prototype. `SpeakerEmbeddingManagerSearch`
**no margin**; custom `VoiceprintMatcher` chosen. **CAM++ issue / NaN `nemo` issues seen; not proof.**
**Memory growth issue in sherpa C# `SpeakerEmbeddingExtractor` seen; `Dispose` streams/embedder; not solved.**
**No `SpeakerEmbeddingExtractor` streaming diarization proof.**

### Measured `hr16/tidyvoiceX` `VoiceprintEval` run on internet `sr-data`

After committing branch, model `hr16/tidyvoicex-samresnet34-onnx` `tidyvoicex_samresnet34.onnx`
was downloaded outside repo (**~96.24 MB**, not committed). `VoiceprintEval eval` + `calibrate --by-language`
was run on `csukuangfj/sr-data`, same small `sr-data` `corpus.json`, sherpa-onnx **1.13.8 linux-x64 CPU**,
`numThreads=1`, `model-id=tidyvoicex_samresnet34@hr16-ONNX-sherpa-1.13.8`.
**Downloaded model SHA256 `93e35d944e90d50a8d3768b06695b1384aae43880eac05a966006a7bb1127a0a`.**
**Not `fr/en/nl`, not `nl`, not `hr16` on real `fr/nl`, not diarization, not accuracy production.**

```text
total=7 known=5 unknown=2
personCorrect=5/5 personCorrectRate=1.0
unknownCandidate=2 unknownCandidateRate=1
unknownRejected=2 unknownRejectionRate=1
FAR=0 FRR=0 accuracy=1.0 balancedError=0 coverage=0.714285714
knownCount=5 ambiguous=0
meanBestScore=0.657684706 meanMargin=0.1936188019
provisional MinScore=0.525 MinMargin=0
audioSecondsTotal=33.898875 embeddings=7
embeddingWallMsTotal=5925.9235 embeddingCpuMsTotal=5933.239
embeddingWallMsPerAudioSecond=174.8118 embeddingCpuMsPerAudioSecond=175.0276
embeddingAllocatedBytesTotal=2178512
unit tests=8/8; build=0 warnings/errors
```

`calibrate` `MinScore=0.525`, `MinMargin=0`, `candidateCount=7744` **on this 7-row corpus only**;
**not `fr/en/nl`, not production**. `wespeaker_en...` comparison on same `sr-data` had lower `personCorrectRate`;
different model + `modelId`, not comparable calibration. `hr16` still **not `nl` proof**.

### Internet test voices found (not bundled; `hr16` + `fr/en/nl` not measured)

You do **not** need your own recording to smoke-test. Public datasets can provide internet
voices. **No audio copied into branch; no `hr16` + `fr/en/nl` test run on these datasets; no accuracy; verify
current URLs/licenses; `VoiceprintEval` needs 16-bit PCM WAV 16 kHz after conversion.**

- **`areffarhadi/tidyM-pairs`** (Hugging Face, **CC-BY-4.0**, text-only trials):
  `label enroll_path test_path`, `1` same speaker, `0` different; `<speaker_folder>/<utt>.wav`;
  `fr`, `en`, `nl`, **81 languages**; **TidyM/TidyVoiceM** test; ~2.8M pairs; **audio separate**
  `Mozilla Data Collective`, not HF `TidyM`; **TidyVoiceX `areffarhadi/TidyVoiceX` `401` not
  opened; separate `TidyVoiceX` model training corpus; no `nl` measured.** Best shape for
  `PersonCorrect` + cross-lingual; **needs source license check + audio download**.
- **`XRXRX/X-Voice-Testset`** (Hugging Face, custom per-source licenses; **24 kHz mono WAV**):
  `X-Voice`, **30 languages incl `fr`, `en`, `nl`**, `ground_truth`, `waveform`, `prompt`;
  500/lang, >100 speakers, ~5.85 GB; `ECAPA-TDNN >=0.6` prompt/reference consistency;
  **not guaranteed same speaker across languages**; **no `nl` result; convert `24k -> 16k`.**
- **`MiniMaxAI/TTS-Multilingual-Test-Set`** (Hugging Face, **CC-BY-SA-4.0**, ~3.44 MB, 48 rows):
  `french`, `english`, `dutch`, `speaker/french/french_female`, `.../french_male`,
  `speaker/english/english_*`, `speaker/dutch/dutch_*`, `8dutch_*`, `10english_*`, `14french_*`;
  `prompt_text`; human Common Voice audio; one male + one female per language; **not proof
  same speaker cross-lingual; no second independent test utterance per speaker in each language;
  conversion required.** Fine `fr/en/nl` smoke, not `PersonCorrect` calibration.
- **`csukuangfj/sr-data`** (`sherpa-onnx`, GitHub, **Apache-2.0**, WAV):
  `fangjun`, `leijun`, `liudehua`, `enroll/*`, `test/*`; already used `VoiceprintSmoke` +
  `VoiceprintEval`; **not `fr/nl`.**
- **Common Voice / TidyVoice** (`Mozilla`, **CC0/per-version** + **CC-BY-4.0 TidyM**):
  `fr`, `en`, `nl`, `client_id`/`speaker_id`, `TidyVoiceX` 40 langs; **not assume `nl`;
  license/source check; no own voice needed.**
- **Multilingual LibriSpeech / MLS** (`OpenSLR 94`, **CC-BY-4.0**): `fr`, `nl`, `en`,
  separate speakers; not cross-lingual same speaker. **VoxCeleb / VoxPopuli / Multilingual TEDx**
  possible speaker data; no `fr/nl` proof.
- `CSS10` single speaker per language not cross-lingual. `SpeechDataAI/dutch...`, `TTS-AGI...`
  `nl` candidates, licenses/price/source. **Synthetic TTS data not proof for real `nl`.**

### `fr/en/nl` corpus recommendations

Must be **real local data**, not synthetic. Internet real audio can be downloaded; **not** fabricated.
Same speaker `fr/en/nl`. Separate `fr`, `en`, `nl`,
`nl-possible`, `cross`, `unknown`. **Dutch only if `nl` truly required; not assume model `nl`.**

- `language` `fr`, `en`, `nl`, `nl-possible`.
- `condition` `clean`, `noisy`, `short`, `overlap`, `cross`, `noisy-nl`, etc.
- `enrollment` clean profile; `test` same + cross.
- multiple sessions/devices/microphones; not one file.
- `unknown` `fr/en/nl`, `short`, `noisy`, `overlap`, `NaN`-safe, unreadable handled.
- `unknownCases`.
- cross-lingual pairs `fr→en`, `nl→en`, `fr→nl`, `en→nl`, `en→fr`, `nl→fr`.
- enrollment language vs test language recorded **manually**; no fields currently.
- no `fr/en/nl` profile `Voiceprint.Language`.
- `PersonCorrect` exact `PersonId`, no language normalization.

### `fr/en/nl` + diarization separate problem

`hr16` speaker embedding **does not fix `transcribe.cpp` diarization**. `Sortformer`
**`diar_streaming_sortformer_4spk-v2.1` declares English only** in project docs; warning if chosen
language not declared; **not measured French `fr`**, **not measured Dutch `nl`**. `Sortformer`
**4 speakers**. ASR `fr/en/nl` separate; `moss`, `granite`, `multitalker` not proof `nl`.
**`SpeakerSnippetExtractor` needs good diarized turns.** `SpeakerAssignment` heuristic.
For `fr/en/nl`, test diarization separately. `transcribe.cpp` `Speaker 0` fallback not identity.
No `nl` diarization model claimed.

## CPU findings

**`transcribe.cpp` + `transcribe` already uses CPU/GPU; speaker embedding is additive.** Original
`transcribe.cpp` has no speaker embedding; this branch adds ONNX speaker model. `sherpa-onnx`
ONNX Runtime CPU; **no cloud API cost** in this implementation; in cloud deployment compute billed.

```text
total = transcribe + diarize + N_embeddings * embedding + N_rechecks * embedding + matching
matching = N_turns * N_profiles * Dimension + overhead
```

### Cost drivers

- `N_embeddings`: one per snippet; no per-word by default.
- `N_rechecks`: `MaxRechecks`, not unbounded; no auto recheck in `VoiceprintEval`.
- snippet duration: `minTurnMs`, `maxSnippetMs`, `paddingMs`, quality filtering.
- `NumThreads`, `Provider`, CPU/NPU/GPU, quantized ONNX, `model`, `dim`.
- `model size`; `hr16` ~101 MB; `wespeaker` ~25 MB; `titanet` ~97 MB; no CPU measured for all.
- `Dimension` matching; matching cheap vs neural embedding but not free.
- no `sherpa` batching in `VoiceprintEval`; sequential calls.
- transcribe + diarize + speaker sequential in app; **no total CPU hiding**.
- `VoiceprintEval` actual `EmbeddingCpuMsPerAudioSecond`, `EmbeddingWallMsPerAudioSecond`.
- no `GC` global effects removal; `EmbeddingAllocatedBytes` delta, `GC.GetTotalAllocatedBytes`.
- `Process.TotalProcessorTime` delta; **not full-process CPU**, **not total transcription CPU**.
- `SpeakerAssignment` O(segments * turns + words * turns), no speaker model.
- no `NaN`/unknown extra model call; no crash retries; no `Skipped`.
- no streaming diarization; offline whole buffer `Sortformer`.
- **no automatic re-verification if not `Known`.**

### Optimization findings

- enroll once; save local `Voiceprint`; **no re-enrollment per test**.
- `VoiceprintMatcher` only needs saved embeddings; **raw audio not required**.
- don't call embedder per word; per `SpeakerSnippet` / turn; aggregate votes.
- filter too short/noisy/overlap; `minTurnMs`, `maxSnippetMs`, padding; `maxSnippetMs`.
- `MinTurn` `SherpaOnnx IsReady`; too-short fails; catch `Finite=false`.
- `MaxRechecks=0` or low.
- recheck only `Ambiguous`/`Unknown`/`NeedsReverification`, not all.
- `UnknownRejected`, `FAR/FRR`, not `sim_ab`.
- don't run diarizer if speaker identity not needed; `WithDiarize` OFF; no speaker model if `Speaker 0` acceptable.
- `SpeakerAssignment` separate; no speaker-embedding for non-attributed segments.
- one model loaded; sequential; no parallel `SherpaOnnxSpeakerEmbedder` sharing.
- `NumThreads=1` measured; more threads must be measured; no guarantee.
- CPU-only `Provider="cpu"`; no GPU package in project.
- **compression reduces storage, not inference CPU**; matching compressed embeddings after decompress same.
- **no `fr/en/nl` CPU claims.**

### Measured `VoiceprintEval` run on `sr-data` (not `fr/en/nl`, not benchmark)

- sherpa-onnx 1.13.8 + `wespeaker_en...` + `sr-data`.
- `total=7`, `known=5`, `unknown=2`.
- raw `personCorrect=4`, `personCorrectRate=0.8`.
- `unknownCandidate=2`, `unknownCandidateRate=1`.
- calibration `unknownRejected=2`, `unknownRejection=1`.
- `MinScore≈0.794133`, `MinMargin≈0.008992` **on this tiny corpus only**.
- `FAR=0`, `FRR=0.2`, `accuracy=0.857142857`, `BER=0.1`, `coverage=0.571428571`.
- `meanBestScore=0.845851`, `meanMargin=0.039201`.
- `audioSecondsTotal=33.898875`, `embeddingsTotal=7`.
- `embeddingWallMsTotal≈1346-1348`, `embeddingCpuMsTotal≈1373-1385`.
- `embeddingWallMsPerAudioSecond≈39.72-39.77`, `embeddingCpuMsPerAudioSecond≈40.51-40.88`.
- `embeddingAllocatedBytesTotal≈2,178,176`.
- numbers vary run to run; no accuracy; no production.
- no `hr16` `fr/nl`.
- no `nl`.
- no diarization CPU.

## Calibration and ambiguity learnings

### What `AmbiguityPolicy` does / doesn't

- no universal `MinScore`, `MinMargin`, `MaxRechecks`.
- no `FAR/FRR` inside `VoiceprintMatcher`.
- `MinScore` no `unknown` special; `PersonCorrect` separate.
- `Known` can be wrong person.
- no `SpeakerSegmentResult.Probability`.
- no `SpeakerAssignment` probability.
- no `condition` / `language` effect inside `VoiceprintMatcher`.
- no per-language `AmbiguityPolicy` selection in `VoiceprintMatcher`.
- `VoiceprintEval` calibrates raw `results`, then manual apply.

### Calibration separation

`VoiceprintEval` intentionally separates **raw top-1** from **`Known/Unknown/Ambiguous`**:

```text
actualPersonId raw top1 -> calibrate `Known`
```

- `unknownCandidateRate` raw != unknown rejection.
- `FAR/FRR` needs `PersonCorrect`.
- `unknownRejected` needs `Known`.
- `FAR=0` can coexist `FRR>0`, `Coverage<1`.
- **per-language + cross-lingual calibration** needed.
- `fr`, `en`, `nl` not automatically same seuil.
- `nl-possible` not proven `nl`.
- no `FAR/FRR` guarantee.
- no production threshold.
- no `SpeakerSegmentResult.Probability`.
- `Finite=false` excluded from `hasCandidate`, but still total/unknown/falseReject; no `Skipped`.
- `unknown` no saved candidate vs wrong saved candidate not fully distinguished in `Unknown`.
- `Ambiguous` separate from `Unknown`.
- `ReverifiedCount` not used calibration.
- no voting.

### Re-verification recommendations (not implemented automatically)

`SpeakerMatch.NeedsReverification` + `MatchWithRechecks`. Current recheck needs alternative
embeddings. Suggested bounded caller logic:

1. trigger only `Known==false`, `Ambiguous`, `Unknown`, `NaN`, low `Probability`, low `BestScore`, low `Margin`, too-short, noisy, overlap, no candidate.
2. keep raw `SpeakerSegments`, `Words`, original PCM.
3. re-crop; margin; clamp; drop silence; drop overlap; drop too-short; choose better snippet.
4. drop noisy; no `NaN`.
5. recompute embedding; alternative snippet; aggregate votes by `PersonId`, not `SpeakerId`.
6. optional re-run diarizer whole buffer/region; `SortformerPreset`; model; `maxSnippetMs`; adjust `minTurnMs`.
7. re-map ids; **don't assume `SpeakerId` stable**.
8. recompute `SpeakerAssignment` afterward, but no probability propagation.
9. `MaxRechecks`, no loop; otherwise `Unknown`/`Ambiguous`.
10. user confirmation / abstain.
11. `NeedsReverification` does not itself call anything.

**No `AmbiguityRechecker`, no automatic second diarizer pass, no `SpeakerEmbeddingManager` integration.**

## Future storage: compressed containers

Current storage is **local JSON embeddings**. **Compression is not encryption.** No raw voice.

**`JsonVoiceprintStore` current:** JSON + atomic temp+move; no `.zip`, no `.tgz`, no `.zst`, no `SQLite`,
no `IVoiceprintStore`, no `IVoiceAudioStore`, no `CompressedVoiceprintStore`, no `EncryptedVoiceprintStore`,
no audio backup, no `fr/en/nl` `Voiceprint.Language`, no `Condition`, no schema version, no `model sha` field
beyond `EmbeddingModelId`, no `CreatedAt` UTC enforcement, no concurrent writes, no encryption.

**Voiceprint compression** (`.zip`, `.tgz`, `.zst`, `SQLite`, etc.) can be added later as a store
backend **without changing `VoiceprintMatcher`** if preserve:

- `PersonId`, `DisplayName`, `EmbeddingModelId`, `Dimension`, `Embeddings`, `CreatedAt`, `SourceSampleCount`.
- model version + quantization + **model SHA256** + embedding `schemaVersion`.
- no model mixing; no cross-model cosine.
- atomic writes, backup, restore, `RemoveAsync`, migration.

**Raw voice compressed container** is separate. If user says keep voices in compressed container:

- `.zip`, `.tgz`, `.zst`, `tar`, `wavpack`, `opus`, `flac`; original audio vs derived embeddings distinct.
- `VoiceprintEval` currently reads individual 16k 16-bit WAVs, **not compressed corpus container**.
- `PcmExtensions.ReadWavToPcm` reads WAV path, no zip stream; extractor would be needed.
- `VoiceprintEval` no audio store; `JsonVoiceprintStore` no audio.
- **compressed audio not needed for matching if embeddings saved.**
- raw voice useful re-enrollment/recheck/audit; **more sensitive privacy**.
- no claim current supports it.
- **privacy, consent, retention, deletion, access control, encryption**.
- **compression != encryption**.
- **no cloud**; `JsonVoiceprintStore` no secret; no `Password`/`Key`.
- no concurrency safe current.

**Recommended future `IVoiceprintStore` + `CompressedZipVoiceprintStore` + `EncryptedVoiceprintStore`
+ separate `IVoiceAudioStore`; not implemented.**

## Minimal end-to-end flow

```text
1. PCM 16 kHz mono finite (`PcmExtensions.ReadWavToPcm` or `AudioLoader` + resample elsewhere)
2. `model.Supports(FeatureDiarization)` / model role
3. `DiarizeSession.Run` whole buffer OR `WithDiarize`
4. keep raw `SpeakerSegmentResult` `SpeakerSegments` + `Words`
5. `SpeakerSnippetExtractor.Extract`
6. filter `SpeakerId != 0`, minTurn, overlap, noisy, short, maxSnippet, padding
7. `ISpeakerEmbedder.EmbedAsync`
8. `VoiceprintMatcher.MatchWithRechecks`
9. separate `Known/Unknown/Ambiguous`, `NeedsReverification`
10. bounded recheck, not auto
11. map per-run `SpeakerId` -> `PersonId`
12. optional `SpeakerAssignment.Apply`
13. `speakerNames` user-approved display names
14. raw transcript unchanged
```

Speaker-embedding only flow **without diarization** is `VoiceprintEval`. Diarized identity flow
currently **caller-composed**; not one `transcribe.cpp` call.

## `fr/en/nl` operational plan

1. pick **`hr16/tidyvoicex_samresnet34.onnx`** candidate; verify **Apache-2.0 + SHA256 + sherpa version + provider + `Dimension=256`**.
2. pin `modelId`; no `wespeaker_en` for `nl`.
3. prepare 16k/16-bit WAVs; convert; no `.zip` currently.
4. corpus `fr/en/nl` same speakers; `unknown`; `unknownCases`; `language`; `condition`; cross.
5. `VoiceprintEval eval`.
6. `calibrate` global + `--by-language`.
7. evaluate `FAR/FRR`, unknown, `PersonCorrect`, CPU, `NaN`, `Finite`, `Coverage`, `BER`.
8. choose `MinScore`, `MinMargin`, `MaxRechecks` per `fr/en/nl/cross`.
9. separate diarization test `Sortformer` English-only warning; no `nl` proof.
10. only then implement recheck/names/persistence/compression/production.

## Not provided / not proven

No `transcribe.cpp` speaker embedding, no `transcribe.cpp` `PersonId`, no `SpeakerId` stable,
no `SpeakerAssignment` probability, no streaming diarization, no combined ASR+diarize, no `nl`,
no `fr/en/nl` accuracy, no `hr16` `fr/en/nl` test, no universal thresholds, no `FAR/FRR` guarantee, no re-verification
automation, no `VoiceprintEval` recheck, no `VoiceprintEval` diarization, no `SpeakerNames`, no
`IVoiceprintStore`, no compression, no encryption, no concurrent writes, no raw audio, no `transcribe`
ASR `nl` proof, no `Sortformer nl`, no `Sortformer` unEnglish proof, no `SpeakerEmbeddingManager`,
no `SpeakerEmbedding` probability, no `Calibration by condition`, no `CrossLingual` metric, no `nl-possible`
special behavior, no `GPU/NPU` claims, no `batching`, no `streaming`, no `memory` guarantee, no CPU hiding,
no production claims. `SpeakerAssignment` not verified. **This is a prototype.**

## Validation at time of writing

- full `dotnet build TranscribeCppSharp.slnx`: **0 warnings / 0 errors**.
- `dotnet test tests/TranscribeCppSharp.Voiceprint.Tests`: **8/8**.
- `VoiceprintSmoke`: real `wespeaker_en...` inference.
- `VoiceprintEval eval` + `calibrate --by-language`: real `sr-data` run.
- `git diff --check`: clean.
- `hr16` **tested on `sr-data`, not `fr/en/nl`**; **not `nl`**; **branch committed before attempt**.
