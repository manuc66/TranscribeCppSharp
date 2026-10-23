---
title: Desktop app
nav_order: 15
---

# Desktop app

`TranscribeCppSharp.UI` is an [Avalonia](https://avaloniaui.net/) app on .NET 10 that drives
the same wrapper and the same native library as the [command-line tool](cli.md). It adds no
transcription logic of its own, so every limit documented on the other pages — the
[thread-safety rule](concurrency.md#thread-safety), the [streaming abort](streaming-bench.md),
the [missing CUDA runtime](compute.md#using-cuda) — applies to it unchanged.

## Running it

Each [release](https://github.com/manuc66/TranscribeCppSharp/releases) attaches a
self-contained archive per platform — `TranscribeCppSharp.UI-<version>-<rid>.zip` for
`win-x64`, `linux-x64`, `linux-arm64`, `osx-arm64` and `osx-x64`. Unzip it and run the
executable: it carries the .NET runtime and the matching native transcribe.cpp build, so
no SDK is needed. It carries the project licence and the upstream component licences under
`licenses/`. Three things it does **not** carry:

- **No code signature.** There is no installer and no notarization, so Windows SmartScreen
  and macOS Gatekeeper will warn or refuse on first launch.
- **No `.app` bundle on macOS.** The archive is a bare executable (no `Info.plist`), so Finder
  gives it no Dock name and Gatekeeper quarantines it like any unsigned download; clear it
  with `xattr -dr com.apple.quarantine TranscribeCppSharp.UI` if it refuses to run.
- **No `ffmpeg`.** The app still needs it on `PATH` for any non-WAV input and for the
  microphone ([Audio input](audio-input.md)).

To run from a clone instead, fetch the native libraries once and then start it:

```bash
dotnet run --project tools/FetchNative
dotnet run --project src/TranscribeCppSharp.UI
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

## The six tabs

The pictures below are generated from the app itself by
[`samples/UIScreenshot`](../samples/UIScreenshot), which drives the real window with the
real view models and takes the frame itself. Nothing is stubbed to suit a picture, and
nothing is fabricated to fill one: no audio is picked and nothing is transcribed, and what
the machine has decided is in the picture — the Settings tab lists the devices it can see,
and the model pickers and cache line count what it has fetched. Regenerate them with:

```bash
dotnet run --project samples/UIScreenshot           # writes into docs/assets/images
```

Each tab also carries an icon, and its page opens with that icon, the tab's name and a
one-line description of what the tab is for. The icons come from
[Material.Icons.Avalonia](https://github.com/AvaloniaUtils/Material.Icons.Avalonia) (MIT)
and are decorative: every action still carries its text label, and the accessible names
are unchanged.

**Where the grid scrolls.** The Models tab is the one tab not wrapped in the view's
`ScrollViewer`. That wrapper measures its child against infinite height, and a star row
given infinite height resolves to its content — the grid came out 2408 px tall in a 720 px
window, so it never scrolled internally and the whole page scrolled instead, taking the
column headers off the top and leaving the detail pane four screens down. Without it the
grid takes the space that is left, scrolls inside itself with its header in place, and the
detail pane appears by shrinking it.

**And both star, not one.** The grid and the detail pane are two `*` rows, so they split
what the fixed rows leave rather than competing for it. The fixed rows measure 368 px — the
banner carrying a caveat, the filter row and the sort row both wrapping, a summary, the
buttons and a status line — so at the old 720 px window only 222 px were left: 111 px each,
and **zero for the grid** once a row was selected and the pane took its fixed 250. That is a
master-detail whose master disappears when you pick a row, which is what the test that
asserts a floor now guards. The window opens at 880 px and will not shrink below 640 px,
which is where the Models tab stops being usable.

**What the three model pickers offer.** Transcription, Streaming and Batch list only the
aliases already on disk, so an entry means you can run it now — with a fresh cache the list
is empty and the button that starts a run is disabled. Streaming narrows that further to the
models that can stream — upstream's declaration when nothing has been checked, this machine's
own answer once the model has been loaded — because a model that cannot stream has nothing to
do on that tab. Under each picker sits a count and a
`Get more…` button that switches to the Models tab, always visible rather than only once the
list has run out: a picker with one entry does not tell you the other 71 exist. The list is
re-read on every tab change, because downloads happen in Models while the pickers are built
once when the window opens — without that, the guidance would point at a download that never
appears.

**Which entry is selected.** A tab reselects the model it last *ran*, not the first alias
alphabetically: the app records it in `ui-settings.json` beside the log (`<cache>/../ui-settings.json`)
and reads it when the picker is built. Picking a model in the list and not running it is not a
use and is not recorded. A model that has since been deleted, or that no longer passes the
streaming filter, is ignored — the picker falls back to the first entry. With nothing recorded
yet, or a settings file that cannot be read, the first alias is selected as before.

### Transcription

![The Transcription tab.]({{ '/assets/images/ui-transcription.png' | relative_url }})

Pick a file, a model and the decoder settings, then run.

The form is ordered as the decisions are: **Audio**, **Goal**, **Model and language**,
**Run options**, **Speaker labels** (only for the speaker goal), then one **Advanced settings**
disclosure for the decoder tuning that has a working default. Speaker labels come after the
transcript options because they are post-processing: a dedicated diarizer runs as a second
pass over the same audio, after the transcriber; a model that diarizes itself does it in the
same pass. One line under the model restates the settings (`Will transcribe, language fr,
windows of 30 s`), so the pickers and the result cannot disagree after the fact. The primary
button is disabled until a model for the goal is on disk, a file is selected and still exists,
and a requested speaker attribution can actually be produced; when it is disabled, the line
under it names the missing piece and where to get it instead of leaving a dead button.

**Goal** is the outcome, not a native role: *Speech to text*, *Speech to text + speakers*,
*Speakers only*, *Identify language* or *Detect speech*. It filters the model picker to the
catalog role the outcome needs, so a diarizer is not offered for a text run and the
language-identification and VAD models are not hidden among the transcribers. The line under
the picker names the pipeline in the order it runs
(`Audio → speech to text (windows of 30 s) → speaker labels with diar_streaming_sortformer_4spk-v2.1 (second pass) → text + speakers`).
When no model for the goal is on disk, the picker is empty, the hint names the missing role and
**Get more…** opens the Models tab with that role filter already set. The goal also decides the
speaker step: choosing the speaker goal includes it, and choosing text-only leaves it out.

The tab runs the **role** the selected model serves (transcribe.cpp 0.4), and the
run button and the summary name it. An ASR model is transcribed as below; a
diarization model (Sortformer) returns speaker turns; a language-identification
model returns the ranked languages; a voice-activity model returns the speech
segments. The result appears in the Text tab and, by role, in a **Languages** grid
(ranked candidates) or a **Speech** grid (segments, in samples); a diarizer fills
the **Speakers** tab. A model whose role is language identification also shows an
**Allowed languages** field (comma-separated codes) to restrict the ranking; a model that
transcribes *and* identifies languages runs ASR here, so the field is not shown for it. Decoder settings that only apply
to ASR are hidden for the other roles.

A voice-activity model is scored one window at a time (Silero VAD is
chunked-unbounded upstream), so a long file does not have to fit in memory; the
Window setting is that window. Each window is scored independently, so the segment
boundaries at a seam can differ slightly from a single pass — the speech found is
the same. A diarizer is run whole — its speaker ids are per run, so splitting would
renumber them at the seam.

A 16 kHz mono 16-bit WAV is read directly. Any other format (`mp3`, `flac`, `ogg`, `m4a`,
`aac`, `opus`, `wma`) and the audio track of a video container (`mp4`, `m4v`, `mkv`, `mov`,
`avi`, `webm`, `3gp`) is decoded with `ffmpeg`, which has to be installed and on `PATH` — see
[Audio input](audio-input.md). The video stream is not selected: the decoder is asked for raw
16 kHz mono PCM, so the picture is ignored. A container with no audio track still fails, with
`ffmpeg`'s error.

| Setting | Default | Notes |
|---|---|---|
| Model | the model last run for this goal, else the first on disk that serves it | The picker lists what is downloaded *and* serves the goal's role; the Models tab fetches and deletes models |
| Language | `en`, then whatever the model allows | Before the model is probed, the picker falls back to ten codes (`en`, `fr`, `de`, `es`, `it`, `pt`, `ru`, `zh`, `ja`, `ko`). Once a model has been probed the list is what *it* declares, with `auto` added when it reports language detection, and `auto` means no language is passed at all so the model decides for itself. If the current choice is not in the new list, the picker takes the first entry instead of keeping an unavailable one. Each entry is drawn with its English name beside the code (`fr — French`), added by the app from a table of the codes the Whisper family uses; the item itself stays the code, and a code the table does not know keeps its bare form rather than being guessed at |
| Threads | library default | Left empty on purpose. This is not a benchmarked recommendation — raising it past the core count usually makes it slower |
| KV type | Auto | `F32` keeps the most precision and the most memory; `F16` halves the cache |
| Context size | model maximum | Lower uses less memory; too low truncates long passages |
| Window | 30 s | Longest audio passed in one call. Longer audio is split into windows overlapping by 1 s and merged; see [Long audio and diarization](diarization.md). The compute buffer grows with this value and the native API does not report its size, so the default is deliberately conservative: 30 s completed a measured Parakeet run on the CPU and on a 4 GB Vulkan device, while sending a long file in one call asked for 10.1 GB on Vulkan and 113.6 GB (108,382 MB) on the CPU |
| Adjust window automatically | on | When the first window does not fit in memory, lower it and retry; the value that worked is remembered for the model, backend and device. It never raises the window on its own, because the native API gives no measurement that a larger one fits |
| Keep special tags | off | Off removes markers such as `<|notimestamps|>` from the text |

Four settings appear only for models that have them: the Whisper initial prompt and
temperature, the task picker (and the target-language picker only while the task is
Translate), the speculative-decoding draft count, and the vocabulary list. They are
hidden based on what the model reports, not on what its name suggests — see
[Asking the model](#asking-the-model) below.

**Vocabulary** is custom terms for the decoder, one per line, in priority order —
names, jargon, acronyms. It is passed as the run's vocabulary list and the field is
shown only while the model reports the vocabulary feature; a downloaded model whose
capabilities could not be read keeps it visible, like every other group. Upstream ignores the list with a native warning on a
model that does not implement it, and this project has not measured how much it changes
a transcript: the field makes no accuracy claim. It applies to offline runs; the CLI has
no flag for it.

**Speaker labels** belong to the **Speech to text + speakers** goal, which is also the on/off
choice for the step: a text-only goal leaves every segment as `Speaker 0`, and the speaker card
is not shown at all. For a model that attributes speakers itself (MOSS,
granite-speech-4.1-2b-plus) the request is passed as `WithDiarize` and the run stays one pass.
For a model that cannot, the speaker card shows a **Diarizer model** picker with the downloaded
models that serve the DIARIZE role (Sortformer), selects the only one automatically, and states
in one line what will run. With no diarizer downloaded the picker is not shown, the run is
refused before it starts, and the **Get a diarizer…** button opens the Models tab already
filtered to the diarization role, rather than starting a run that would silently produce
`Speaker 0`. A model that only diarizes is the **Speakers only** goal: its whole result is
speaker turns. The run button is named for the goal (Transcribe, Diarize, Identify language or
Detect speech), so the action is visible before the run rather than only in the result. See
[Long audio and diarization](diarization.md).

**Cancel** stops the run in flight. The native abort is polled between decode steps, so it is
not instant, and the wrapper turns a native abort into cancellation *before* it reads the
partial result — so the partial transcript is discarded rather than shown, and the status line
says exactly that. A cancelled run is not a failure and is not written to the log.

While a run is in flight, a **live text** panel shows the text of the windows finished so far.
It updates once per window — the Window setting decides how often — not word by word: the
offline run offers no finer callback. A file shorter than one window shows no preview; the
result appears when the run ends. The panel is a preview, not a saved result: it is cleared when
the run finishes and the Text tab takes over.

If a run fails, the status names the failure and the tab offers **Copy error** for the full
report; see [When something fails](#when-something-fails).

**Before a run, memory.** The tab states two facts, and only two. If the pinned model file is
larger than the free memory of the device the run resolves to, the service refuses before
loading it and names both figures — the weights alone would not fit. And if a previous run of
the same model, window, backend and device recorded a lower free figure than the device has now,
the tab warns with that measurement. The second is a measurement from this machine, not an
estimate: the native API exposes free memory but not the compute graph's size, so a run whose
weights fit can still run out of memory. That is why the Window default is 30 s rather than a
large value: the graph grows with the window, and 30 s is the value this project measured
completing on both the CPU and a 4 GB Vulkan device. Raising it is a choice the caller makes,
with **Test memory** to measure it.

**Adjust window automatically** (on by default) turns the first out-of-memory failure into a
retry at half the window, down to 5 s and at most ten values. The progress line says the window
was lowered while the retry runs, and the status at the end names both values. The value that
worked is remembered for that model, backend and device, so the next run starts there instead of
trying the value that failed. Only the first window can trigger this: once a window has produced
text, re-running the audio would duplicate it, so a later failure is reported as it is. The
service never raises the window on its own — the native API does not say how large a window a
device can take, so raising stays the reader's choice. The error report names Window, the
quantization and the device as the levers.

**Test memory** runs one window of the selected file with the selected settings, without
producing a transcript, and reports the lowest free figure the device reached. It measures the
real allocation that fails in an `ErrOom` instead of estimating it, and it costs one window of
compute. It does not test later windows, the diarizer, or the decoder's output length — the
button's tooltip and the result line say so. A successful probe is stored like a finished run, so
the next warning uses its measurement. The button is offered only for a model that serves ASR:
the probe runs one ASR window, and the DIARIZE, LANGID and VAD roles have no such session to
probe.

**While a run is in flight, the progress block says what the bar is counting.** It appears in
the run card, directly under the button that started it, so a run never begins with the
feedback below the fold; the status line takes over again with the completion or the failure.
The phases are the ones the service can actually observe, and the block distinguishes them
instead of leaving a bar at zero:

| Phase | Bar | The line above it |
|---|---|---|
| Loading the model | Indeterminate | `Loading whisper-tiny…` — the native library reports no progress for a load |
| Reading the audio | Indeterminate | `Reading the audio…` — the WAV read or ffmpeg decode reports no fraction |
| Offline ASR, several windows | Determinate | `Transcribing — window 3 of 12`, from the planner's own window list |
| Offline ASR, one window | Indeterminate | `Transcribing — window 1 of 1`, with a note that the native call reports no fraction inside a window |
| VAD | Indeterminate, then determinate | The action's label, then the per-window fraction once the first window returns |
| Chained diarizer | Indeterminate | `Diarizing — attributing speakers` — the transcript is complete; the diarizer runs the whole file in one native call and adds speaker turns |
| One-call role (LANGID, DIARIZE) | Indeterminate | The action's label, e.g. `Identify language…` |

The percentage is hidden while the bar is indeterminate, so it never sits at 95% during a second
pass that has no measurable fraction. An elapsed clock (`elapsed 02:14`) is measured from the
start of the run, and after a second finished window the card adds an estimate
(`about 03:40 left (estimate)`) computed from the windows measured so far. The window count is
the number of native calls the run will make, not an estimate of time. VAD is windowed but
reports no step, so it starts indeterminate and fills the bar from the first finished window.

**The settings are locked while a run or a probe is active.** A run works from the values it
started with, so an edit during it would not change what the run does — it would only change what
the reader thinks it did. The error report and the saved provenance describe that snapshot, not
the pickers at the moment a failure was caught.

For a model that transcribes and **cannot** diarize itself, the speaker card reveals a
**Diarizer model** picker: it offers the downloaded models that serve only the DIARIZE role
(Sortformer). With one chosen, the tab runs the transcriber, then the diarizer over the same
audio, and gives each segment the speaker whose turn overlaps it most. The two run one after the
other, so only one model is in memory at a time. The join is this app's, not the library's, and
it is heuristic — see
[Combining a diarizer with a transcriber](diarization.md#combining-a-diarizer-with-a-transcriber).
The goal is offered only when the transcriber reports segment timings; the note in its place
explains the absence when it does not. Under the goal, the plan line names the two models,
and a warning quotes the diarizer's declared languages when the chosen language is not among
them (Sortformer declares English only). During the second pass the progress block says
**Diarizing — attributing speakers** and the bar is indeterminate, because the diarizer reports
no finer progress.

The result is shown four ways: as text, as segments with timings, as words with timings, and
as speaker-attributed segments. `Export` writes `.txt`, `.vtt`, `.html`, `.md`, `.json`, `.srt` or `.ass`, picked by the
extension you type; the formats themselves are the same as
[the CLI's](exporting.md) — the HTML document is produced by the same shared
renderer. The app now records the audio file's length from the samples it read, so the HTML
`audio:` line is the real duration; a run restored from an older history entry that did not
record it says `audio: unknown` rather than passing the last segment's end for it. The Markdown
export is the app's own: a header, then one paragraph per segment with the speaker in bold and
the timestamp in code, and the pause markers when they are on. The Text tab is a read-only text
box: a passage can be selected and copied, **Find next** moves through the matches of the search
box, and **Copy** writes the whole result to the clipboard without going through a file.
**Copy + time** writes one `[hh:mm:ss] Speaker: text` line per segment, with the names you gave
the speakers.

Each row of the Segments grid has a **play** button: it plays that segment's audio with
`ffplay`, which ships with ffmpeg but is a separate executable. If it is not on `PATH`, the
status line says so. This is not an editor: playback does not follow the cursor, and the text
cannot be changed.

An audio or video file can also be dropped onto the tab; the drop and the **Browse…** dialog
accept the same extensions, and a drop of anything else leaves the current path alone.

Every file dialog in the app opens in the folder the last one used, and picking or exporting a
file records its folder in `ui-settings.json` beside the log. A folder that has since been
removed is ignored for that launch and the dialog falls back to its own default; the path stays
in the file, so a drive that comes back is still the last one used.

Above the result tabs, beside **Export**, sit three export options that change the
exported document and nothing else:

| Setting | Default | What it does |
|---|---|---|
| **Timestamps every** | 60 s | Longest gap between two inline timestamps in the HTML and Markdown exports. The first segment and every speaker change are always stamped; `0` leaves only those |
| **Mark pauses** | off | Writes a pause marker between two segments separated by silence, one dot per whole second, capped at ten. The threshold beside it is the shortest gap that counts. Only the silence *between* recognized segments is visible to it |
| **Names** (Speakers tab) | empty | One field per speaker id, shown when the result has more than one speaker or speaker turns. A name replaces `Speaker N` in the HTML and WebVTT exports; a blank field keeps `Speaker N` |

The three are remembered between launches, in `ui-settings.json` beside the log;
a value outside the range the controls allow is read back as its default rather
than passed to the export.

None of the three rewrites the transcript: the Text tab still shows the model's
text as it came back, and the same result can be exported twice with different
options. The pause markers and names are not carried into the `.txt` export, which
has no speaker column to put them in.

### Streaming

![The Streaming tab.]({{ '/assets/images/ui-streaming.png' | relative_url }})

Transcribes the microphone live, at 16 kHz mono 16-bit PCM. The audio is captured by
`ffmpeg`, which must be on `PATH` — the same requirement as decoding a non-WAV file. It opens
the system input on Linux (PulseAudio) and macOS (AVFoundation); Windows DirectShow has no
default input, so set `TRANSCRIBE_MIC_DEVICE` to a device name
(`ffmpeg -f dshow -list_devices true -i ""` lists them). `TRANSCRIBE_MIC_FORMAT` and
`TRANSCRIBE_MIC_DEVICE` override the default on every platform.

The two text areas are the point of the layout. **Committed** text, on the left, is final.
**Tentative** text, on the right and in italics, is what the model would say so far and may
still revise. Which side text lands on depends on the commit policy: `Auto` lets the model
decide, `Finalize` commits only when the model calls a segment final (later but steadier
text), and `Stable Prefix` commits once a prefix has stopped changing (sooner text that
can still be revised). The Stable Prefix agreement count is how many unchanged passes a
prefix needs before it is committed.

Both text areas are selectable: drag for a range, double-click for a word, and the right-click
Copy item or Ctrl+C copies the selection. `Clear`, beside Stop, empties both. During a run it
does not reset the native stream — committed text is append-only — so it hides what was shown
up to the click and displays what comes after it; the model keeps the audio context it has
already captured. After a run, Clear just empties the two panels.

Family extensions appear only for a model that both streams and has the matching extension
builder: a decode interval for Moonshine, buffered-streaming context and attention for
Parakeet, delay tokens and a decode interval for Voxtral.

An empty extension field is not "off": the extension is left off the run entirely, so the
model's own default applies — which is also what the native default, the `-1` sentinel the
`*_ext_init` functions set, means. Each field shows `(model default)` as its placeholder,
`Reset` clears them all (enabled only when something is set), and the tooltips say what each
knob does and what the default is. For Parakeet buffered context that default is a tuple from
the model's training menu — 5600 / 1040 / 1040 ms for parakeet-unified-en-0.6b — and a
positive value must be an exact multiple of the 80 ms encoder frame. For Voxtral the delay
defaults to 6 tokens, and only 1 to 15 or 30 is accepted. The values the library actually
initializes are exposed by the wrapper's builders and pinned by a test, rather than written
into this page.

**Utterance (s)** closes the native stream and starts a new one on the same model after that
many seconds of audio, so the memory the stream holds does not grow with how long you stream.
The streaming API works on one utterance at a time and keeps that utterance's working set,
which for a model that re-decodes from the start on every feed grows with its audio — measured
at roughly 2.8 MiB per second of audio. The committed text of each closed utterance is kept and
prepended to the next, so nothing already transcribed disappears, but the model loses its audio
context across the boundary and a word split across it can be broken. The default is 30 s; `0`
keeps one utterance for the whole session, which is the behaviour before this field existed.
The numbers, the controls behind them, and what is still unverified are on
[Streaming benchmarks](streaming-bench.md#4-memory-grows-with-the-stream-and-ending-the-utterance-bounds-it).

Start is enabled only for a model that can stream. The picker offers only the downloaded
models upstream records as able to stream — the same pinned-catalog source the Models tab's
capability filter reads. MOSS, the default model, is not one of them: it declares
`streaming.supported = false`, the native library refuses `StreamBegin` with
`ErrNotImplemented`, and the two agree. With only MOSS on disk the picker is empty and the
line under it points at the Models tab rather than offering a run that fails. A model upstream
says nothing about is still offered — unknown is not a no — and Start then falls back to
upstream's record until this machine has loaded the model and answered for itself.

**Upstream and this machine can disagree, and the machine wins.** A Sortformer diarizer is
recorded upstream as `streaming.supported = true`, but that is its streaming *diarization*
API; this tab runs the ASR stream, and the loaded model reports it has none. Rather than keep
such a model in the picker under a "does not support streaming" warning, the tab leaves it out
once it has been checked — the same way it leaves out a model upstream itself records as unable
to stream — and selects the next streaming model. This is why the tab can offer a model whose
name says "streaming" only after it has actually loaded it and confirmed the ASR stream is
there.

Read [Real-time streaming](streaming.md) and [Streaming benchmarks](streaming-bench.md)
before relying on this tab. In short, and unchanged by the GUI: the model default does not
keep up with live audio on the CPU backend, setting a decode interval or the commit policy is
not optional for a live pipeline, and on the CPU backend a stream of roughly 88 s of audio
**aborts the process inside ggml**. That abort is native, so no `try`/`catch` in the app
intercepts it and no status line will ever report it. It is upstream behaviour in
transcribe.cpp, not a bug in this app. **Utterance (s)**, above, is the one mitigation the tab
offers: the default 30 s closes the stream before a single utterance reaches the abort or
grows the memory further, at a cost to the model's context across the boundary.

### Batch

![The Batch tab.]({{ '/assets/images/ui-batch.png' | relative_url }})

Queue several files and run them one after another against a single loaded model.

Sequential is not an oversight: the library is
[not thread-safe](concurrency.md#thread-safety), so one model at a time is what is safe.
Results land in a grid with the detected language, the status and the text, and export as
`.txt` or `.json`.

While the queue runs, the progress card names the file in flight and its position — `File 2 of
5` above the file name and its state (reading, done, failed). The bar counts finished files, so
it moves once per file. That is the finest step the batch can state: each file is one native
call, not the windowed path the Transcription tab uses, so there is no percentage inside a
file to show.

**Cancel** stops the batch. The file in flight is dropped — the same partial-result rule as the
Transcription tab — while the items that already finished stay in the grid; the status line
counts how many of how many completed. A batch cancelled before it started does not resolve or
download the model first.

### History

![The History tab.]({{ '/assets/images/ui-history.png' | relative_url }})

Every finished transcription is saved on this machine, so closing the app does not lose it.
The file holds the transcript and the few facts that name it — audio path, model, language,
requested diarization, window, date — under `<cache>/../history`, beside the log and the
settings. It is self-contained: an entry opens after the audio file has moved or been deleted.

The tab lists the entries newest first with the date, audio name, model, language, length and
segment count, with **Open**, **Delete** and **Refresh**. **Open**
loads a saved transcript into the Transcription tab, where it can be exported again; nothing is
re-transcribed and nothing is saved twice. The export then names the model and settings recorded
with that run, not whatever the pickers happen to say now — an old transcript exported after
switching models still describes the run that produced it. A file that cannot be parsed is left
out of the list rather than failing the tab. Nothing is pruned automatically — entries stay until
they are deleted — and the tab shows the folder path so it can be managed outside the app.

What it does **not** save: a cancelled run (its partial result is discarded), a failed run, or a
batch's per-file results. Only a finished run from the Transcription tab is written. If the
write fails, the run still counts as successful and the status line says the history could not
be saved, rather than hiding it.

### Models

![The Models tab.]({{ '/assets/images/ui-models.png' | relative_url }})

The 76 curated aliases from the same manifest the CLI resolves against. All of them together
come to 58 GiB, which is why the grid starts empty of weights and shows what is on disk.

- **Filters.** Free text over the alias, the family read from the repository name, the
  repository and the licence; plus *on disk only*, *commercial licences only*, a size
  filter, and a licence filter. The commercial filter is a text test on the SPDX identifier
  in the manifest, so read the licence itself before relying on it — `other` means the
  manifest names no standard licence.
- **Per row:** download, measure, delete. A download is verified against the sha256 in the
  manifest before it is cached, at the pinned revision.
- **A download can be cancelled.** *Cancel download*, beside *Refresh*, stops the transfer in
  flight. `ModelStore` deletes its `.part` file, so a cancelled download leaves nothing
  truncated in the cache, and the row can be started again. The button is enabled only while a
  download is running; the status line says *cancelled*, not *failed*.
- **The default model is marked.** The alias the CLI uses when no `--model` is given is
  shown as `default` in the Alias column. It is a mark of which alias a command ends up
  using, not a claim about quality or speed.
- **Sort by clicking a header.** Eight of the nine data columns order the grid — Alias,
  Family, Size, On disk, Licence, Languages, *x realtime* and Diarization — and the arrow
  on the header says which way. Clicking again reverses it. Each orders by the value it
  shows, so what you see in the column is what the order follows. The three sort buttons
  above the grid set the same state, so both routes move the same arrow.
  **The one data column that declines is WER**, deliberately: its 74 figures come from
  three metrics (67 WER, 6 CER, 1 DER), two datasets and eight languages, so an order
  over them would rank numbers that do not measure the same thing. Its tooltip says so
  rather than leaving a silent refusal. `Action` is not data — it holds buttons.
- **Filter by language.** The picker beside the licence and size filters narrows the
  grid to the models whose card declares a language — codes, as the cards state them.
  A model that declares nothing is hidden while a language is selected, because
  unknown is not the same as yes. The free-text search reaches the codes too, so
  typing one filters the same way, and *Clear* turns all of them off at once.
- **Filter by capability.** The picker beside the language filter lists every capability
  transcribe.cpp records — `streaming`, `diarize`, `translate`, `timestamps`,
  `lang_detect`, `batching` and the rest. The names are theirs, taken from their catalog
  rather than written here, so every choice has at least one model behind it; a model with
  no record is hidden while one is set, which is the rule the language filter applies too.
  **It selects one at a time.** For a model that both streams *and* diarizes — there are
  two: `diar_streaming_sortformer_4spk-v2.1` and `multitalker-parakeet-streaming-0.6b-v1` —
  filter by `streaming` and read the Diarization column, which now answers for ten models
  rather than waiting for a download.
- **Filter by role.** The picker beside the capability filter narrows the grid to the role
  transcribe.cpp records: speech to text (the role their catalog leaves implicit), diarization,
  language identification or speech detection. The role also appears under the alias for the
  three non-ASR models, so a diarizer, the language identifier or the VAD model is visible
  without opening the detail pane; ASR is the ordinary case and stays unlabelled. A model with
  no record is hidden while a role is set, because unknown is not the role asked for. The
  detail pane names the role and whose statement it is: the catalog is what the tab can show
  before a download, and the loaded model reports its own role.
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
  With no model selected, the pane carries a line saying to pick one rather than an empty
  half of the window.
- **Licence and languages come from the model card.** The grid's Languages column
  shows the first four codes and counts the rest, because some models declare 99;
  the detail pane below carries the whole list and names where it came from. Every
  entry states its languages except the voice-activity model (`silero-vad-v6.2`),
  which has none — the manifest is filled in by
  [`tools/UpdateModelManifest`](../tools/UpdateModelManifest), whose
  `--languages-only` mode reads `cardData.language` and rewrites nothing else, so
  adding the column cannot re-pin a revision or a checksum.
- **Diarization answers from whichever source can answer.** A model that has been loaded
  on this machine reports for itself, and wins — including when the load fails, in which
  case the cell says `?` rather than letting a catalog entry stand in for a result the
  machine could not produce. Until then the column reads what transcribe.cpp records
  (`capabilities.diarize`), which answers for all 76 models — four `supported`, the rest not;
  the detail pane names the source and says *unverified by them* where they mark it supported
  without claiming to have checked it. *Check diarization* loads only the models whose answer
  could still change: the ones the catalog records as supported — the four it marks
  `verified: false` — and any it records nothing about. It skips the rest, whose answer the
  catalog already gives, and rows already checked this session, and says how many it skipped.
  It asks both the ASR `diarize` feature and the DIARIZE role, so a Sortformer — whose whole
  product is speaker turns — reads *supported*. See
  [Long audio and diarization](diarization.md).
- **On disk is a yes or a no.** The Size column beside it carries the download size and
  the line above the grid carries the total, so the per-row size was saying twice what
  the reader already had and costing the column its width. What the yes means — verified
  against the manifest's sha256 — is in the detail pane, next to the path.
- **A download shows on its own row.** While a model is being fetched, its Download button
  reads "Downloading…" and a progress bar appears beside it in the Action column, and the
  status line names the model and the percentage. The bar at the bottom of the tab shows the
  same progress; the benchmark bar beside it appears only while a model is being timed, not
  during a download, so the two no longer overlap.
- **Error rate is their figure.** One number per model, from transcribe.cpp's own
  measurement, for the quantization this project pins. Hover it for the metric, the
  dataset, the split, the language and their provenance flag — including *engine build not
  recorded upstream*, which several rows honestly carry. The header says **WER** because
  that is the majority of it, but 67 of the 74 figures are WER, six are CER and one is a
  DER, so read the tooltip rather than the column name when it matters. **It is not a
  measurement of your audio, but it is a useful screen from the people who built the
  engine**; the number is theirs. The banner links transcribe.cpp's per-model accuracy
  tables at the pinned revision, and the detail pane links the selected model's Hugging
  Face card, where the published WER per quantization and the full language matrix live.
- **Measure** times a model over an excerpt of audio *you* pick, because a timing is only
  meaningful for the audio it was taken on. One pass is discarded as warm-up and three are
  timed; the best is kept, since contention only ever makes a run slower. The backend is
  recorded with the result, because timings from different backends are not comparable.
  Results are held in memory for the session and are not written to disk.

`Sort by speed` orders the grid by what you measured here, and models you have not measured
cannot be placed in a speed order, so they stay at the bottom in name order.

**Accuracy figures are upstream's, and they are worth reading.** The WER column and the
detail pane quote transcribe.cpp's headline error rate for each model, on their benchmark.
Hover a cell for the metric, dataset, split, language and provenance. The column is not
sortable because 67 of the 74 figures are WER, six are CER and one is a DER, over two
datasets and eight languages — an order over them would rank numbers that do not measure the
same thing. Those figures are useful for narrowing the field before you download; they are
not a measurement of your audio. `Measure` answers speed on your audio, and
[Streaming benchmarks](streaming-bench.md) is this project's only benchmark (Moonshine
streaming).

### Settings

![The Settings tab.]({{ '/assets/images/ui-settings.png' | relative_url }})

Backend and device selection, the cache directory, the version of the native library, and
the compute devices this build and this machine can see. The tab ends with **About**: this app's
version (the wrapper's SemVer, read from the assembly), a line naming transcribe.cpp as the
native library and its authors as its authors, and the two repository links. This project binds
and packages that library; it does not implement speech recognition.

`Auto` is the default backend and its behaviour is the one described on
[Compute](compute.md): the GPU is used when one initializes, and a forced backend fails
rather than silently falling back. Pinning a *device* is stricter still — it never falls
back to another device, so a model that will not fit there fails instead of quietly running
elsewhere. The backend and device picked here are what the three transcription tabs use.

**Auto is a device choice too.** The Device list starts with **Auto — let the backend
choose**, so a device that was pinned can be unpinned again; the box used to show that
sentence only as a placeholder, with no row to select once a device had been picked. The
names the Backend picker does show are plain — **Auto**, **CPU**, **CPU (accelerated)**,
**Metal**, **Vulkan**, **CUDA**, **ROCm** — rather than the enum members' `BackendAuto`
spelling.

There is no **Save** button. A change is used by the next run and written to
`ui-settings.json` as it is made: the backend by name, the device by its stable id (the
runtime handle is valid only in the process that enumerated it). On the next launch the saved
device is selected again when it is still enumerated; if it is missing, Auto is selected and
the id stays in the file, so a device that comes back is used again.

The panel is **optional**: leaving both controls alone is the normal case. A line under the
pickers says so and names the backends this build and machine report as available, so the
list does not read as a choice that must be made. `Auto` probing and the exact fallback rules
are on [Compute](compute.md).

**The pickers only offer what can actually run.** The Backend list is built from the native
`BackendAvailable` probe, so a backend this build or machine cannot satisfy — Metal on Linux,
CUDA without a CUDA build, CPU (accelerated) without a registered BLAS/AMX device — is not
offered. If the probe itself fails, the full list is shown and the hint says so rather than
hiding a backend on a failed guess. The Device list is narrowed to the devices the selected
backend accepts: a Vulkan GPU is not offered when the CPU backend is forced, and an ACCEL
device (BLAS/AMX) is never offered because it cannot be the primary. The grid below still
shows every device the machine reports, including ones the selected backend cannot use: it
is an inventory, not a set of choices.

A line states which device the current choice resolves to and how much memory it has
free — `Auto` follows the native policy (discrete GPU before integrated, then CPU), a pinned
device is named as pinned. The devices grid shows **Total** and **Free** per device; a dash means
the device did not report the figure. Both are snapshots as of the last **Refresh devices**
click, not a live monitor: re-read them after closing another GPU application.

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
- **The run asks for AUTO timestamp granularity**, not a fixed SEGMENT. AUTO is the one value
  the native dispatcher never rejects; the model resolves it to its own maximum. A fixed
  SEGMENT failed on granite-speech-4.1-2b-plus, which reports WORD and still rejects SEGMENT
  with `ErrUnsupportedTimestamps`.

The language picker works the same way: ten codes stand in while nothing has been probed,
and a probed model replaces that list with the languages it actually reports. The UI used to
offer languages a model could not handle, and no way to ask for auto-detection at all.

Capabilities are cached per alias for the session, in a single cache shared by the tabs. The
transcription and streaming tabs each read the default model when they are built, so without
the sharing they loaded the same weights twice at once — two copies of the file in memory,
and on a machine already short of RAM that was enough for the OOM killer. A caller that asks
while a load is in progress waits for it instead of starting another.

## When something fails

A failure the app catches is shown as a selectable line of status text and also appended, in
full — exception type, message, inner exceptions and stack trace — to
`<cache>/../logs/ui.log`, beside the `models` directory the Settings tab already names. The
Transcription tab keeps the last failure and offers **Copy error**, which puts a report on the
clipboard with the exception type, the message, the native status and failed method when there
is one, the log path, and the stack trace — so a failure can be pasted into a bug report instead
of retyped. A message that reports a failure also names the log path. The file is appended to
and never rotated. Not every failure in the app goes through it — only the ones the view models
catch and report. One that deliberately does not: a model deleted while the Streaming tab was
reading its capabilities leaves the picker without a log entry, because a file the reader has
already removed is not a capability failure. A model that is present but cannot be loaded is
logged as usual.

An out-of-memory run (`ErrOom`) is named as such, with the next step that can help — a smaller
**Window** (the most direct knob: it caps the audio and compute buffer of one native call), a
smaller quantization or context size, or the CPU backend — rather than only the native sentence.
The copied report also records the run's model, window, KV type, context size, backend, device
and diarizer, because a native status without them is a dead end. For an `ErrOom` it adds what the
service saw when the call failed: the device the model actually ran on and its free memory at
that point, the session limits (`n_ctx`, KV bytes, max audio), and which window of how many was
running. That is what tells a first-window allocation apart from a later growth. None of those
settings is changed automatically: the app does not retry with different values, since a silent
retry would hide the cost and report a result for a configuration the reader did not choose.

## Look and theme

The window follows the operating system's light or dark setting: it sets
`RequestedThemeVariant="Default"` and pins no theme of its own. The colours it uses are
defined once per variant, so changing the system theme changes the app with it rather than
leaving a light panel on a dark window.

What the views share lives in two files under `src/TranscribeCppSharp.UI/Styles`:

- **`Tokens.axaml`** holds the numbers and the semantic colours — a spacing scale, two
  corner radii, four font sizes, three icon sizes, and brushes for helper text, cards, the
  tentative streaming panel, warnings and the page-icon accent. Each brush is defined for
  Light, Dark and Default, which is what makes the app theme-aware.
- **`Controls.axaml`** turns them into classes the views apply by name: `section` for a
  heading, `label` for a field caption, `hint` for muted helper text, `danger` for a
  warning, `card` and `tentative` for panels, `accent` for the one action that starts work
  on a tab, and `pageTitle`, `pageDescription` and `pageIcon` for the heading at the top of
  a tab. A button with an icon puts both in a `buttonContent` panel, so the icon and the
  label are one line rather than two content items.

The icons are a separate MIT-licensed package, `Material.Icons.Avalonia`; they are vector
paths, so there are no icon image assets to keep in sync. It is presentation only. No token
or class changes what the app does, and none carries a claim about it.

## What the app does not do

- **It ships unsigned.** Each release attaches a self-contained archive per platform, but
  nothing is code-signed and there is no installer, as above.
- **It is not a second transcription implementation.** It is a front end over the wrapper, so
  it has exactly the wrapper's behaviour and the wrapper's limits. Where the app and the
  documentation disagree, the documentation is describing the code and the app is the thing
  that is wrong.
- **It cannot attribute speakers while streaming.** `transcribe_stream_params` has no
  `diarize` field in transcribe.cpp v0.4.0, so the streaming API cannot request it
  ([Diarization](diarization.md#diarization-models-and-the-diarize-role)). The Speakers tab belongs to the
  Transcription tab, which runs offline.
- **It does not time models for you against published numbers.** `Measure` runs on your
  machine, over your audio.
- **It does not invent an accuracy comparison of its own.** Where a figure exists, the grid
  quotes upstream's error rate per model — their measurement on their benchmark, with metric,
  dataset and language in the tooltip — which is useful for narrowing the field but is not one
  sortable ranking. What this project's own judgement adds is filtering by licence, family,
  size, language, capability and what is on disk.

## How it is tested

Headless Avalonia tests in `tests/TranscribeCppSharp.UI.Tests`, run by the same
`./scripts/run-integration-tests.sh` as everything else, on all three CI platforms. They
build the real `App` on Avalonia's headless backend, so the views load with the styles they
get on screen.

What they cover is worth stating, because it is narrow: the automation ids on all six views,
which view models hide options a model does not support, the defaults, and the catalogue
item's display states. The bindings inside a `DataTemplate` are verified by realizing the
rows and reading the values back — a property with no binding fails silently, which is how
the default-model mark stayed missing from the grid while its property existed. One test
also arranges the Transcription tab with a result and a model that declares a vocabulary, and
checks that the vocabulary field and the export options are visible and do not overlap the rows
around them — an id existing is not the same as a control being on screen.

Two things they do **not** cover. The accessible names set in the XAML are not asserted by
any test — only the ids are, and a name is not an id. And no test drives the window
interactively end to end, or asserts what the app looks like.

The pictures above are not a screenshot test either. They come from
[`samples/UIScreenshot`](../samples/UIScreenshot), which runs the real window through
Avalonia's headless platform and saves whatever it draws; it is a generator, run by hand,
not a check that fails when a pixel moves. Nothing in CI compares them against anything,
so a visual regression passes unnoticed — the automation ids are what CI actually pins down.