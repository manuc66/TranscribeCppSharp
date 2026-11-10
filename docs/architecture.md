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

A `DllImportResolver` registered in the Interop layer finds `libtranscribe` in the app output directory (including the `runtimes/<rid>/native/` layout) and the NuGet global packages folder — without requiring `LD_LIBRARY_PATH`. Its `libggml*` dependencies are loaded from the same directory by the native loader.

Every candidate is checked with `transcribe_version()` before it is accepted. The
bindings are generated against one upstream `transcribe.cpp` release and struct sizes
move between them (v0.4.1 grew `transcribe_model_load_params` from 24 to 32 bytes), so a
native left over from an earlier package version must not be loaded. The NuGet packages
folder keeps every version the machine ever restored; a candidate that reports another
version is unloaded and listed in the exception rather than silently winning the search
and failing later on a struct-size check. Because the resolver owns the search, it either
returns an accepted library or throws its own message — the runtime's default probing is
not a second fallback.

In practice this means a plain `dotnet run` needs no `<RuntimeIdentifier>`. When the
library is still missing — typically a forgotten runtime package — the wrapper throws a
`DllNotFoundException` naming the exact package to add (with the version the bindings
target), the paths it searched, and any candidate rejected for its version. The full
story is under [Installation](getting-started.md#installation).

Because the resolver prefers the app output directory, dropping your own native build
next to your app overrides the packaged one. That is how both
[CUDA](compute.md#using-cuda) and [Alpine/musl](development.md#building-from-source) are
supported without a package variant: only the native binaries differ, the P/Invoke
signatures do not.

## Versioning & compatibility

Two version numbers are in play, decoupled on purpose:

- **`TranscribeCppSharp`** (this wrapper) follows [Semantic Versioning (SemVer)](https://semver.org/) for its **own C# API**. Breaking API changes bump the major/minor version of the wrapper. The user-facing packages **`TranscribeCppSharp.Interop`**, **`TranscribeCppSharp.Bundle`** (meta-package) and **`TranscribeCppSharp.Cli`** (the `transcribe` tool) follow the wrapper's version too: the Interop assembly is generated C# that is rebuilt with every wrapper release, so it is republished under the wrapper's number.
- **`TranscribeCppSharp.Native.*`** runtime packages are versioned to match the **upstream `transcribe.cpp` version** they bind to (e.g. `0.4.1` = transcribe.cpp v0.4.1). They track the ABI.

So `TranscribeCppSharp 0.4.3` depends on `TranscribeCppSharp.Interop 0.4.3`, while the bundle it ships pulls the `TranscribeCppSharp.Native.*` packages of the upstream version it binds (`0.4.1` here). An upstream release ships as new Native packages without necessarily changing the wrapper's own version; a wrapper release always republishes Interop, because its assets changed with the wrapper even when the ABI did not. The correspondence between a wrapper release and the upstream version it targets is recorded in [CHANGELOG.md](https://github.com/manuc66/TranscribeCppSharp/blob/main/CHANGELOG.md).

This decoupling is why documentation here cites upstream behaviour at a specific
version — "transcribe.cpp v0.4.1" appears in the streaming, thread-safety and CUDA
sections because those statements are only true of that version, and a later upstream
release may change them.
