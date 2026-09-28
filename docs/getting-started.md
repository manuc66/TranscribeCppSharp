---
title: Getting started
nav_order: 2
---

# Getting started

Install the wrapper, point it at a GGUF model and a WAV file, and read the transcript.
Everything else in this documentation is a refinement of that.

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

*Note: For Linux Alpine (musl) or other platforms, please refer to [Building from source](development.md#building-from-source). Like [Using CUDA](compute.md#using-cuda), a custom native build is picked up automatically when placed in the app output directory.*

The wrapper resolves `libtranscribe` automatically in plain `dotnet run` scenarios (no `<RuntimeIdentifier>` needed): it searches the app output directory — including the `runtimes/<rid>/native/` layout where .NET places runtime-package binaries — and the NuGet global packages folder. If the native library is still missing at runtime (e.g. you forgot the runtime package), the wrapper throws a `DllNotFoundException` that lists the exact package to add for your platform (e.g. `dotnet add package TranscribeCppSharp.Native.linux-x64`) and the paths it searched. It does not silently produce a misleading error.

The search order and the resolver itself are described under [Native library loading](architecture.md#native-library-loading).

## Load a model and transcribe

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

The audio must be 16 kHz mono 16-bit WAV for that call. Every other format needs
`ffmpeg` on `PATH` — see [Audio input](audio-input.md).

`BackendRequest.BackendCpu` is explicit here so the example is deterministic. Omit it and
the default `AUTO` policy runs on the GPU whenever one initializes — see
[Compute](compute.md).

## Batch processing

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

## Real-time streaming

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

Streaming cannot request speaker attribution — the reason is in
[Speaker diarization](diarization.md#not-available-streaming).

## Model capabilities

A model is asked what it can do rather than assumed: `model.Supports(feature)` and
`model.GetCapabilities()`. The capability worth checking before a run is
`Feature.FeatureDiarization` — see
[Speaker diarization](diarization.md) — and the snippet is on
[Model capabilities](models.md#model-capabilities).

## The command-line tool

The `transcribe` tool does the same thing with no code, no project file and no manual
model handling: `dotnet tool install -g TranscribeCppSharp.Cli`. It is covered
separately, because its options, its export formats and its limits are its own —
[Command-line tool](cli.md).

## Run the samples

```bash
# Run the smoke test sample
dotnet run --project samples/SmokeTest -- model.gguf audio.wav
```

See [Development](development.md) for the prerequisites and the full test command.
