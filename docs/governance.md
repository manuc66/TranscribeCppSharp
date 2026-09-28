---
title: Governance
nav_order: 14
---

# Governance

## Security

To report a security vulnerability, please use the [GitHub Security Advisory](https://github.com/manuc66/TranscribeCppSharp/security/advisories) feature.

## License

This project is licensed under the **MIT License** (matching `transcribe.cpp`).

There is a second licence in play that this one does not cover, and it is easy to miss:
**the models you load are not covered**. They come from different ecosystems with
different terms, some non-commercial. See [Model licenses](models.md#model-licenses).

## Attribution

This project is **a packaging and binding effort only** — the underlying library is not my work:

- The native library (`transcribe.cpp`) is developed and owned by the [transcribe.cpp authors](https://github.com/handy-computer/transcribe.cpp) (MIT License).
- The bundled native components (ggml, etc.) are owned by their respective authors; their MIT license texts are distributed alongside the binaries in the `TranscribeCppSharp.Native.*` packages.
- I did **not** author the native library and claim no credit for it. This repository only adds:
  - A C# interop layer (auto-generated P/Invoke bindings via `LibraryImport`).
  - A high-level C# wrapper (`IDisposable` resources, typed exceptions).
  - A command-line tool (`transcribe`) and the model manifest it resolves against.
  - Pre-built native binaries packaged for .NET consumption.

The transcribe.cpp project is an independent upstream project; bug reports about the native library itself should go to its [repository](https://github.com/handy-computer/transcribe.cpp).

The speech-to-text models are third-party work as well, and none of them are bundled
here: Whisper, Moonshine, Parakeet, Canary, GigaAM, Voxtral, Qwen3-ASR and the MOSS
diarization model are all developed by their own authors and carry their own terms. The
alias list links each model's licence but does not redistribute it — see
[Model licenses](models.md#model-licenses).

The same attribution appears in `README.md` and in the NuGet package description, and
`tools/license-check.sh` fails CI if it is removed from the README — the README is packed
into every package, so the credit has to travel with the binaries.
