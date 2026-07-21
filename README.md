# TranscribeCppSharp

[![CI](https://github.com/manuc66/TranscribeCppSharp/actions/workflows/ci.yml/badge.svg)](https://github.com/manuc66/TranscribeCppSharp/actions/workflows/ci.yml)
[![NuGet Version](https://img.shields.io/nuget/v/TranscribeCppSharp.svg)](https://www.nuget.org/packages/TranscribeCppSharp)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

.NET bindings for [transcribe.cpp](https://github.com/handy-computer/transcribe.cpp), providing high-performance, cross-platform audio transcription and speech-to-text capabilities.

## Features

- **Multi-Model Architecture**: Seamless support for Whisper, Moonshine, Parakeet, and Voxtral.
- **Hardware Acceleration**: Built-in support for CPU, CUDA, Vulkan, Metal, and CoreML backends.
- **Modern .NET Core**: Leverages `LibraryImport` for high-performance interop and `SafeHandle` for reliable memory management.
- **Flexible APIs**:
  - **High-Level Wrapper**: Intuitive C# API for rapid development.
  - **Low-Level Interop**: Direct access to the native C API when needed.
  - **Streaming & Batch**: Support for real-time transcription and efficient batch processing.
- **Cross-Platform**: Pre-compiled native runtimes for Windows, Linux, and macOS (x64 and ARM64).

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

// Load the model (supports .gguf and .bin formats)
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

By default, the library detects and uses the best available backend. You can force a specific one:

```csharp
using var model = Model.Load("model.gguf", p => p
    .WithBackend(BackendRequest.Cuda) // or Vulkan, Metal, etc.
    .WithGpuDevice(0)); // Throws ErrBackend if device index is invalid or unavailable
```

### Batch Processing

Transcribe multiple audio buffers efficiently in parallel:

```csharp
var audios = new float[][] { audio1, audio2, audio3 };
// Thread-safe if the session is not used elsewhere simultaneously
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

## Concurrency Model

All transcription calls (`Session.Run`, `Batch.Run`, etc.) are **CPU-bound and blocking**. This is a conscious design choice to avoid the overhead of "fake" async-over-sync wrappers.

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

The project is divided into several layers to balance raw performance with ease of use:

1.  **`TranscribeCppSharp.Native.*` (Runtimes)**: Platform-specific packages containing the pre-compiled native `libtranscribe` binaries.
2.  **`TranscribeCppSharp.Interop` (Low-level)**: Auto-generated P/Invoke declarations using `LibraryImport` for minimal overhead.
3.  **`TranscribeCppSharp` (High-level)**: Idiomatic C# abstraction layer providing `IDisposable` resources and typed exceptions.
4.  **`Generator` (Tool)**: Ensures C# bindings stay in sync with the upstream native API by parsing Rust FFI definitions.

### Native Library Loading
The library uses standard .NET runtime identifiers (RID) to resolve the correct native binary. At runtime, the `TranscribeCppSharp.Native.*` packages place binaries in the `runtimes/` folder, which is automatically searched by the .NET host. For custom loading logic, the Interop layer is compatible with `NativeLibrary.SetDllImportResolver`.

## Error Handling

The high-level wrapper throws `TranscribeException` when a native call fails. You can filter by `StatusCode` to handle specific errors.

```csharp
try 
{
    using var model = Model.Load("invalid.gguf");
}
catch (TranscribeException ex) when (ex.StatusCode == Status.ErrModelLoad)
{
    Console.WriteLine("Failed to load model: Check file path and format.");
}
```

*Note: See the `Status` enum in the `TranscribeCppSharp.Interop` namespace for the full list of error codes.*

## Enterprise Readiness

### Thread-Safety
- **`Model`**: **Thread-safe**. You can create multiple `Session` objects from a single `Model` instance across different threads.
- **`Session`**: **Not thread-safe**. A session maintains internal state (KV cache) for transcription. For concurrent processing, use multiple sessions or synchronize access.
- **`Batch`**: **Thread-safe wrapper**. It uses the provided session exclusively for the duration of the call.
- **`StreamSession`**: **Not thread-safe**. It is a view over a `Session` and shares its state.

### Hardware Requirements
| Model Size | RAM (Quantized) | VRAM (Quantized) |
|---|---|---|
| Tiny | ~150 MB | ~100 MB |
| Base | ~250 MB | ~200 MB |
| Small | ~700 MB | ~600 MB |
| Medium | ~2.0 GB | ~1.5 GB |
| Large-v3 | ~4.5 GB | ~3.5 GB |

### Versioning & Compatibility
This project follows [Semantic Versioning (SemVer)](https://semver.org/).

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
