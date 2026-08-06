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

## Installation

Add the main wrapper package to your project:

```bash
dotnet add package TranscribeCppSharp
```

To include the native binaries for your platform, add the corresponding runtime package:

- **Linux (x64)**: `TranscribeCppSharp.Native.linux-x64`
- **Linux (ARM64)**: `TranscribeCppSharp.Native.linux-arm64`
- **Windows (x64)**: `TranscribeCppSharp.Native.win-x64`
- **macOS (ARM64)**: `TranscribeCppSharp.Native.osx-arm64`
- **macOS (x64)**: `TranscribeCppSharp.Native.osx-x64`

*Note: For Linux Alpine (musl) or other platforms, please refer to the [Building from source](#building-from-source) section. Like [Using CUDA](#using-cuda), a custom native build is picked up automatically when placed in the app output directory.*

## Quick Start

### Basic Transcription

```csharp
using TranscribeCppSharp;

// Initialize compute backends once, before loading any model
TranscribeCppSharp.Backends.InitDefault();

// Load the model (GGUF format)
using var model = Model.Load("whisper-tiny.gguf");

// Create a transcription session
using var session = model.CreateSession();

// Input must be 16kHz mono float PCM
float[] pcm = ...; 

// Run transcription (blocking call)
var transcript = session.Run(pcm);

Console.WriteLine($"Result: {transcript.FullText}");
```

### Selecting Hardware Backend

By default, the wrapper passes `BackendAuto` and the native library selects the backend it has available. You can force a specific one:

```csharp
using var model = Model.Load("model.gguf", p => p
    .WithBackend(BackendRequest.BackendVulkan) // or BackendCuda, BackendMetal, etc.
    .WithGpuDevice(0)); // Invalid device index -> ErrInvalidArg; unavailable backend -> ErrBackend
```

Backends must be initialized once before the first `Model.Load`, otherwise the native call fails with `ErrBackend`. Use `Backends.InitDefault()` (resolves the directory next to the loaded library) or `Backends.Init(dir)` to point at a specific artifact directory.

### Batch Processing

Transcribe multiple audio buffers in parallel:

```csharp
var audios = new float[][] { audio1, audio2, audio3 };
var results = Batch.Run(session, audios);

foreach (var result in results)
{
    Console.WriteLine(result.FullText);
}
```

### Real-Time Streaming

```csharp
using var stream = session.CreateStream();
stream.Begin(); // Initialize the streaming state

// Feed audio chunks incrementally
while (isRecording)
{
    float[] chunk = GetAudioChunk();
    stream.Feed(chunk);
    
    // Read partial results
    var current = stream.CurrentText;
    Console.Write($"\r{current.FullText}");
}

// Finalize the stream to get the last bits of text
// This must be called BEFORE Dispose() if you want the final results
stream.Complete();
var final = stream.CurrentText;
```

## Features

- **Multi-Model**: Loads GGUF models for the model families supported by transcribe.cpp (Whisper, Moonshine, Parakeet, Canary, GigaAM, and others — 16 families upstream).
- **Hardware Acceleration**: The bundled runtimes include CPU, Vulkan (Windows/Linux) and Metal (macOS) backends. See [Using CUDA](#using-cuda) for NVIDIA GPUs.
- **Modern .NET**: Uses `LibraryImport` for interop and `SafeHandle` for native resource lifetime.
- **Flexible APIs**:
  - **High-Level Wrapper**: Intuitive C# API for rapid development.
  - **Low-Level Interop**: Direct access to the native C API when needed.
  - **Streaming & Batch**: Support for incremental streaming transcription and batch processing.
- **Cross-Platform**: Pre-compiled native runtimes are packaged for Windows, Linux, and macOS (x64 and ARM64). Only linux-x64 is exercised by CI.

## Using CUDA

The NuGet packages do **not** bundle a CUDA runtime (the bundled binaries are
CPU + Vulkan on Windows/Linux and Metal on macOS). The upstream releases do
include CUDA archives, but shipping and supporting CUDA builds is out of scope
for this packaging layer — so to use an NVIDIA GPU you provide your own CUDA
build of transcribe.cpp and place it next to your app; the wrapper prefers
native binaries in the app output directory over the packaged ones.

1. **Download** the upstream CUDA archive for your platform (this project is
   bound to transcribe.cpp v0.1.3):

   - Linux x64: `transcribe-native-0.1.3-linux-x86_64-cuda.tar.gz`
   - Windows x64: `transcribe-native-0.1.3-windows-x86_64-cuda.tar.gz`

   from the [transcribe.cpp v0.1.3 release](https://github.com/handy-computer/transcribe.cpp/releases/tag/v0.1.3).

2. **Extract** it and copy `libtranscribe.so` (Linux) or `transcribe.dll`
   (Windows) — plus the sibling `libggml*.so` / `ggml*.dll` files — into your
   app's output directory (where your `.dll`/`.exe` is produced).

3. **Request the CUDA backend** at load time:

   ```csharp
   using var model = Model.Load("model.gguf", p => p
       .WithBackend(BackendRequest.BackendCuda)
       .WithGpuDevice(0));
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
4.  **`Generator` (Tool)**: Ensures C# bindings stay in sync with the upstream native API by parsing Rust FFI definitions.

### Native Library Loading
A `DllImportResolver` registered in the Interop layer finds `libtranscribe` in the app output directory or the NuGet global packages folder, without requiring `LD_LIBRARY_PATH`. Its `libggml*` dependencies are loaded from the same directory by the native loader.

## Error Handling

The high-level wrapper throws `TranscribeException` when a native call fails. You can filter by `StatusCode` to handle specific errors.

```csharp
try 
{
    using var model = Model.Load("invalid.gguf");
}
catch (TranscribeException ex) when (ex.StatusCode == Status.ErrGguf)
{
    Console.WriteLine("Failed to load model: Check file path and format.");
}
```

*Note: See the `Status` enum in the `TranscribeCppSharp.Interop` namespace for the full list of error codes.*

## Thread Safety

The native library and this wrapper are **not** thread-safe by default. The relevant rules:

- **Concurrent compute is limited**: at most one `Session.Run`, `Batch.Run`, or active stream may be in flight across **all sessions of the same model** at a time. Sessions share the model's backend instances and some per-family state, so overlapping runs on the same model race (per the upstream library: corrupted decodes on CPU, command-buffer failures on Metal). This is a **known limitation of the upstream native library in 0.x**, documented in its [public header](https://github.com/handy-computer/transcribe.cpp/blob/v0.1.3/include/transcribe.h) (see "KNOWN 0.x LIMITATION — concurrent COMPUTE"), not something this wrapper imposes or can lift.
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

- **`TranscribeCppSharp`** (this wrapper) follows [Semantic Versioning (SemVer)](https://semver.org/) for its **own C# API**. Breaking API changes bump the major/minor version of the wrapper.
- **`TranscribeCppSharp.Interop`** and **`TranscribeCppSharp.Native.*`** are versioned to match the **upstream `transcribe.cpp` version** they bind to (e.g. `0.1.3` = transcribe.cpp v0.1.3). They track the ABI, not the wrapper's API.

So `TranscribeCppSharp 0.1.0` depends on `TranscribeCppSharp.Interop 0.1.3`; a later upstream release will ship as a new Interop/Native version without necessarily changing the wrapper's own version. The correspondence between a wrapper release and the upstream version it targets is recorded in [CHANGELOG.md](CHANGELOG.md).

## Attribution

This project is **a packaging and binding effort only** — the underlying library is not my work:

- The native library (`transcribe.cpp`) is developed and owned by the [transcribe.cpp authors](https://github.com/handy-computer/transcribe.cpp) (MIT License).
- The bundled native components (ggml, etc.) are owned by their respective authors; their MIT license texts are distributed alongside the binaries in the `TranscribeCppSharp.Native.*` packages.
- I did **not** author the native library and claim no credit for it. This repository only adds:
  - A C# interop layer (auto-generated P/Invoke bindings via `LibraryImport`).
  - A high-level C# wrapper (`IDisposable` resources, typed exceptions).
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

# Run the smoke test sample
dotnet run --project samples/SmokeTest -- model.gguf audio.wav
```

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
docs](https://github.com/handy-computer/transcribe.cpp/blob/v0.1.3/docs/models)
describe each supported family and where its models come from; that is the
source of truth, not this README.
