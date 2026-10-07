---
title: Desktop app
nav_order: 15
---

# Desktop app

`TranscribeCppSharp.Ui` is an [Avalonia](https://avaloniaui.net/) app on .NET 10 that drives
the same wrapper and the same native library as the [command-line tool](cli.md). It adds no
transcription logic of its own, so every limit documented on the other pages — the
[thread-safety rule](concurrency.md#thread-safety), the [streaming abort](streaming-bench.md),
the [missing CUDA runtime](compute.md#using-cuda) — applies to it unchanged.

## Running it

The app is **not distributed**. There is no installer, no published binary and no NuGet
package for it; what exists is the source in `src/TranscribeCppSharp.Ui`. To run it, fetch
the native libraries once and then start it:

```bash
dotnet run --project tools/FetchNative
dotnet run --project src/TranscribeCppSharp.Ui
```

`FetchNative` is the same prerequisite the CLI has. The project copies the fetched native
build into the app's output directory on every build, which is deliberate: a front end that
resolved an older `libtranscribe` would fail at run time rather than at build time, on
whichever entry point happened to be missing.

The native library is what the app and the CLI share, so there is no per-app installation to
keep in step. What is shared is the model cache as well: see [Models](#models) below.

The window opens on Windows, macOS and Linux. It is resizable, scrolls vertically, and
does not scroll horizontally — a horizontally scrollable panel would offer the grids an
infinite width, and a `DataGrid` given infinite width gives its star-sized columns no width
at all.

## The five tabs

The pictures below are generated from the app itself by
[`samples/UiScreenshot`](../samples/UiScreenshot), which drives the real window with the
real view models and takes the frame itself. Nothing is stubbed to suit a picture, and
nothing is fabricated to fill one: what a tab shows on a fresh machine is what a tab shows
here — no audio picked, nothing transcribed, nothing downloaded. What the machine decides
is in the picture, so the Settings tab lists whatever devices it has, and the cache line
counts whatever it has fetched. Regenerate them with:

```bash
dotnet run --project samples/UiScreenshot           # writes into docs/assets/images
```

**Where the grid scrolls.** The Models tab is the one tab not wrapped in the view's
`ScrollViewer`. That wrapper measures its child against infinite height, and a star row
given infinite height resolves to its content — the grid came out 2408 px tall in a 720 px
window, so it never scrolled internally and the whole page scrolled instead, taking the
column headers off the top and leaving the detail pane four screens down. Without it the
grid takes the space that is left, scrolls inside itself with its header in place, and the
detail pane appears by shrinking it.

**What the three model pickers offer.** Transcription, Streaming and Batch list only the
aliases already on disk, so an entry means you can run it now — with a fresh cache the list
is empty and the button that starts a run is disabled. Under each picker sits a count and a
`Get more…` button that switches to the Models tab, always visible rather than only once the
list has run out: a picker with one entry does not tell you the other 71 exist. The list is
re-read on every tab change, because downloads happen in Models while the pickers are built
once when the window opens — without that, the guidance would point at a download that never
appears.

### Transcription

![The Transcription tab.]({{ '/assets/images/ui-transcription.png' | relative_url }})

Pick a file, a model and the decoder settings, then run.

A 16 kHz mono 16-bit WAV is read directly. Any other format (`mp3`, `flac`, `ogg`, `m4a`)
is decoded with `ffmpeg`, which has to be installed and on `PATH` — see
[Audio input](audio-input.md).

| Setting | Default | Notes |
|---|---|---|
| Model | the CLI's default alias | Downloaded on first use and cached |
| Language | `en`, then whatever the model allows | Before the model is probed, the picker falls back to ten codes (`en`, `fr`, `de`, `es`, `it`, `pt`, `ru`, `zh`, `ja`, `ko`). Once a model has been probed the list is what *it* declares, with `auto` added when it reports language detection, and `auto` means no language is passed at all so the model decides for itself. If the current choice is not in the new list, the picker takes the first entry instead of keeping an unavailable one |
| Threads | library default | Left empty on purpose. This is not a benchmarked recommendation — raising it past the core count usually makes it slower |
| KV type | Auto | `F32` keeps the most precision and the most memory; `F16` halves the cache |
| Context size | model maximum | Lower uses less memory; too low truncates long passages |
| Window | 300 s | Longest audio passed in one call. Longer audio is split into windows overlapping by 1 s and merged; see [Long audio and diarization](diarization.md) |
| Keep special tags | off | Off removes markers such as `<|notimestamps|>` from the text |

Three settings appear only for models that have them: the Whisper initial prompt and
temperature, the task and the target-language picker, and the speculative-decoding
draft count. They are
hidden based on what the model reports, not on what its name suggests — see
[Asking the model](#asking-the-model) below.

The result is shown four ways: as text, as segments with timings, as words with timings, and
as speaker-attributed segments. `Export` writes `.txt`, `.vtt` or `.json`, picked by the
extension you type; the formats themselves are the same as
[the CLI's](exporting.md).

### Streaming

![The Streaming tab.]({{ '/assets/images/ui-streaming.png' | relative_url }})

Transcribes the microphone live, at 16 kHz mono 16-bit PCM.

The two text areas are the point of the layout. **Committed** text, on the left, is final.
**Tentative** text, on the right and in italics, is what the model would say so far and may
still revise. Which side text lands on depends on the commit policy: `Auto` lets the model
decide, `Finalize` commits only when the model calls a segment final (later but steadier
text), and `Stable Prefix` commits once a prefix has stopped changing (sooner text that
can still be revised). The Stable Prefix agreement count is how many unchanged passes a
prefix needs before it is committed.

Family extensions appear only for a model that both streams and has the matching extension
builder: a decode interval for Moonshine, buffered-streaming context and attention for
Parakeet, a latency/quality preset for Sortformer, delay tokens and a decode interval for
Voxtral.

Read [Real-time streaming](streaming.md) and [Streaming benchmarks](streaming-bench.md)
before relying on this tab. In short, and unchanged by the GUI: the model default does not
keep up with live audio on the CPU backend, setting a decode interval or the commit policy is
not optional for a live pipeline, and on the CPU backend a stream of roughly 88 s of audio
**aborts the process inside ggml**. That abort is native, so no `try`/`catch` in the app
intercepts it and no status line will ever report it. It is upstream behaviour in
transcribe.cpp, not a bug in this app.

### Batch

![The Batch tab.]({{ '/assets/images/ui-batch.png' | relative_url }})

Queue several files and run them one after another against a single loaded model.

Sequential is not an oversight: the library is
[not thread-safe](concurrency.md#thread-safety), so one model at a time is what is safe.
Results land in a grid with the detected language, the status and the text, and export as
`.txt` or `.json`.

### Models

![The Models tab.]({{ '/assets/images/ui-models.png' | relative_url }})

The 72 curated aliases from the same manifest the CLI resolves against. All of them together
come to 58 GiB, which is why the grid starts empty of weights and shows what is on disk.

- **Filters.** Free text over the alias, the family read from the repository name, the
  repository and the licence; plus *on disk only*, *commercial licences only*, a size
  filter, and a licence filter. The commercial filter is a text test on the SPDX identifier
  in the manifest, so read the licence itself before relying on it — `other` means the
  manifest names no standard licence.
- **Per row:** download, measure, delete. A download is verified against the sha256 in the
  manifest before it is cached, at the pinned revision.
- **The default model is marked.** The alias the CLI uses when no `--model` is given is
  shown as `default` in the Alias column. It is a mark of which alias a command ends up
  using, not a claim about quality or speed.
- **Sort by clicking a header.** Alias, Size and *x realtime* order the grid, and the
  arrow on the header says which way. Clicking again reverses it. The other columns have
  no order behind them — you cannot sort by licence or by a count — so they say so by
  not taking a click rather than by sorting something arbitrary. The three sort buttons
  above the grid set the same state, so both routes move the same arrow.
- **Filter by language.** The picker beside the licence and size filters narrows the
  grid to the models whose card declares a language — codes, as the cards state them.
  A model that declares nothing is hidden while a language is selected, because
  unknown is not the same as yes. The free-text search reaches the codes too, so
  typing one filters the same way, and *Clear* turns all of them off at once.
- **The detail pane quotes upstream.** Below the grid, the selected model shows what
  [`transcribe.cpp`](https://github.com/handy-computer/transcribe.cpp) itself records:
  the parameter count and the checkpoint it was converted from, every quantization they
  publish (with the note that only the one this row pins has been downloaded and verified
  here), their capability flags — with *unverified upstream* when they mark one supported
  without claiming to have checked it — and their headline accuracy figure, named as their
  measurement on their benchmark. The family column comes from the same source, so it
  reads `whisper` rather than a name guessed from the repository.
  The data is pinned at the tag this project binds; see
  [`catalog/`](../catalog/README.md) for how it was fetched and what it must not be used for.
- **Licence and languages come from the model card.** The grid's Languages column
  shows the first four codes and counts the rest, because some models declare 99;
  the detail pane below carries the whole list and names where it came from. Every
  one of the 72 entries states its languages — the manifest is filled in by
  [`tools/UpdateModelManifest`](../tools/UpdateModelManifest), whose
  `--languages-only` mode reads `cardData.language` and rewrites nothing else, so
  adding the column cannot re-pin a revision or a checksum.
- **Diarization is a third state.** A model that is on disk can be loaded, which is the only
  way to learn whether it can attribute speakers; until then the column says `?`, and
  *Check diarization* loads each downloaded model once to find out. See
  [Long audio and diarization](diarization.md), including what is not verified in CI.
- **Measure** times a model over an excerpt of audio *you* pick, because a timing is only
  meaningful for the audio it was taken on. One pass is discarded as warm-up and three are
  timed; the best is kept, since contention only ever makes a run slower. The backend is
  recorded with the result, because timings from different backends are not comparable.
  Results are held in memory for the session and are not written to disk.

`Sort by speed` orders the grid by what you measured here, and models you have not measured
cannot be placed in a speed order, so they stay at the bottom in name order.

**This app does not rank models by accuracy.** This project publishes no accuracy comparison
between them, so there is none to show. `Measure` tells you how fast a model ran on your
audio and nothing else. [Streaming benchmarks](streaming-bench.md) is the only benchmark the
project has, and it covers Moonshine streaming only.

### Settings

![The Settings tab.]({{ '/assets/images/ui-settings.png' | relative_url }})

Backend and device selection, the cache directory, the version of the native library, and
the compute devices this build and this machine can see.

`Auto` is the default backend and its behaviour is the one described on
[Compute](compute.md): the GPU is used when one initializes, and a forced backend fails
rather than silently falling back. Pinning a *device* is stricter still — it never falls
back to another device, so a model that will not fit there fails instead of quietly running
elsewhere. The backend and device picked here are what the three transcription tabs use.

## Asking the model

Everywhere a model is chosen, the app loads it once and asks it what it can do, rather than
guessing from the alias — a Whisper-derived model whose name does not say "whisper" would be
guessed wrong. `Model.Supports` needs the weights loaded and the manifest declares no
capabilities, so this is the authoritative answer, and it is why options appear as soon as a
model is on disk.

Two consequences worth knowing:

- **A model that is not downloaded keeps every option visible**, and the line under the
  picker says why. Hiding an option on a guess would hide one that would have worked.
- **The probe loads on the CPU**, so checking a model's capabilities does not take the GPU
  away from a run already using it.

The language picker works the same way: ten codes stand in while nothing has been probed,
and a probed model replaces that list with the languages it actually reports. The UI used to
offer languages a model could not handle, and no way to ask for auto-detection at all.

Capabilities are cached per alias for the session.

## What the app does not do

- **It is not distributed.** Source only, as above.
- **It is not a second transcription implementation.** It is a front end over the wrapper, so
  it has exactly the wrapper's behaviour and the wrapper's limits. Where the app and the
  documentation disagree, the documentation is describing the code and the app is the thing
  that is wrong.
- **It cannot attribute speakers while streaming.** `transcribe_stream_params` has no
  `diarize` field in transcribe.cpp v0.3.0, so the streaming API cannot request it
  ([Diarization](diarization.md#not-available-streaming)). The Speakers tab belongs to the
  Transcription tab, which runs offline.
- **It does not time models for you against published numbers.** `Measure` runs on your
  machine, over your audio.
- **There is no accuracy comparison behind the model grid.** Filtering by licence, family or
  size is as far as it goes.

## How it is tested

Headless Avalonia tests in `tests/TranscribeCppSharp.Ui.Tests`, run by the same
`./scripts/run-integration-tests.sh` as everything else, on all three CI platforms. They
build the real `App` on Avalonia's headless backend, so the views load with the styles they
get on screen.

What they cover is worth stating, because it is narrow: the automation ids on all five views,
which view models hide options a model does not support, the defaults, and the catalogue
item's display states. The bindings inside a `DataTemplate` are verified by realizing the
rows and reading the values back — a property with no binding fails silently, which is how
the default-model mark stayed missing from the grid while its property existed.

Two things they do **not** cover. The accessible names set in the XAML are not asserted by
any test — only the ids are, and a name is not an id. And no test drives the window
interactively end to end, or asserts what the app looks like.

The pictures above are not a screenshot test either. They come from
[`samples/UiScreenshot`](../samples/UiScreenshot), which runs the real window through
Avalonia's headless platform and saves whatever it draws; it is a generator, run by hand,
not a check that fails when a pixel moves. Nothing in CI compares them against anything,
so a visual regression passes unnoticed — the automation ids are what CI actually pins down.