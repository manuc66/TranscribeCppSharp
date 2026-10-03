---
title: Architecture
nav_order: 11
---

# Architecture

The project is divided into several layers, each with a distinct responsibility:

1.  **`TranscribeCppSharp.Native.*` (Runtimes)**: Platform-specific packages containing the pre-compiled native `libtranscribe` binaries.
2.  **`TranscribeCppSharp.Interop` (Low-level)**: Auto-generated P/Invoke declarations using `LibraryImport`.
3.  **`TranscribeCppSharp` (High-level)**: Idiomatic C# abstraction layer providing `IDisposable` resources and typed exceptions.
4.  **`TranscribeCppSharp.Cli` (Command-line tool)**: The `transcribe` .NET tool, a consumer of the high-level wrapper. It adds model resolution/download/caching, windowing and transcript export on top of it.
5.  **`Generator` (Tool)**: Ensures C# bindings stay in sync with the upstream native API by parsing Rust FFI definitions.

## Native library loading

A `DllImportResolver` registered in the Interop layer finds `libtranscribe` in the app output directory (including the `runtimes/<rid>/native/` layout), the NuGet global packages folder, or lets the runtime's default resolution (`.deps.json` runtime targets) handle it — without requiring `LD_LIBRARY_PATH`. Its `libggml*` dependencies are loaded from the same directory by the native loader.

In practice this means a plain `dotnet run` needs no `<RuntimeIdentifier>`. When the
library is still missing — typically a forgotten runtime package — the wrapper throws a
`DllNotFoundException` naming the exact package to add and the paths it searched, rather
than a bare "not found". The full story is under
[Installation](getting-started.md#installation).

Because the resolver prefers the app output directory, dropping your own native build
next to your app overrides the packaged one. That is how both
[CUDA](compute.md#using-cuda) and [Alpine/musl](development.md#building-from-source) are
supported without a package variant: only the native binaries differ, the P/Invoke
signatures do not.

## Versioning & compatibility

Two version numbers are in play, decoupled on purpose:

- **`TranscribeCppSharp`** (this wrapper) follows [Semantic Versioning (SemVer)](https://semver.org/) for its **own C# API**. Breaking API changes bump the major/minor version of the wrapper. The user-facing packages **`TranscribeCppSharp.Bundle`** (meta-package) and **`TranscribeCppSharp.Cli`** (the `transcribe` tool) follow the wrapper's version too.
- **`TranscribeCppSharp.Interop`** and the **`TranscribeCppSharp.Native.*`** runtime packages are versioned to match the **upstream `transcribe.cpp` version** they bind to (e.g. `0.3.0` = transcribe.cpp v0.3.0). They track the ABI, not the wrapper's API.

So `TranscribeCppSharp 0.3.1` depends on `TranscribeCppSharp.Interop 0.3.0`; `TranscribeCppSharp.Bundle 0.3.1` pulls a wrapper and the native runtime packages of the matching upstream version. A later upstream release will ship as a new Interop/Native version without necessarily changing the wrapper's own version. The correspondence between a wrapper release and the upstream version it targets is recorded in [CHANGELOG.md](https://github.com/manuc66/TranscribeCppSharp/blob/main/CHANGELOG.md).

This decoupling is why documentation here cites upstream behaviour at a specific
version — "transcribe.cpp v0.3.0" appears in the streaming, thread-safety and CUDA
sections because those statements are only true of that version, and a later upstream
release may change them.
