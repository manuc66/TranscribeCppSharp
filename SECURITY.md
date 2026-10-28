# Security policy

## Reporting a vulnerability

Use GitHub's private advisory form:
<https://github.com/manuc66/TranscribeCppSharp/security/advisories/new>.

Please do not open a public issue for a vulnerability. Include the package and
version (`TranscribeCppSharp`, `TranscribeCppSharp.Native.<rid>`, the CLI or the
server), the platform, and the smallest reproduction you have.

## Scope

This repository is a packaging and binding layer over
[transcribe.cpp](https://github.com/handy-computer/transcribe.cpp). In scope: the
C# wrapper, the generated interop bindings, the CLI, the desktop app, the
experimental HTTP server, the packaging and the release workflow.

**Not this project's code:** the native library and ggml. A crash or a wrong
result inside decoding or inference belongs upstream; it will be relayed and the
pin in `build/TRANSCRIBE_VERSION` bumped, but the fix is theirs.

**Not sandboxed:** model files. They come from the user or from HuggingFace
(pinned revision, sha256 verified before the file enters the cache), and they are
parsed by the native library in this process. A malicious GGUF is a native-code
trust decision, not something the wrapper can contain.

## What is actually checked

- Native release archives are pinned by sha256 (`build/native-sha256.json`) and
  the installed files are re-hashed before tests and packing
  (`build/native-libs-sha256.json`, `FetchNative --verify`).
- Model downloads are pinned to a revision and verified against the LFS sha256
  before they are moved into the cache.
- The workflows pin every action by commit sha, default to `contents: read`, and
  run gitleaks over the full history.

## What is not claimed

There is no bug bounty and no third-party audit. Nothing here is claimed to be
thread-safe, memory-safe or leak-free beyond what the tests and the ABI checks
establish. If something is unknown, it is written down as unknown.
