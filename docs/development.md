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

# Check the installed libraries against build/native-libs-sha256.json. Offline:
# it re-hashes what is there, which is what catches a tampered tree. FetchNative
# records the pinned version in the ".done" marker and refetches when
# build/TRANSCRIBE_VERSION moves; --verify still catches a cache that was
# restored as-is.
dotnet run --project tools/FetchNative -- --verify

# Run unit and integration tests
./scripts/run-integration-tests.sh

# Opt-in: also fetch the MOSS diarization model (~668 MB) and run the
# speaker-attribution test
WITH_DIARIZATION_MODEL=1 ./scripts/run-integration-tests.sh

# Run the smoke test sample. A local Debug build targets net10.0 only; CI and
# Release builds also produce net8.0/net9.0, so -f names the one to run.
dotnet run --project samples/SmokeTest -f net10.0 -- model.gguf audio.wav

# Run the CLI from source (see "Command-line tool" for the installed tool).
# WAV is read directly; other formats (ogg, mp3, …) are decoded with ffmpeg.
dotnet run --project src/TranscribeCppSharp.Cli -- audio.ogg

# Regenerate the desktop-app screenshots in docs/assets/images/ui-*.png
dotnet run --project samples/UIScreenshot
```

Every command above needs `tools/FetchNative` to have run first, so the native
libraries are in the output directory. The screenshot generator does too — it
opens the real window, which initializes the compute backends.

A local **Debug** build keeps only what this machine can load, and prunes the
rest from its output:

- of the `runtimes/<rid>` trees that Avalonia's SkiaSharp and HarfBuzzSharp
  dependencies bring, only the host's RID and its family folder (`osx`, `win`)
  stay. On this Linux machine that is `linux-x64` alone instead of thirteen
  Linux RIDs (musl, bionic, loongarch64, riscv64…): 14 MiB instead of 180 MiB
  for the desktop app's output;
- the projects that ship `net8.0`/`net9.0`/`net10.0` assets build `net10.0`
  only, so a sample that references them is not copied three times. Restore
  still resolves all three, so `packages.lock.json` keeps its per-framework
  sections and a local build cannot rewrite a versioned file;
- the native runtime each output stages (65 MiB, 43 MiB of it the Vulkan
  backend) is **hardlinked** from `native-packages/` on Unix instead of copied,
  so the ten outputs share one copy on disk. Same bytes, same inode: nothing a
  build produces or loads changes.

Measured here, the UI's Debug output went from 263 MiB to 97 MiB and the whole
`bin`+`obj` tree from 1.6 GiB to 200 MiB. **What ships does not change**: with
`CI=true`, in `Release`, and for a pack or a publish, every framework is built
and the full portable `runtimes/` tree is kept (`-r <rid>`); the trim is skipped
outside a local Debug build. See `TranscribeTargetFrameworks` in
`Directory.Build.props` and `TrimForeignNativeAssets` in
`Directory.Build.targets`.

The screenshots are byte-identical across runs **on one machine**, which is what
makes regenerating them safe: on that machine, a changed PNG means something in
the UI changed. They are **not** reproducible between machines. The fonts, the
compute devices on the Settings tab and the cache counts on the Models tab all
come from the machine that drew them, so those pixels change when the machine
does.

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
1.  Clone [transcribe.cpp](https://github.com/handy-computer/transcribe.cpp) at the tag this project binds to — `build/TRANSCRIBE_VERSION`, currently `v0.4.2`. The resolver checks `transcribe_version()` before accepting a native, so a build from another tag is rejected and named in the error rather than loaded against bindings generated for a different struct layout.
2.  Build the native library using `cmake` (ensure `BUILD_SHARED_LIBS=ON`). On Alpine, build inside the distro so the resulting library links against `musl`.
3.  Copy the resulting `libtranscribe.so` (or `.dll`/`.dylib`) — **and the sibling `libggml*.so` files it loads** — into your application's output directory. As with [Using CUDA](compute.md#using-cuda), the wrapper prefers native binaries in the app output directory over the packaged ones, so no `LD_LIBRARY_PATH` is needed. The C# interop contract is unchanged as long as the version matches: only the native binaries differ, not the P/Invoke signatures.

## This documentation site

The site is published from [`docs/`](../docs) by GitHub's own Jekyll build — there is no
site build step in this repository to run or maintain. The theme is pinned to a release
so it cannot change under the content. Pages carry a `nav_order` in their front matter,
which is what fixes their position in the sidebar; adding a page means adding a number,
not editing a navigation file.
