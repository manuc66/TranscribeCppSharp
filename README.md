# TranscribeCppSharp

[![CI](https://github.com/manuc66/TranscribeCppSharp/actions/workflows/ci.yml/badge.svg)](https://github.com/manuc66/TranscribeCppSharp/actions/workflows/ci.yml)
[![CodeQL](https://github.com/manuc66/TranscribeCppSharp/actions/workflows/github-code-scanning/codeql/badge.svg)](https://github.com/manuc66/TranscribeCppSharp/actions/workflows/github-code-scanning/codeql)
[![Code Coverage](https://codecov.io/gh/manuc66/TranscribeCppSharp/branch/main/graph/badge.svg)](https://codecov.io/gh/manuc66/TranscribeCppSharp)
[![Quality Gate Status](https://sonarcloud.io/api/project_badges/measure?project=manuc66_TranscribeCppSharp&metric=alert_status)](https://sonarcloud.io/dashboard?id=manuc66_TranscribeCppSharp)
[![CodeFactor](https://www.codefactor.io/repository/github/manuc66/transcribecppsharp/badge)](https://www.codefactor.io/repository/github/manuc66/transcribecppsharp)
[![FOSSA Status](https://app.fossa.com/api/projects/git%2Bgithub.com%2Fmanuc66%2FTranscribeCppSharp.svg?type=shield)](https://app.fossa.com/projects/git%2Bgithub.com%2Fmanuc66%2FTranscribeCppSharp?ref=badge_shield)
[![NuGet Version](https://img.shields.io/nuget/v/TranscribeCppSharp.svg)](https://www.nuget.org/packages/TranscribeCppSharp)
[![NuGet Downloads](https://img.shields.io/nuget/dt/TranscribeCppSharp.svg)](https://www.nuget.org/packages/TranscribeCppSharp)
[![Docs](https://img.shields.io/badge/docs-manuc66.github.io/TranscribeCppSharp-blue)](https://manuc66.github.io/TranscribeCppSharp/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

.NET bindings for [transcribe.cpp](https://github.com/handy-computer/transcribe.cpp): load GGUF speech-to-text models and transcribe audio (16 kHz mono float PCM) from C#.

Three ways to use it:

- **A command-line tool** — `dotnet tool install -g TranscribeCppSharp.Cli` gives you `transcribe`, which downloads a curated model on first use and prints or exports the transcript. See [Command-line tool](docs/cli.md).
- **A .NET library** — the `TranscribeCppSharp` wrapper (plus the native runtime package for your platform) for C# code. See [Getting started](docs/getting-started.md).
- **A desktop app** — `TranscribeCppSharp.Ui`, an Avalonia GUI over the same wrapper. It is **not distributed as a package**: there is no installer and no NuGet package for it, so you run it from a clone. See [Desktop app](#desktop-app).

📖 **[Full documentation →](https://manuc66.github.io/TranscribeCppSharp/)** — the guide, the reference, and the measured limits with the method behind each one. Published from [`docs/`](docs/); this README is the short version and the details are delegated to those pages.

## Installation

The wrapper is platform-agnostic and ships no native binaries. A minimal install is the wrapper plus the native runtime package for your platform:

```bash
dotnet add package TranscribeCppSharp
dotnet add package TranscribeCppSharp.Native.linux-x64   # pick your platform
```

If you would rather not pick a platform (or want one package that works everywhere), install the bundle meta-package instead — it pulls the wrapper and every native runtime:

```bash
dotnet add package TranscribeCppSharp.Bundle
```

Native runtime packages: `TranscribeCppSharp.Native.linux-x64`,
`.linux-arm64`, `.win-x64`, `.osx-arm64`, `.osx-x64`.

*Note: For Linux Alpine (musl) or other platforms, see
[Building from source](docs/development.md#building-from-source). Like
[Using CUDA](docs/compute.md#using-cuda), a custom native build is picked up
automatically when placed in the app output directory.*

The wrapper resolves `libtranscribe` automatically in plain `dotnet run` scenarios (no
`<RuntimeIdentifier>` needed). If the native library is still missing at runtime (e.g. you
forgot the runtime package), the wrapper throws a `DllNotFoundException` that lists the
exact package to add for your platform and the paths it searched. It does not silently
produce a misleading error. See
[Installation](docs/getting-started.md#installation) and
[Native library loading](docs/architecture.md#native-library-loading).

## Quick Start

The four snippets below are checked by a test: each is compared byte-for-byte against a
marked region in `HighLevelApiTests.cs` and `samples/SmokeTest/Program.cs`, so they
cannot drift from the API without failing the build
([`ReadmeExamplesTest`](docs/development.md#tests-that-keep-the-documentation-honest)).

### Basic Transcription

`Model.Load` initializes the compute backends automatically on first use, but you can (and for custom setups, should) do it explicitly with `Backends.InitDefault()`:

<!-- @readme basic-transcription -->
```csharp
Backends.InitDefault(); // optional: automatic in Model.Load, but explicit is clearer
var modelPath = TestConfig.ModelPath; // your GGUF model file, e.g. "test-models/ggml-tiny.bin"
var audioPath = TestConfig.AudioPath; // your WAV audio file, e.g. "test-audio/jfk.wav"
using var model = Model.Load(modelPath, p => p.WithBackend(BackendRequest.BackendCpu));
using var session = model.CreateSession();
var pcm = PcmExtensions.ReadWavToPcm(audioPath);
var transcript = session.Run(pcm);
```
<!-- @end basic-transcription -->

Omit the `WithBackend` call to get the default `AUTO` policy, which runs on the GPU
whenever one initializes — see [Compute](docs/compute.md).

### Batch Processing

<!-- @readme batch-transcription -->
```csharp
Backends.InitDefault(); // optional: automatic in Model.Load, but explicit is clearer
var modelPath = TestConfig.ModelPath; // your GGUF model file, e.g. "test-models/ggml-tiny.bin"
var audioPath = TestConfig.AudioPath; // your WAV audio file, e.g. "test-audio/jfk.wav"
using var model = Model.Load(modelPath, p => p.WithBackend(BackendRequest.BackendCpu));
using var session = model.CreateSession();
var pcm1 = PcmExtensions.ReadWavToPcm(audioPath);
var pcm2 = PcmExtensions.ReadWavToPcm(audioPath);
var results = Batch.Run(session, new[] { pcm1, pcm2 });
```
<!-- @end batch-transcription -->

### Real-Time Streaming

<!-- @readme streaming-transcription -->
```csharp
stream.Begin();
int chunkSize = 16000; // 1 second
for (int i = 0; i < pcm.Length; i += chunkSize)
{
    int length = Math.Min(chunkSize, pcm.Length - i);
    var chunk = pcm.AsSpan(i, length);
    stream.Feed(chunk);
}
stream.Complete();
var text = stream.GetCurrentText();
```
<!-- @end streaming-transcription -->

### Model Capabilities

A model is asked what it can do, not assumed. `model.Supports(Feature.FeatureDiarization)`
is the one worth calling before a run:

<!-- @readme model-capabilities -->
```csharp
Backends.InitDefault(); // optional: automatic in Model.Load, but explicit is clearer
var modelPath = TestConfig.ModelPath; // your GGUF model file, e.g. "test-models/ggml-tiny.bin"
using var model = Model.Load(modelPath, p => p.WithBackend(BackendRequest.BackendCpu));
var supportsPnc = model.Supports(Feature.FeaturePnc);
var caps = model.GetCapabilities();
```
<!-- @end model-capabilities -->

Speaker attribution, long audio and windowing: [Long audio and diarization](docs/diarization.md).

## Desktop app

`TranscribeCppSharp.Ui` is an [Avalonia](https://avaloniaui.net/) app on .NET 10 that drives
the same wrapper and the same native library — it adds no transcription logic of its own, and
its limits are the library's. It is built in CI on Linux, macOS and Windows. As noted above
it is distributed as source, so it runs from a clone:

```bash
dotnet run --project tools/FetchNative            # once: the native library the app loads
dotnet run --project src/TranscribeCppSharp.Ui
```

Five tabs, one per job:

| Tab | What it does |
|---|---|
| **Transcription** | Pick a file, a model and the decoder settings (language, threads, KV type, context size, window length, and the knobs that only some model families have). The result comes back as text, as segments, as words, or as speaker-attributed segments, and exports to `.txt`, `.vtt` or `.json`. |
| **Streaming** | Transcribe the microphone live, with committed text on the left and the tentative part the model may still revise on the right. The commit policy and the family-specific knobs are there. |
| **Batch** | Queue several files and run them one after another against one loaded model, then export the results as `.txt` or `.json`. |
| **Models** | The 72 curated aliases, filterable by alias, family, size, licence, or what is already on disk, with per-row download, measure and delete. It can also time a model on audio you choose, on your machine, for this session only. |
| **Settings** | Backend and device selection, the cache directory, the native library's version, and the compute devices this build can see. |

Three things worth knowing before you rely on it:

- **It shares the model cache with the `transcribe` CLI.** A model downloaded in one is
  there for the other, and the Models tab can delete what the CLI fetched.
- **It asks the model what it can do, instead of guessing from the name.** Loading is the
  only way the native library answers — the manifest declares no capabilities — so a model
  that is not on disk keeps every option visible and the app says so underneath the picker.
  Options are hidden only once a loaded model has said it lacks the feature.
- **It does not rank models by accuracy.** This project publishes no accuracy comparison
  between them, so the app does not pretend to have one; `Measure` times a model, and speed
  is all it tells you. See [Streaming benchmarks](docs/streaming-bench.md) for the one
  benchmark the project does have, and what it does not cover.

The tab that carries the library's limits is Streaming: the app calls the same
`StreamSession` the CLI and the library do, so the native abort documented in
[Streaming benchmarks](docs/streaming-bench.md) applies here too and is not a GUI bug.

## What it does not do

Stated plainly, so nothing is implied. Each item has its own page with the detail.

- **The library is not thread-safe.** At most one run per model at a time, across *all* of
  that model's sessions; parallel workers each need their own `Model`. This is an upstream
  0.x limitation, not one this wrapper imposes
  ([Concurrency](docs/concurrency.md#thread-safety)).
- **A stream is not yet reliable for long sessions.** Measured RTF degrades as a
  stream grows, and on the CPU backend a stream of ~88 s of audio aborts the
  process inside ggml — natively, uncatchably. Real numbers, the one knob that
  helps, and the reproduction are in
  [Streaming benchmarks](docs/streaming-bench.md).
- **Streaming cannot attribute speakers.** `transcribe_stream_params` has no `diarize`
  field in transcribe.cpp v0.3.0, so the streaming API cannot request it; we do not
  invent one ([Diarization](docs/diarization.md#not-available-streaming)).
- **No CUDA runtime ships in the packages.** The bundled binaries are CPU + Vulkan on
  Windows/Linux and Metal on macOS; an NVIDIA GPU means supplying your own CUDA build
  ([Using CUDA](docs/compute.md#using-cuda)).
- **The MIT license does not cover the models.** They come from different ecosystems,
  some non-commercial, and this project neither bundles nor curates them
  ([Model licenses](docs/models.md#model-licenses)).
- **Speaker attribution is not verified in CI.** It is covered only when the ~668 MB MOSS
  asset is fetched with `WITH_DIARIZATION_MODEL=1`. Until then, a regression that emptied
  `SpeakerSegments` would go unnoticed
  ([diarization](docs/diarization.md#what-is-verified-and-what-ci-does-not-run)).
- **There is no GPU in CI.** The tool is tested end to end on the CPU, but GPU selection
  itself is covered by policy tests, not by a real run
  ([what the CLI does not do](docs/cli.md#what-the-cli-does-not-do)).
- **The desktop app is not distributed.** It is built and its headless tests run in CI on
  all three platforms, but there is no installer, no published binary and no NuGet package
  for it — only the source in `src/TranscribeCppSharp.Ui`
  ([Desktop app](#desktop-app)).

The formats the decoder is *verified* to read are listed, with the tests that cover them,
on [Audio input](docs/audio-input.md).

## Attribution

This project is **a packaging and binding effort only** — the underlying library is not my work:

- The native library (`transcribe.cpp`) is developed and owned by the [transcribe.cpp authors](https://github.com/handy-computer/transcribe.cpp) (MIT License).
- The bundled native components (ggml, etc.) are owned by their respective authors; their MIT license texts are distributed alongside the binaries in the `TranscribeCppSharp.Native.*` packages.
- I did **not** author the native library and claim no credit for it. This repository only adds:
  - A C# interop layer (auto-generated P/Invoke bindings via `LibraryImport`).
  - A high-level C# wrapper (`IDisposable` resources, typed exceptions).
  - A command-line tool (`transcribe`) and the model manifest it resolves against.
  - Pre-built native binaries packaged for .NET consumption.

The transcribe.cpp project is an independent upstream project; bug reports about the native library itself should go to its [repository](https://github.com/handy-computer/transcribe.cpp).

## Development

```bash
# Download native libraries for your current platform
dotnet run --project tools/FetchNative

# Run unit and integration tests
./scripts/run-integration-tests.sh

# Run the desktop app (also needs FetchNative; see Desktop app)
dotnet run --project src/TranscribeCppSharp.Ui
```

Prerequisites, the opt-in diarization run, the checks that keep this documentation
honest, and building from source: [Development](docs/development.md).

## Governance

**Security**: report a vulnerability through the
[GitHub Security Advisory](https://github.com/manuc66/TranscribeCppSharp/security/advisories) feature.

**License**: this project is licensed under the **MIT License** (matching `transcribe.cpp`).
The models you load are *not* covered by it — see
[Model licenses](docs/models.md#model-licenses).

**Versioning**: the wrapper and the native runtime are versioned separately on purpose;
`TranscribeCppSharp 0.3.1` binds `transcribe.cpp` v0.3.0. See
[Versioning & compatibility](docs/architecture.md#versioning--compatibility) and
[CHANGELOG.md](CHANGELOG.md).
