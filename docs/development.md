---
title: Development
nav_order: 13
---

# Development

## Prerequisites

- .NET 10.0 or later.
- Native libraries (can be fetched using the provided script).

## Building and testing

```bash
# Download native libraries for your current platform
dotnet run --project tools/FetchNative

# Run unit and integration tests
./scripts/run-integration-tests.sh

# Opt-in: also fetch the MOSS diarization model (~668 MB) and run the
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

## Tests that keep the documentation honest

Documentation here is not maintained by hand alone. Two checks run in CI and fail the
build when prose and reality diverge:

- **`ReadmeExamplesTest`** (`tests/TranscribeCppSharp.Interop.Tests/ReadmeExamplesTest.cs`)
  keeps every `<!-- @readme … -->`-marked C# block in `README.md` byte-identical to the
  `// @readme-begin` region in `HighLevelApiTests.cs` and `samples/SmokeTest/Program.cs`,
  and checks the `transcribe --help` block against `TranscribeCommand.HelpText`. The
  help-text check exists because that block had drifted once: the compute options were
  missing from the prose and present in the tool.
- **`license-check.sh`** (`tools/license-check.sh`) verifies that every packable project
  declares an explicit MIT license, and that `README.md` — which is packed into every
  NuGet package — still carries the upstream attribution. CI fails if the string
  "transcribe.cpp authors" is missing from it.

## Building from source

The `Native.*` packages redistribute exactly what upstream transcribe.cpp publishes in its releases — nothing more. This project is a packaging/binding layer, not a binary provider: it does not compile musl, CUDA, or other variant builds. If a variant you need is not in the upstream release, building it yourself is on you.

You need to build from source when:
- You are using **Alpine Linux** (which uses `musl` instead of `glibc`, making the pre-built Linux binaries incompatible). Upstream transcribe.cpp does not ship musl builds, so there is no `Native.*` package to install for this case.
- You need to support a non-standard architecture or custom OS.
- You want to enable specific hardware optimizations not included in the default build.

**Steps:**
1.  Clone [transcribe.cpp](https://github.com/handy-computer/transcribe.cpp).
2.  Build the native library using `cmake` (ensure `BUILD_SHARED_LIBS=ON`). On Alpine, build inside the distro so the resulting library links against `musl`.
3.  Copy the resulting `libtranscribe.so` (or `.dll`/`.dylib`) — **and the sibling `libggml*.so` files it loads** — into your application's output directory. As with [Using CUDA](compute.md#using-cuda), the wrapper prefers native binaries in the app output directory over the packaged ones, so no `LD_LIBRARY_PATH` is needed. The C# interop contract is unchanged: only the native binaries differ, not the P/Invoke signatures.

## This documentation site

The site is published from [`docs/`](../docs) by GitHub's own Jekyll build — there is no
site build step in this repository to run or maintain. The theme is pinned to a release
so it cannot change under the content. Pages carry a `nav_order` in their front matter,
which is what fixes their position in the sidebar; adding a page means adding a number,
not editing a navigation file.
