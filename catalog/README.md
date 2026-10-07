# Upstream model catalog — verbatim, pinned

The files in `catalog.zip` are **upstream's, not ours**. They come from
[`handy-computer/transcribe.cpp`](https://github.com/handy-computer/transcribe.cpp),
the project whose native library this one packages. Everything here is their work:

```json
{
  "repository": "https://github.com/handy-computer/transcribe.cpp",
  "tag": "v0.3.0",
  "tag_object": "a87ca3373011e7760d4c477ce20199f5edb61770",
  "commit": "077110eb00ca6880e13bef4ddebf766b3b76896c",
  "files": 72
}
```

See [PINNED.json](PINNED.json) for the record as it was fetched. The tag is
annotated and immutable, so `catalog/<alias>.json` at that tag is the same bytes
forever; [SHA256SUMS](SHA256SUMS) records one checksum per file, which the tests
re-verify so a re-zip cannot quietly change what is inside.

## Why these files and not their `catalog.db`

Every transcribe.cpp release ships a queryable `catalog.db` with a published
SHA-256. **We do not ship it.** Reading a SQLite file needs a SQLite reader, which
in .NET means a native binary — and this project's wrapper states that it
*platform-agnostic and ships no native binaries*. The per-model JSON files carry
the same records, need only `System.IO.Compression` and `System.Text.Json`, which
are already in the framework, and are read one entry at a time.

## What is in them

Per model: `family` (the real one, not a guess from the repository name),
`params`, `upstream_repo` and `upstream_commit` (where the checkpoint came from),
`languages`, `capabilities` (each with a `supported` **and** a `verified` flag),
every published quantization with its size, `accuracy_benchmarks` (WER/CER per
language and quantization, with confidence intervals and a `measurement_provenance`
flag), and `speed_benchmarks` measured on three named machines.

## How it may be used

- **The accuracy figures are upstream's measurements, not ours.** Attribute them.
  Many rows carry `measurement_provenance: "legacy-published"` with a null
  `engine_sha`, meaning they are not tied to a specific build of the engine.
- **Do not present their speed numbers beside ours.** The `speed_benchmarks` are
  for three machines (`m4-max`, `ryzen-4750u` and one other), while the timings
  this project measures are session-local and keyed to the machine that took them.
  Putting the two in one column would be a ranking nobody checked — which is the
  rule `ModelBenchmarkOrder` exists to enforce.
- **A language listed is not a language that works well.** `whisper-tiny` declares
  roughly a hundred languages; their own table puts `en` at 7.5 % WER, `fr` at
  44 %, and several languages above 100 %. Showing the count without that context
  would overstate support.

upstream transcribe.cpp is MIT-licensed; their `LICENSE` and
`THIRD-PARTY-LICENSES.md` cover these files as part of that repository.

## Regenerating

Not built from `main` — that would drift from the native library this project
binds, which is pinned in [`build/TRANSCRIBE_VERSION`](../build/TRANSCRIBE_VERSION).
To move the pin forward, re-run the fetch against the new tag, update
`PINNED.json`, `SHA256SUMS` and the version, then run the tests.