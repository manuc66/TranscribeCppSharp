# TranscribeCppSharp

[![CI](https://github.com/manuc66/TranscribeCppSharp/actions/workflows/ci.yml/badge.svg)](https://github.com/manuc66/TranscribeCppSharp/actions/workflows/ci.yml)
[![CodeQL](https://github.com/manuc66/TranscribeCppSharp/actions/workflows/github-code-scanning/codeql/badge.svg)](https://github.com/manuc66/TranscribeCppSharp/actions/workflows/github-code-scanning/codeql)
[![Code Coverage](https://codecov.io/gh/manuc66/TranscribeCppSharp/branch/main/graph/badge.svg)](https://codecov.io/gh/manuc66/TranscribeCppSharp)
[![Quality Gate Status](https://sonarcloud.io/api/project_badges/measure?project=manuc66_TranscribeCppSharp&metric=alert_status)](https://sonarcloud.io/dashboard?id=manuc66_TranscribeCppSharp)
[![CodeFactor](https://www.codefactor.io/repository/github/manuc66/transcribecppsharp/badge)](https://www.codefactor.io/repository/github/manuc66/transcribecppsharp)
[![FOSSA Status](https://app.fossa.com/api/projects/git%2Bgithub.com%2Fmanuc66%2FTranscribeCppSharp.svg?type=shield)](https://app.fossa.com/projects/git%2Bgithub.com%2Fmanuc66%2FTranscribeCppSharp?ref=badge_shield)
[![NuGet Version](https://img.shields.io/nuget/v/TranscribeCppSharp.svg)](https://www.nuget.org/packages/TranscribeCppSharp)
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

*Note: For Linux Alpine (musl) or other platforms, please refer to the [Building from source](#building-from-source) section.*

## Quick Start

### Basic Transcription

```csharp
using TranscribeCppSharp;

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
    .WithGpuDevice(0)); // Throws ErrBackend if device index is invalid or unavailable
```

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
stream.Finalize();
var final = stream.CurrentText;
```

## Features

- **Multi-Model**: Loads GGUF models for the model families supported by transcribe.cpp (Whisper, Moonshine, Parakeet, Voxtral, and others).
- **Hardware Acceleration**: The bundled runtimes include CPU, Vulkan (Windows/Linux) and Metal (macOS) backends. The API exposes CUDA as a `BackendRequest` value, but the shipped binaries do not bundle a CUDA runtime; check `BackendAvailable(BackendRequest)` at runtime to see what a given build provides.
- **Modern .NET**: Uses `LibraryImport` for interop and `SafeHandle` for native resource lifetime.
- **Flexible APIs**:
  - **High-Level Wrapper**: Intuitive C# API for rapid development.
  - **Low-Level Interop**: Direct access to the native C API when needed.
  - **Streaming & Batch**: Support for incremental streaming transcription and batch processing.
- **Cross-Platform**: Pre-compiled native runtimes are packaged for Windows, Linux, and macOS (x64 and ARM64). Only linux-x64 is exercised by CI.

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

- **`Model`**: believed **thread-safe** — you can create multiple `Session` objects from a single `Model` instance across different threads. This is not covered by concurrency tests yet.
- **`Session`**: **Not thread-safe**. A session maintains internal state (KV cache) for transcription. For concurrent processing, use multiple sessions or synchronize access.
- **`Batch`**: **Not thread-safe**. Calls into the provided session internally. Use separate sessions for concurrent batch processing.
- **`StreamSession`**: **Not thread-safe**. It is a view over a `Session` and shares its state.

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

The transcribe.cpp project is an independent upstream project with its own maintainers and governance; this package is **not affiliated with or endorsed by** them. Please direct bug reports about the native library itself to the upstream repository.

## Development

### Prerequisites

- .NET 10.0 or later.
- Native libraries (can be fetched using the provided script).

### Building and Testing

```bash
# Download native libraries for your current platform
./fetch-native.sh

# Run unit and integration tests
./run-integration-tests.sh

# Run the smoke test sample
dotnet run --project samples/SmokeTest -- model.gguf audio.wav
```

### Building from source

While we provide pre-compiled binaries for major platforms, you may need to build from source if:
- You are using **Alpine Linux** (which uses `musl` instead of `glibc`, making standard Linux binaries incompatible).
- You need to support a non-standard architecture or custom OS.
- You want to enable specific hardware optimizations not included in the default build.

**Steps:**
1.  Clone [transcribe.cpp](https://github.com/handy-computer/transcribe.cpp).
2.  Build the native library using `cmake` (ensure `BUILD_SHARED_LIBS=ON`).
3.  Copy the resulting `libtranscribe.so` (or `.dll`/`.dylib`) to your application's output directory or set `LD_LIBRARY_PATH`.

## Governance

### Security
To report a security vulnerability, please use the [GitHub Security Advisory](https://github.com/manuc66/TranscribeCppSharp/security/advisories) feature.

### License
This project is licensed under the **MIT License** (matching `transcribe.cpp`).
