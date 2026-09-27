# Changelog

All notable changes to this project are documented in this file.

The versioning scheme is described in the [README](README.md#versioning--compatibility):
the wrapper (`TranscribeCppSharp`) follows SemVer for its own C# API, while
`TranscribeCppSharp.Interop` and `TranscribeCppSharp.Native.*` track the upstream
[transcribe.cpp](https://github.com/handy-computer/transcribe.cpp) version they bind to.

## Unreleased

### Audio loading: peak memory no longer scales with the audio twice over

Two allocations in the audio path each held a second full-size copy of the
audio alongside the result, and both are on the way in for every transcription.

Fixed:

- The ffmpeg fallback fed the decoded PCM through a pipe into a `MemoryStream`,
  then converted it to `float[]`. A pipe carries no length, so the whole stream
  had to be buffered before its size was known, and the `MemoryStream` held up
  to three full-size copies at once: its growth buffer, the `ToArray()` copy,
  and the `float[]` being filled next to both. Measured peak RSS of the CLI for
  30 minutes of audio: **610 MiB**, against 299 MiB for a WAV read directly.
  ffmpeg now writes to a temporary file, whose length is known before
  allocating, so the `float[]` is the only large allocation — the same input
  now peaks at **274 MiB**, within 5 MiB of the direct reader. The temp file is
  always removed, and a temp directory that cannot be written now reports one
  line like every other load failure instead of a .NET stack trace.
- `PcmExtensions.ReadWavToPcm` read the entire data chunk into a `byte[]` and
  only then converted it, holding the audio twice: once as 16-bit integers, once
  as floats. For 30 minutes that was **164 MiB allocated for a 109 MiB result**
  (1.50x, all on the large object heap); it is now **109 MiB for 109 MiB**
  (1.00x), because the reader converts 64 KiB at a time.
- The CLI's window loop copied every window into a fresh `float[]` before
  calling `Session.Run`, which already takes a `ReadOnlySpan<float>`. A 300 s
  window is 19 MB, so each window was a large object heap allocation that lived
  only for the call. Peak RSS on 451 s of audio: 516 -> 498 MiB.
- `StreamSession.Feed` rented a 48-byte scratch struct from `ArrayPool` and
  pinned it with a `GCHandle` on every call, while `Complete()` and
  `GetCurrentText()` used `stackalloc` for the same struct. Now consistent
  (94 ns -> 12 ns per feed). This one is a consistency and allocation fix, not a
  performance claim: at 100 ms chunks it is 0.0008% of a second of audio.
- A cancellable `Run` (or `Batch.Run`) installed its token as
  `SetAbortCallback(() => ct.IsCancellationRequested)`, allocating a closure
  over the token, a second delegate to wrap it, and a third for the matching
  `ClearAbortCallback` — on every call. The token is now stored and a single
  interop delegate, created once per session, reads whichever abort source is
  current. Measured **89 bytes/call -> 0** with the delegates forced to escape.
  This closes the long-standing `FINDINGS_OPEN.md` §4.3.

Also cleaned up, on the way:

- The ffmpeg invocation left a `RedirectStandardOutput = true` behind when its
  output moved from a pipe to a file: .NET created the pipe and nothing drained
  it. It was dead code rather than a live hang — with that command line ffmpeg
  writes 0 bytes to stdout — but leaving it would have reintroduced a hang that
  no test catches the moment a future change made ffmpeg write there. ffmpeg is
  now also run with `-nostdin`, so it cannot consume the user's keystrokes or
  block on an input stream nobody feeds.
- `Session.GetLimits` and `Model.GetCapabilities` allocated their native struct
  with `Marshal.AllocHGlobal` while every other struct-shaped call goes through
  `StackAllocHelper`. Now consistent; both structs (56 and 32 bytes) use the
  stack path. A consistency change, not a performance one.

Corrected:

- The XML doc on `Session.Run` said it returns a `Transcript` with "FullText
  and DetectedLanguage eagerly loaded" and told the caller to call
  `ReadSegments()`, `ReadWords()`, `ReadTokens()` for the rest. `ReadResults()`
  has always populated segments, words, tokens and speaker segments
  unconditionally, so the doc described an opt-in API that did not exist and
  invited callers to re-read rows the returned `Transcript` already holds. The
  doc now says what happens. The eager loading itself is unchanged and intended:
  it costs 2.0 µs, against seconds of inference.
- The README did not mention that the ffmpeg fallback now needs a writable
  temporary directory.

Added:

- Tests for the paths these changes opened up: a WAV payload spanning several
  conversion chunks (both sides of the boundary, and the stereo downmix stride
  across one), a truncated data chunk, the ffmpeg path compared sample-for-sample
  against the WAV reader over 1.28 MB of decoded audio, resample plus stereo
  downmix, temp-file cleanup, the new `StackAllocHelper` overload, and the abort
  source (a token and a user callback staying distinguishable, and switching
  between them without allocating).

### Command-line tool: first-run robustness and test coverage

`transcribe` is what most people run first, and the paths a new user hits
before any model is loaded were untested. They are now covered, and five of
them no longer end in a crash, a hang or a needless download.

Fixed:

- A file that is not audio (a text file named `.wav`, a truncated file) ended
  the process with a .NET stack trace and exit code 134. It now produces one
  actionable line.
- `--out` into a directory that does not exist did the same. The parent
  directory is now checked while parsing, and a permission failure is reported
  instead of thrown.
- `--chunk 1` looped forever: consecutive windows overlap by 1 s, so a 1 s
  window never advanced. A window that leaves no room for the overlap is now
  rejected, with the smallest usable value in the message.
- The model was resolved (and downloaded — 667 MB for the default alias)
  *before* the input file, the flags and the compute choice were checked. Every
  check now happens first, so a mistyped filename or a `--backend cuda` on a
  machine without CUDA costs nothing.
- An unknown option was ignored: `--formt json` silently wrote the plain
  format. Unknown options, an extra positional argument, an unusable `--chunk`
  and an empty `--lang` are now reported.
- A HuggingFace spec naming a file in a subdirectory
  (`owner/repo/q4/model.gguf`) could never resolve and reported the file as
  "not found": only the bare file name was compared against the repo listing.
  The full path is accepted too.
- The README claimed that pinning `@<revision>` in a HuggingFace spec makes a
  later run offline. It does not: a spec carries no sha256, so the hash is read
  from the HuggingFace metadata on **every** run. Only the curated aliases are
  offline from the second run on, because the shipped manifest pins their
  revision *and* their hash. The README now says that.

Added:

- Test coverage for the CLI: argument parsing, the window/overlap math, the
  audio loader, the plain/WebVTT/JSON writers, the model manifest, the download
  and its sha256 verification (with a stubbed HTTP handler, so no 600 MB fetch
  is needed), the command in process, and the real executable end to end. The
  CLI assembly goes from effectively no coverage to ~92% line coverage
  (224 -> 360 tests).
- The README's "All options" block had drifted from `transcribe --help` (it was
  missing `--backend`, `--device` and `--list-devices`); a test now compares the
  two.
- A "Speaker diarization" section in the README: the four ways to ask for it
  (capability probe, per-run toggle, batch, CLI), verified against the MOSS
  diarization model, and the one that is not available — the streaming API,
  whose upstream `transcribe_stream_params` has no `diarize` field in
  transcribe.cpp v0.2.4.

## [0.3.1] - wrapper release

Binds to **transcribe.cpp v0.2.4** (was v0.2.3). No C# API change; this is a
packaging release, so the wrapper takes a patch bump while `Interop` and
`Native.*` move to 0.2.4.

- The `TranscribeCppSharp.Native.*` packages now redistribute the upstream
  v0.2.4 archives. On the native side that means ggml 0.25.3, an upstream fix
  for decode-budget scaling, lower Parakeet memory use, and an offline Voxtral
  patch — all authored upstream by the transcribe.cpp authors.
- **No ABI change**: the upstream Rust FFI file is byte-for-byte identical
  between v0.2.3 and v0.2.4, and the public header differs only by its version
  define and one doc comment. The only visible effect on this package is a
  corrected doc comment on `ItnMode.ItnModeDefault`: upstream v0.2.4 documents
  that `DEFAULT` resolves to **ON** for `sensevoice` (its ITN toggle is the only
  source of casing and punctuation there), while `funasr_nano` keeps the
  upstream `itn=False` default.

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
- **GPU by default.** The tool no longer pins the CPU backend: it uses the
  upstream `AUTO` policy (every discrete GPU probed before the integrated ones,
  CPU as last resort) and prints the backend the model actually landed on.
  `--backend <auto|cpu|cpu-accel|metal|vulkan|cuda|rocm>` forces one and
  `--device <n>` pins an exact device (indices from `--list-devices`); a forced
  choice fails loudly rather than falling back silently.
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

- **v0.2.4** — current version packaged by this project.
- **v0.2.3** — packaged by 0.2.0 and 0.3.0.
- **v0.1.3** — first version packaged by this project.

[0.3.1]: https://github.com/manuc66/TranscribeCppSharp/compare/v0.3.0...v0.3.1
[0.3.0]: https://github.com/manuc66/TranscribeCppSharp/compare/v0.2.0...v0.3.0
[0.2.0]: https://github.com/manuc66/TranscribeCppSharp/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/manuc66/TranscribeCppSharp/compare/v0.1.3...v0.1.0
