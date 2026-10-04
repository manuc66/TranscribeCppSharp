---
title: Models
nav_order: 5
---

# Models

## Choose a model

The model argument accepts three forms:

```bash
transcribe audio.wav --model whisper-tiny            # curated alias (72 across all families)
transcribe audio.wav --model whisper-tiny --quant Q8_0
transcribe audio.wav /path/to/my-model.gguf           # local file, fully offline
transcribe audio.wav handy-computer/parakeet-tdt-0.6b-v3-gguf/parakeet-tdt-0.6b-v3-Q5_K_M.gguf
```

`--list-models` prints every alias with its quantization, license and size, and
flags non-commercial licenses; `--model-info <alias>` gives the full record
(pinned revision, sha256, license URL). Excerpts:

```text
$ transcribe --list-models
alias                                    quant    license              size unit
breeze-asr-25                            Q5_K_M   apache-2.0           1.08 GB
canary-1b                                Q5_K_M   cc-by-nc-4.0 !     798.89 MB
parakeet-tdt-0.6b-v3                     Q5_K_M   cc-by-4.0          523.52 MB
whisper-large-v3-turbo                   Q5_K_M   apache-2.0         590.92 MB
…
! = non-commercial license; verify before any commercial use.

$ transcribe --model-info whisper-tiny
whisper-tiny
  repo       : handy-computer/whisper-tiny-gguf
  revision   : 2678cc66038359b97c8e6fd6454c56fc9006d571
  quant      : Q5_K_M
  file       : whisper-tiny-Q5_K_M.gguf
  size       : 42.16 MB
  license    : apache-2.0
  license url: https://huggingface.co/handy-computer/whisper-tiny-gguf
```

Sizes scale to KB, MB or GB, with the unit in its own column so the numbers stay
aligned across the range — the catalogue runs from 33 MB to 16 GB. The base is
1024, so `MB` is what the binary prefix calls MiB. The GUI shows the same
figures, from the same formatter; it used to round everything to MB and
disagreed with the terminal about the same model.

## Alias or spec

Any GGUF that transcribe.cpp supports works through the generic
`<owner>/<repo>/<file.gguf>[@<revision>]` form, so the alias list is a
convenience, not a limit. Two differences from an alias: the license is unknown
(only the model card is linked) and the sha256 comes from the HuggingFace
metadata on **every** run, because a spec carries no hash of its own — so a spec
needs the network even when the weights are already cached. A curated alias pins
its revision *and* its sha256 in the shipped manifest, which is why it is
offline from the second run on. Pinning `@<revision>` only removes the
"which revision is current?" lookup, not the file lookup.

## Caching

Models are cached under `$XDG_CACHE_HOME/TranscribeCppSharp/models`
(`%LOCALAPPDATA%` on Windows, `~/Library/Caches` on macOS). Weights are never
bundled with the tool.

## Model capabilities

A model is asked what it can do, not assumed. From the library:

<!-- @readme model-capabilities -->
```csharp
Backends.InitDefault(); // optional: automatic in Model.Load, but explicit is clearer
var modelPath = TestConfig.ModelPath; // your GGUF model file, e.g. "test-models/ggml-tiny.bin"
using var model = Model.Load(modelPath, p => p.WithBackend(BackendRequest.BackendCpu));
var supportsPnc = model.Supports(Feature.FeaturePnc);
var caps = model.GetCapabilities();
```
<!-- @end model-capabilities -->

`model.Supports(Feature.FeatureDiarization)` is the one worth calling before a
run, because it is the only way to know in advance whether speaker attribution
is possible — see [Speaker diarization](diarization.md).

## Model licenses

The MIT license covers this wrapper and the bundled native library, **not the
models you load with it**. GGUF models come from different ecosystems with
different licenses — some are permissive (MIT, Apache-2.0), some are
non-commercial (e.g. CC-BY-NC-4.0 for some Parakeet/Canary variants). This
project does not bundle or redistribute models, and it does not verify or
curate their licenses.

Before using a model in a commercial product, check the license on the page
you download it from (typically Hugging Face). The [upstream transcribe.cpp
docs](https://github.com/handy-computer/transcribe.cpp/blob/v0.3.0/docs/models)
describe each supported family and where its models come from; that is the
source of truth, not this page.

transcribe.cpp supports every model family it can decode — 16 families upstream
— and the alias set spans them: Whisper, Moonshine, Parakeet, Canary, GigaAM,
Voxtral, Qwen3-ASR, MOSS diarization, and others.
