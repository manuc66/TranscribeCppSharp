# Changelog

All notable changes to this project are documented in this file.

The versioning scheme is described in the [README](README.md#versioning--compatibility):
the wrapper (`TranscribeCppSharp`) follows SemVer for its own C# API, while
`TranscribeCppSharp.Interop` and `TranscribeCppSharp.Native.*` track the upstream
[transcribe.cpp](https://github.com/handy-computer/transcribe.cpp) version they bind to.

## [0.3.0] - wrapper release

Binds to **transcribe.cpp v0.2.3** (unchanged). No C# API change; this is a
packaging + tooling release.

**Packaging (breaking install behavior):**

- `TranscribeCppSharp.Interop` no longer depends on the native runtime
  packages. Previously every consumer pulled all five `Native.*` packages
  (~200 MB) regardless of platform. Add the native package for your RID
  (`dotnet add package TranscribeCppSharp.Native.linux-x64`), or the new
  `TranscribeCppSharp.Bundle` meta-package that pulls the wrapper plus every
  native runtime.
- The wrapper now depends only on the (platform-agnostic) Interop core.

**New command-line tool:**

- `TranscribeCppSharp.Cli` — a `.NET tool` exposing `transcribe`. Published
  RID-specific (SDK 10): `dotnet tool install` selects the package for your
  platform and only that RID's native binaries are embedded; an `any` fallback
  is framework-dependent. Install is a few MB, nothing is downloaded at first
  run for the native.
- Models are never bundled. `--model` accepts a file path, a curated alias
  (72 models across every family transcribe.cpp supports — Whisper, Moonshine,
  Parakeet, Canary, GigaAM, Voxtral, Qwen3-ASR, Granite-speech, MOSS and
  streaming/multitalker diarization, …), or a generic HuggingFace spec
  `<owner>/<repo>/<file.gguf>[@<revision>]`. Downloads use a pinned revision,
  are verified by sha256 (read from the HF LFS metadata) and cached.
- `--list-models` / `--model-info <alias>` show each model's license
  (non-commercial licenses are flagged); `--quant` selects a quantization.
- `tools/UpdateModelManifest` regenerates the model manifest from the
  HuggingFace API.

**Versioning:** `TranscribeCppSharp.Bundle` and `TranscribeCppSharp.Cli` follow
the wrapper's SemVer (this release / the tag); `Interop` and `Native.*` keep
tracking the upstream transcribe.cpp version.

## [0.2.0] - wrapper release

**Breaking API change** (wrapper minor bump per SemVer 0.x): the device-selection
API no longer uses integer GPU indices. Binds to **transcribe.cpp v0.2.3** (was
v0.1.3). Upstream v0.2.0 was a
deliberate pre-1.0 ABI break — see upstream
[docs/migrating-to-0.2.md](https://github.com/handy-computer/transcribe.cpp/blob/v0.2.3/docs/migrating-to-0.2.md).

- Device selection: `ModelLoadParamsBuilder.WithGpuDevice(int)` **removed**
  (obsolete error) — use `WithDevice(BackendDevice)` / `WithDevice(IntPtr)`
  with a handle from `Backends.EnumerateDevices()`. `BackendDevice` now
  carries the runtime-owned `Handle`; `Backends.GetDeviceInfo(handle)`
  resolves metadata for any handle; `Model.Device` reports the loaded
  model's device. `0` is now a selectable device, no longer the auto
  sentinel (omit `WithDevice` for automatic selection).
- New bindings: `BackendRequest.Rocm`, `Feature.Diarization`,
  `RunParamsBuilder.WithDiarize(DiarizeMode)`, `Session.RawText`,
  `Session.ReadSpeakerSegments()` / `SpeakerSegmentCount` (+ batch
  mirrors), `SegmentResult.SpeakerId`, `Transcript.RawText` /
  `Transcript.SpeakerSegments`, `SortformerStreamExtBuilder`
  (+ `StreamParamsBuilder.WithSortformerExt`).
- Regenerated `NativeMethods` from v0.2.3 (`DeviceInfo`,
  `SpeakerSegment`, `SortformerStreamExt`, `RawText`, …); native
  `sha256` hashes for all 5 RIDs in `build/native-sha256.json`.

## [0.1.0] - wrapper release

Wrapper SemVer baseline. This release binds to **transcribe.cpp v0.1.3**.

- First public packaging: `TranscribeCppSharp` (wrapper), `TranscribeCppSharp.Interop`,
  and per-RID `TranscribeCppSharp.Native.*` packages (win-x64, linux-x64, linux-arm64,
  osx-x64, osx-arm64).
- Explicit upstream attribution: the project is presented as bindings + packaging for
  transcribe.cpp, not affiliated with or endorsed by upstream; license texts of bundled
  native components ship inside the `Native.*` packages.
- Native library loading via a `DllImportResolver`: `libtranscribe` and its `libggml*`
  dependencies are resolved at runtime from the NuGet package folder or app output,
  with no `LD_LIBRARY_PATH` required.
- The wrapper's package version is decoupled from the upstream ABI version; the
  Interop/Native packages keep tracking the upstream transcribe.cpp version.

## Upstream version history (transcribe.cpp)

- **v0.2.3** — current version packaged by this project.
- **v0.1.3** — first version packaged by this project.

[0.3.0]: https://github.com/manuc66/TranscribeCppSharp/compare/v0.2.0...v0.3.0
[0.2.0]: https://github.com/manuc66/TranscribeCppSharp/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/manuc66/TranscribeCppSharp/compare/v0.1.3...v0.1.0
