---
title: Command-line tool
nav_order: 2
---

# Command-line tool

`transcribe` is the fastest way to try this project: it needs no code, no
project file, and no manual model handling.

```bash
dotnet tool install -g TranscribeCppSharp.Cli
```

The tool is published per platform (win-x64, linux-x64, linux-arm64, osx-x64,
osx-arm64), and the native runtime for your platform is embedded in the package,
so nothing native is downloaded at first run. It needs the .NET 10 runtime.

Each aspect of the tool has its own page, because each has its own failure modes:
[Compute](compute.md), [Models](models.md), [Exporting](exporting.md),
[Long audio and diarization](diarization.md) and [Audio input](audio-input.md).

## Transcribe a file

```bash
transcribe jfk.wav --model whisper-tiny
```

The model is fetched from HuggingFace on first use (42.16 MB for that alias),
verified by sha256 and cached. Aliases carry a pinned revision, so later runs
reuse the cache and need no network. Console output below, with the
`ggml_*`/`load_backend` native log lines and the two timing lines (per-window
progress and the real-time factor, which depend on your hardware) omitted. The
device name is a placeholder; decimal separators follow your locale:

```text
audio : 176 000 samples = 11,0s @ 16kHz mono
model : whisper/whisper-tiny
compute: Vulkan0 on NVIDIA RTX 4090 [requested: auto]
diarization supported: False
window: 300s (model max audio 0s)

=== window 1 @ 00:00.0 (176 000 samples) ===
  lang   :    aborted: False  truncated: False
  full   : And so my fellow Americans ask not what your country can do for you, ask what you can do for your country.

=== summary ===
  windows: 1
```

The `compute:` line is not decoration: it reports the backend the model
**actually** landed on, so you can see the device rather than assume it. See
[Compute](compute.md).

## All options

```text
$ transcribe --help
Transcribe audio with a speech-to-text model. Supports every model family
that transcribe.cpp supports (Whisper, Moonshine, Parakeet, Canary, GigaAM,
Voxtral, Qwen3-ASR, MOSS diarization, …).

The model's role decides what runs (transcribe.cpp 0.4): an ASR model is
transcribed, a diarization model (Sortformer) prints speaker turns, a
language-identification model prints ranked languages, a voice-activity
model prints speech segments.

Usage: transcribe <audio> [model] [options]

  <audio>        WAV (16 kHz mono 16-bit) read directly; any other
                 format (ogg, mp3, m4a, …) is decoded with ffmpeg
                 (must be installed)
  [model]        a model file path, a known alias (default:
                 moss-transcribe-diarize), or a HuggingFace spec
                 '<owner>/<repo>/<file.gguf>[@<revision>]'.
                 Aliases and any other supported GGUF are downloaded
                 from HuggingFace on first use and cached.

  --model <m>    same as the [model] argument
  --quant <q>    quantization for a known alias (e.g. Q4_K_M, Q5_K_M,
                 Q8_0, F16); default is per model (see --list-models)
  --list-models  list the known model aliases and exit
  --model-info <alias>  show details (family, params, license, upstream catalog)
  --cache-size   how many models are cached, and how much they take
  --remove-model <alias>  delete a cached model's file and report what
                 was freed
  --backend <b>  compute backend: auto (default), cpu, cpu-accel,
                 metal, vulkan, cuda, rocm. 'auto' runs on the GPU
                 when one initializes (every discrete GPU is probed
                 before the integrated ones) and falls back to the CPU
                 otherwise; a forced backend fails instead of falling
                 back
  --device <n>   run on that exact device (index from --list-devices);
                 never falls back to another device
  --list-devices list the compute devices this build and machine can see
  --lang <code>  language code for the decoder (default: en)
  --allowed <codes>  language-identification model only: comma-separated
                 codes to rank (e.g. en,fr,de); default is all labels
  --chunk <sec>  max per-transcription window in seconds (default: 300);
                 long audio is split with 1 s overlap and deduplicated
  --vad <model>  run a VAD model (e.g. silero-vad-v6.2) over the file
                 first and transcribe only the speech it finds, with
                 the original timestamps. Unmeasured: it changes what
                 the model sees, so the text can differ. Off by default
  --no-diarize   disable speaker diarization
  --out <file>   write the report to a file; format controlled
                 by --format (plain/vtt/json/html/srt/ass)
  --format <fmt> report file format: plain (default, timestamped
                 lines), vtt (WebVTT, speaker cues; ASR models only),
                 json, html (numbered paragraphs with timestamps and
                 speakers; ASR models only), srt or ass (subtitles,
                 ASR models only)
  --doctor       check the native library, ffmpeg and the compute devices,
                 and print what is missing (never downloads); with
                 --verify-cache it also re-hashes the cached models;
                 --json prints one object instead of the lines
  --version      print the tool and native library versions and exit
  --help         show this help
```

This block is kept honest by a test: `HelpTextBlock_ShouldBeTheRealHelpText`
in `tests/TranscribeCppSharp.Interop.Tests/ReadmeExamplesTest.cs` compares it to
`TranscribeCommand.HelpText` on every build. It had drifted once — the compute
options were missing here and present in the tool — which is why the check exists.

## Diagnose: `--doctor`, `--cache-size`, `--remove-model`

`transcribe --doctor` answers "why doesn't this work?" in one pass, without a
model, a network or a download:

```
ok    tool      0.4.1+bb8e6aa
ok    native    transcribe.cpp 0.4.1 (4d7c1cc)
ok    abi       RunParams and ModelLoadParams match the native library
ok    ffmpeg    /usr/bin/ffmpeg
warn  ffplay    not found on PATH; only the desktop app plays excerpts
ok    backends  Vulkan0, Vulkan1, CPU
ok    cache     /home/you/.cache/TranscribeCppSharp/models: 8 model(s), 7.17 GB
warn  privacy   readable by other local accounts: ui.log
```

It exits 1 only when a check **fails** (no native library, a wrapper and a
native package from different versions, no compute device, a cached model whose
bytes do not match the manifest); a warning is something to know about, not a
blocker. `--json` prints one object instead of the lines, so a script can read
it — the native logger is routed to stderr for exactly that reason.
`--verify-cache` adds a check that re-hashes every cached model against the
sha256 the manifest pins; that reads every cached weight, so it takes as long as
reading the cache.

`--cache-size` prints how many models are cached and what they take.
`--remove-model <alias>` deletes one cached model and says what it freed; an
alias that is not cached is not an error, and an unknown alias names
`--list-models`. Files cached from a HuggingFace spec (not a curated alias) are
not in the manifest, so they are reported by `--doctor --verify-cache` only when
they match an alias; remove those from the cache directory by hand.

## What the CLI does not do

Stated plainly, so nothing is implied:

- **One file per run.** No batch or directory input. The library's `Batch.Run`
  does batch, but that is a library API, not a tool flag.
- **No interactive/streaming mode.** Each window is transcribed to completion;
  see [Real-time streaming](streaming.md) for the incremental library API.
- **Blocking.** Transcription runs on the calling thread, as in the native
  library ([Concurrency](concurrency.md)).
- **No GPU in CI.** The test suite does run the tool end to end — argument
  parsing, the exported files, and a real transcription of the bundled test
  audio on the CPU, both in process and by launching the executable — but the
  runners have no GPU, so the GPU selection itself is only covered by the
  device-selection policy tests, not by a real run. Speaker attribution with a
  diarization model is covered only when the opt-in MOSS asset is fetched
  (`WITH_DIARIZATION_MODEL=1 ./scripts/run-integration-tests.sh`); CI does not
  fetch it.
- **No CUDA runtime ships with the package.** The bundled binaries are CPU +
  Vulkan on Windows/Linux and Metal on macOS. Using an NVIDIA GPU means
  providing your own CUDA build — see [Using CUDA](compute.md#using-cuda).

## Trimming silence with `--vad`

`--vad <model>` runs a VAD model (Silero, `silero-vad-v6.2` is one of the curated
aliases) over the file before the ASR model sees it, then transcribes only the
speech intervals. Timestamps are mapped back to the original file, so the output
still lines up with the audio; each interval is padded by 300 ms, intervals less
than 1 s apart are merged, and anything shorter than 250 ms is dropped as noise.

Two things are true and worth saying plainly:

- **It changes what the model sees.** The ASR model no longer has the context
  across a pause, so the transcript can differ from a normal run — sometimes
  better, sometimes worse. There is no accuracy measurement behind this flag.
  The 1 s merge gap is not arbitrary: on the repository's 11 s test clip
  (`test-audio/jfk.wav`), a 500 ms gap split the sentence into four cues and one
  of them decoded as "IS NOT" instead of "ask not"; at 1 s the clip came back as
  one cue and the sentence was correct. One clip is not a benchmark, but it is
  the reason for the default.
- **It costs an extra pass** over the whole file with the VAD model. Nothing in
  this project measures whether the total time goes down; on a file with long
  silences it may, on dense speech it will not.

Without `--vad` nothing changes: the file is cut every `--chunk` seconds as
before. The planning rules are in `src/Shared/SpeechWindows.cs`, and they are
unit tested without a model.

## What it supports

76 curated model aliases across every family transcribe.cpp supports, on the GPU
by default, with speaker diarization and plain/WebVTT/JSON export. Which model
families those are, and how their licenses differ, is on
[Models](models.md#model-licenses).
