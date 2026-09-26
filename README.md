# TranscribeCppSharp

[![CI](https://github.com/manuc66/TranscribeCppSharp/actions/workflows/ci.yml/badge.svg)](https://github.com/manuc66/TranscribeCppSharp/actions/workflows/ci.yml)
[![CodeQL](https://github.com/manuc66/TranscribeCppSharp/actions/workflows/github-code-scanning/codeql/badge.svg)](https://github.com/manuc66/TranscribeCppSharp/actions/workflows/github-code-scanning/codeql)
[![Code Coverage](https://codecov.io/gh/manuc66/TranscribeCppSharp/branch/main/graph/badge.svg)](https://codecov.io/gh/manuc66/TranscribeCppSharp)
[![Quality Gate Status](https://sonarcloud.io/api/project_badges/measure?project=manuc66_TranscribeCppSharp&metric=alert_status)](https://sonarcloud.io/dashboard?id=manuc66_TranscribeCppSharp)
[![CodeFactor](https://www.codefactor.io/repository/github/manuc66/transcribecppsharp/badge)](https://www.codefactor.io/repository/github/manuc66/transcribecppsharp)
[![FOSSA Status](https://app.fossa.com/api/projects/git%2Bgithub.com%2Fmanuc66%2FTranscribeCppSharp.svg?type=shield)](https://app.fossa.com/projects/git%2Bgithub.com%2Fmanuc66%2FTranscribeCppSharp?ref=badge_shield)
[![NuGet Version](https://img.shields.io/nuget/v/TranscribeCppSharp.svg)](https://www.nuget.org/packages/TranscribeCppSharp)
[![NuGet Downloads](https://img.shields.io/nuget/dt/TranscribeCppSharp.svg)](https://www.nuget.org/packages/TranscribeCppSharp)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

.NET bindings for [transcribe.cpp](https://github.com/handy-computer/transcribe.cpp): load GGUF speech-to-text models and transcribe audio (16 kHz mono float PCM) from C#.

Two ways to use it:

- **A command-line tool** — `dotnet tool install -g TranscribeCppSharp.Cli` gives you `transcribe`, which downloads a curated model on first use and prints or exports the transcript. See [Command-line tool](#command-line-tool).
- **A .NET library** — the `TranscribeCppSharp` wrapper (plus the native runtime package for your platform) for C# code. See [Installation](#installation) and [Quick Start](#quick-start).

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

Native runtime packages:

- **Linux (x64)**: `TranscribeCppSharp.Native.linux-x64`
- **Linux (ARM64)**: `TranscribeCppSharp.Native.linux-arm64`
- **Windows (x64)**: `TranscribeCppSharp.Native.win-x64`
- **macOS (ARM64)**: `TranscribeCppSharp.Native.osx-arm64`
- **macOS (x64)**: `TranscribeCppSharp.Native.osx-x64`

*Note: For Linux Alpine (musl) or other platforms, please refer to the [Building from source](#building-from-source) section. Like [Using CUDA](#using-cuda), a custom native build is picked up automatically when placed in the app output directory.*

The wrapper resolves `libtranscribe` automatically in plain `dotnet run` scenarios (no `<RuntimeIdentifier>` needed): it searches the app output directory — including the `runtimes/<rid>/native/` layout where .NET places runtime-package binaries — and the NuGet global packages folder. If the native library is still missing at runtime (e.g. you forgot the runtime package), the wrapper throws a `DllNotFoundException` that lists the exact package to add for your platform (e.g. `dotnet add package TranscribeCppSharp.Native.linux-x64`) and the paths it searched. It does not silently produce a misleading error.

## Command-line tool

`transcribe` is the fastest way to try this project: it needs no code, no
project file, and no manual model handling.

```bash
dotnet tool install -g TranscribeCppSharp.Cli
```

The tool is published per platform (win-x64, linux-x64, linux-arm64, osx-x64,
osx-arm64), and the native runtime for your platform is embedded in the package,
so nothing native is downloaded at first run. It needs the .NET 10 runtime.

### Transcribe a file

```bash
transcribe jfk.wav --model whisper-tiny
```

The model is fetched from HuggingFace on first use (42 MB for that alias),
verified by sha256 and cached. Aliases carry a pinned revision, so later runs
reuse the cache and need no network. Console output below, with the
`ggml_*`/`load_backend` native log lines and the two timing lines (per-window
progress and the real-time factor, which depend on your hardware) omitted.
Decimal separators follow your locale:

```text
audio : 176 000 samples = 11,0s @ 16kHz mono
model : whisper/whisper-tiny
diarization supported: False
window: 300s (model max audio 0s)

=== window 1 @ 00:00.0 (176 000 samples) ===
  lang   :    aborted: False  truncated: False
  full   : And so my fellow Americans ask not what your country can do for you, ask what you can do for your country.

=== summary ===
  windows: 1
```

### Choose a model

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
alias                                    quant    license              size
breeze-asr-25                            Q5_K_M   apache-2.0         1106 MB
canary-1b                                Q5_K_M   cc-by-nc-4.0 !      798 MB
parakeet-tdt-0.6b-v3                     Q5_K_M   cc-by-4.0           523 MB
whisper-large-v3-turbo                   Q5_K_M   apache-2.0          590 MB
…
! = non-commercial license; verify before any commercial use.

$ transcribe --model-info whisper-tiny
whisper-tiny
  repo       : handy-computer/whisper-tiny-gguf
  revision   : 2678cc66038359b97c8e6fd6454c56fc9006d571
  quant      : Q5_K_M
  file       : whisper-tiny-Q5_K_M.gguf
  size       : 42 MB
  license    : apache-2.0
  license url: https://huggingface.co/handy-computer/whisper-tiny-gguf
```

Any GGUF that transcribe.cpp supports works through the generic
`<owner>/<repo>/<file.gguf>[@<revision>]` form, so the alias list is a
convenience, not a limit. Two differences from an alias: the license is unknown
(only the model card is linked) and, unless you pin `@<revision>`, the current
revision is looked up on HuggingFace on **every** run — the cache is still used,
but the run is not offline. Models are cached under
`$XDG_CACHE_HOME/TranscribeCppSharp/models` (`%LOCALAPPDATA%` on Windows,
`~/Library/Caches` on macOS); weights are never bundled with the tool. See
[Model licenses](#model-licenses) before using a model commercially.

### Export the transcript

`--out` writes the transcript to a file; `--format` picks the shape:

| `--format` | Output |
| --- | --- |
| `plain` (default) | header comments + timestamped lines |
| `vtt` | WebVTT, with speaker cues |
| `json` | `{"language", "text", "segments":[{start,end,speaker,text}]}` |

```bash
transcribe jfk.wav --model whisper-tiny --out jfk.vtt --format vtt
```

```vtt
WEBVTT
NOTE source: /audio/jfk.wav
NOTE audio 00:00:11 model whisper/whisper-tiny lang en diarize on window 300s generated 2026-01-01 09:00:00

00:00:00.000 --> 00:00:10.500
<v Speaker 0>And so my fellow Americans ask not what your country can do for you, ask what you can do for your country.</v>

NOTE done 2026-01-01 09:00:00
```

Real output, with the input path and timestamps replaced by placeholders. The
`NOTE source` line records the absolute path of the input file, as does the
`# source` header of the `plain` format.

`--format json` writes the language, the full text and the segments, with times
in seconds:

```json
{
  "language": "en",
  "text": "And so my fellow Americans ask not what your country can do for you, ask what you can do for your country.",
  "segments": [
    {"start":0,"end":10.5,"speaker":0,"text":"And so my fellow Americans ask not what your country can do for you, ask what you can do for your country."}
  ]
}
```

### Long audio and speaker diarization

Audio longer than one window is split with a 1 s overlap and deduplicated;
`--chunk` sets the window size in seconds (default 300, lowered automatically if
the model reports a smaller maximum).

Diarization is **on by default** and the default model is `moss-transcribe-diarize`
(667 MB), which attributes each segment to a speaker. Use `--no-diarize` to turn
it off. Speaker attribution only happens with a diarization-capable model: on
other models (Whisper, Parakeet, …) the native library prints a warning and
every segment is reported as `Speaker 0`.

### Audio input

16 kHz mono 16-bit WAV is read directly. Any other format (ogg, mp3, m4a, …) is
decoded by shelling out to `ffmpeg`, which must be installed and on `PATH`.
Convert it yourself if you prefer:

```bash
ffmpeg -i input.mp3 -ar 16000 -ac 1 output.wav
```

### All options

```text
$ transcribe --help
Transcribe audio with a speech-to-text model. Supports every model family
that transcribe.cpp supports (Whisper, Moonshine, Parakeet, Canary, GigaAM,
Voxtral, Qwen3-ASR, MOSS diarization, …).

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
  --model-info <alias>  show details (revision, size, license) for one alias
  --lang <code>  language code for the decoder (default: en)
  --chunk <sec>  max per-transcription window in seconds (default: 300);
                 long audio is split with 1 s overlap and deduplicated
  --no-diarize   disable speaker diarization
  --out <file>   write the transcript to a file; format controlled
                 by --format (plain/vtt/json)
  --format <fmt> transcript file format: plain (default, timestamped
                 lines), vtt (WebVTT, speaker cues) or json
                 (whisper-style segments)
  --help         show this help
```

### What the CLI does not do

Stated plainly, so nothing is implied:

- **CPU backend only.** The tool always requests `BackendRequest.BackendCpu`;
  there is no `--backend` flag. For Vulkan/Metal/CUDA, use the library API
  ([Using CUDA](#using-cuda), [Quick Start](#quick-start)).
- **One file per run.** No batch or directory input.
- **No interactive/streaming mode.** Each window is transcribed to completion;
  see [Real-Time Streaming](#real-time-streaming) for the incremental API.
- **Blocking.** Transcription runs on the calling thread, as in the native
  library ([Concurrency Model](#concurrency-model)).
- **Not covered by CI.** The CI pipeline builds and packs the tool but never
  executes it; the automated tests exercise the library, not `transcribe`.

## Quick Start

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

## Features

- **Command-Line Tool**: `transcribe` transcribes a file with 72 curated models
  across every family transcribe.cpp supports, with speaker diarization and
  plain/WebVTT/JSON export. See [Command-line tool](#command-line-tool).
- **Multi-Model**: Loads GGUF models for the model families supported by transcribe.cpp (Whisper, Moonshine, Parakeet, Canary, GigaAM, and others — 16 families upstream).
- **Hardware Acceleration**: The bundled runtimes include CPU, Vulkan (Windows/Linux) and Metal (macOS) backends. See [Using CUDA](#using-cuda) for NVIDIA GPUs.
- **Modern .NET**: Uses `LibraryImport` for interop and `SafeHandle` for native resource lifetime.
- **Flexible APIs**:
  - **High-Level Wrapper**: Intuitive C# API for rapid development.
  - **Low-Level Interop**: Direct access to the native C API when needed.
  - **Streaming & Batch**: Support for incremental streaming transcription and batch processing.
- **Cross-Platform**: Pre-compiled native runtimes are packaged for Windows, Linux, and macOS (x64 and ARM64). Build and test run on Linux, macOS and Windows in CI.

## Using CUDA

The NuGet packages do **not** bundle a CUDA runtime (the bundled binaries are
CPU + Vulkan on Windows/Linux and Metal on macOS). The upstream releases do
include CUDA archives, but shipping and supporting CUDA builds is out of scope
for this packaging layer — so to use an NVIDIA GPU you provide your own CUDA
build of transcribe.cpp and place it next to your app; the wrapper prefers
native binaries in the app output directory over the packaged ones.

1. **Download** the upstream CUDA archive for your platform (this project is
   bound to transcribe.cpp v0.2.3):

   - Linux x64: `transcribe-native-0.2.3-linux-x86_64-cuda.tar.gz`
   - Windows x64: `transcribe-native-0.2.3-windows-x86_64-cuda.tar.gz`

   from the [transcribe.cpp v0.2.3 release](https://github.com/handy-computer/transcribe.cpp/releases/tag/v0.2.3).

2. **Extract** it and copy `libtranscribe.so` (Linux) or `transcribe.dll`
   (Windows) — plus the sibling `libggml*.so` / `ggml*.dll` files — into your
   app's output directory (where your `.dll`/`.exe` is produced).

3. **Request the CUDA backend** at load time:

   ```csharp
   using var model = Model.Load("model.gguf", p => p
       .WithBackend(BackendRequest.BackendCuda));
   // Exact device: enumerate and pass the handle (omit for automatic selection)
   var cuda = Backends.EnumerateDevices().First(d => d.Kind == "cuda");
   using var modelOnGpu = Model.Load("model.gguf", p => p.WithDevice(cuda));
   ```

   You can verify CUDA is actually available in the current build with
   `BackendAvailable(BackendRequest.BackendCuda)`. If no CUDA build is
   installed, that returns `false` and a `BackendCuda` request will fail with
   `ErrBackend`.

## Concurrency Model

All transcription calls (`Session.Run`, `Batch.Run`, etc.) are **blocking**. This mirrors the native library, whose C API is fully synchronous (no async entry points); the wrapper does not add a "fake" async-over-sync layer on top.

### Recommended Patterns

1.  **Desktop/CLI Apps**: Run transcription on a background thread using `Task.Run()` to keep the UI responsive.
2.  **Web APIs (ASP.NET Core)**: Use a pool of `Session` objects combined with a `SemaphoreSlim` to limit concurrent native calls and prevent thread pool starvation.

```csharp
// Example: Pooling sessions in a service
private readonly SemaphoreSlim _semaphore = new(Environment.ProcessorCount);
public async Task<string> TranscribeAsync(float[] pcm)
{
    await _semaphore.WaitAsync();
    try {
        return await Task.Run(() => _session.Run(pcm).FullText);
    } finally {
        _semaphore.Release();
    }
}
```

## Architecture

The project is divided into several layers, each with a distinct responsibility:

1.  **`TranscribeCppSharp.Native.*` (Runtimes)**: Platform-specific packages containing the pre-compiled native `libtranscribe` binaries.
2.  **`TranscribeCppSharp.Interop` (Low-level)**: Auto-generated P/Invoke declarations using `LibraryImport`.
3.  **`TranscribeCppSharp` (High-level)**: Idiomatic C# abstraction layer providing `IDisposable` resources and typed exceptions.
4.  **`TranscribeCppSharp.Cli` (Command-line tool)**: The `transcribe` .NET tool, a consumer of the high-level wrapper. It adds model resolution/download/caching, windowing and transcript export on top of it.
5.  **`Generator` (Tool)**: Ensures C# bindings stay in sync with the upstream native API by parsing Rust FFI definitions.

### Native Library Loading
A `DllImportResolver` registered in the Interop layer finds `libtranscribe` in the app output directory (including the `runtimes/<rid>/native/` layout), the NuGet global packages folder, or lets the runtime's default resolution (`.deps.json` runtime targets) handle it — without requiring `LD_LIBRARY_PATH`. Its `libggml*` dependencies are loaded from the same directory by the native loader.

## Error Handling

The high-level wrapper throws `TranscribeException` when a native call fails. You can filter by `StatusCode` to handle specific errors.

*Note: See the `Status` enum in the `TranscribeCppSharp.Interop` namespace for the full list of error codes.*

## Model Capabilities

Query what a loaded model supports:

<!-- @readme model-capabilities -->
```csharp
Backends.InitDefault(); // optional: automatic in Model.Load, but explicit is clearer
var modelPath = TestConfig.ModelPath; // your GGUF model file, e.g. "test-models/ggml-tiny.bin"
using var model = Model.Load(modelPath, p => p.WithBackend(BackendRequest.BackendCpu));
var supportsPnc = model.Supports(Feature.FeaturePnc);
var caps = model.GetCapabilities();
```
<!-- @end model-capabilities -->

## Thread Safety

The native library and this wrapper are **not** thread-safe by default. The relevant rules:

- **Concurrent compute is limited**: at most one `Session.Run`, `Batch.Run`, or active stream may be in flight across **all sessions of the same model** at a time. Sessions share the model's backend instances and some per-family state, so overlapping runs on the same model race (per the upstream library: corrupted decodes on CPU, command-buffer failures on Metal). This is a **known limitation of the upstream native library in 0.x**, documented in its [public header](https://github.com/handy-computer/transcribe.cpp/blob/v0.2.3/include/transcribe.h) (see "KNOWN 0.x LIMITATION — concurrent COMPUTE"), not something this wrapper imposes or can lift.
  - For **parallel transcription**, load **one model per worker** (each worker gets its own `Model`, hence its own backend instances).
  - **Serialized** use of many sessions on one model (e.g. a session pool behind a mutex) is fully supported.
- **`Model`**: believed **thread-safe** for creating sessions — you can create multiple `Session` objects from a single `Model` instance across different threads, as long as their runs do not overlap (see the concurrent-compute limit above). Not covered by concurrency tests yet.
- **`Session`**: **Not thread-safe**. A session maintains internal state (KV cache) for transcription. Do not run two operations on the same session concurrently; serialize them or use separate sessions.
- **`Batch`**: **Not thread-safe**. Calls into the provided session internally. Use separate sessions for concurrent batch processing.
- **`StreamSession`**: **Not thread-safe**. It is a view over a `Session` and shares its state.

> **Dispose discipline**: dispose explicitly (`using`/`Dispose()`) — do not rely on the GC finalizer for cleanup. The native contract requires the model to outlive its sessions, and while a `Session` keeps its parent `Model` alive for the session's lifetime, the **order in which finalizers run during GC-only collection is not guaranteed**. Dispose the `StreamSession`/`Session` before their `Model` (the `using var model; using var session;` declaration order does this). This is consistent with the upstream requirement that `transcribe_model_free` be called only after all derived contexts are freed.

> Memory and disk usage depend on the model file, quantization, and backend you use.
> These are not documented here; refer to the model documentation and
> [transcribe.cpp](https://github.com/handy-computer/transcribe.cpp) for accurate numbers.

### Versioning & Compatibility

Two version numbers are in play, decoupled on purpose:

- **`TranscribeCppSharp`** (this wrapper) follows [Semantic Versioning (SemVer)](https://semver.org/) for its **own C# API**. Breaking API changes bump the major/minor version of the wrapper. The user-facing packages **`TranscribeCppSharp.Bundle`** (meta-package) and **`TranscribeCppSharp.Cli`** (the `transcribe` tool) follow the wrapper's version too.
- **`TranscribeCppSharp.Interop`** and the **`TranscribeCppSharp.Native.*`** runtime packages are versioned to match the **upstream `transcribe.cpp` version** they bind to (e.g. `0.2.3` = transcribe.cpp v0.2.3). They track the ABI, not the wrapper's API.

So `TranscribeCppSharp 0.2.0` depends on `TranscribeCppSharp.Interop 0.2.3`; `TranscribeCppSharp.Bundle 0.3.0` pulls a wrapper and the native runtime packages of the matching upstream version. A later upstream release will ship as a new Interop/Native version without necessarily changing the wrapper's own version. The correspondence between a wrapper release and the upstream version it targets is recorded in [CHANGELOG.md](CHANGELOG.md).

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

### Prerequisites

- .NET 10.0 or later.
- Native libraries (can be fetched using the provided script).

### Building and Testing

```bash
# Download native libraries for your current platform
dotnet run --project tools/FetchNative

# Run unit and integration tests
./scripts/run-integration-tests.sh

# Opt-in: also fetch the MOSS diarization model (~617 MB) and run the
# speaker-attribution test
WITH_DIARIZATION_MODEL=1 ./scripts/run-integration-tests.sh

# Run the smoke test sample
dotnet run --project samples/SmokeTest -- model.gguf audio.wav

# Run the CLI from source (see "Command-line tool" for the installed tool).
# WAV is read directly; other formats (ogg, mp3, …) are decoded with ffmpeg.
dotnet run --project src/TranscribeCppSharp.Cli -- audio.ogg
```

Both need `tools/FetchNative` to have run first, so the native libraries are in
the output directory.

### Building from source

The `Native.*` packages redistribute exactly what upstream transcribe.cpp publishes in its releases — nothing more. This project is a packaging/binding layer, not a binary provider: it does not compile musl, CUDA, or other variant builds. If a variant you need is not in the upstream release, building it yourself is on you.

You need to build from source when:
- You are using **Alpine Linux** (which uses `musl` instead of `glibc`, making the pre-built Linux binaries incompatible). Upstream transcribe.cpp does not ship musl builds, so there is no `Native.*` package to install for this case.
- You need to support a non-standard architecture or custom OS.
- You want to enable specific hardware optimizations not included in the default build.

**Steps:**
1.  Clone [transcribe.cpp](https://github.com/handy-computer/transcribe.cpp).
2.  Build the native library using `cmake` (ensure `BUILD_SHARED_LIBS=ON`). On Alpine, build inside the distro so the resulting library links against `musl`.
3.  Copy the resulting `libtranscribe.so` (or `.dll`/`.dylib`) — **and the sibling `libggml*.so` files it loads** — into your application's output directory. As with [Using CUDA](#using-cuda), the wrapper prefers native binaries in the app output directory over the packaged ones, so no `LD_LIBRARY_PATH` is needed. The C# interop contract is unchanged: only the native binaries differ, not the P/Invoke signatures.

## Governance

### Security
To report a security vulnerability, please use the [GitHub Security Advisory](https://github.com/manuc66/TranscribeCppSharp/security/advisories) feature.

### License
This project is licensed under the **MIT License** (matching `transcribe.cpp`).

### Model licenses

The MIT license covers this wrapper and the bundled native library, **not the
models you load with it**. GGUF models come from different ecosystems with
different licenses — some are permissive (MIT, Apache-2.0), some are
non-commercial (e.g. CC-BY-NC-4.0 for some Parakeet/Canary variants). This
project does not bundle or redistribute models, and it does not verify or
curate their licenses.

Before using a model in a commercial product, check the license on the page
you download it from (typically Hugging Face). The [upstream transcribe.cpp
docs](https://github.com/handy-computer/transcribe.cpp/blob/v0.2.3/docs/models)
describe each supported family and where its models come from; that is the
source of truth, not this README.
