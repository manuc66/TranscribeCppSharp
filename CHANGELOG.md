# Changelog

All notable changes to this project are documented in this file.

The versioning scheme is described in the [README](README.md#versioning--compatibility):
the wrapper (`TranscribeCppSharp`) follows SemVer for its own C# API, while
`TranscribeCppSharp.Interop` and `TranscribeCppSharp.Native.*` track the upstream
[transcribe.cpp](https://github.com/handy-computer/transcribe.cpp) version they bind to.

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

- **v0.1.3** — first version packaged by this project.

[0.1.0]: https://github.com/manuc66/TranscribeCppSharp/compare/v0.1.3...v0.1.0
