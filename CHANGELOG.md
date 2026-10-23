# Changelog

All notable changes to this project are documented in this file.

The versioning scheme is described in the [README](README.md#versioning--compatibility):
the wrapper (`TranscribeCppSharp`) follows SemVer for its own C# API, while
`TranscribeCppSharp.Interop` and `TranscribeCppSharp.Native.*` track the upstream
[transcribe.cpp](https://github.com/handy-computer/transcribe.cpp) version they bind to.

## Unreleased

### Server

#### OpenAI-compatible HTTP server

`src/TranscribeCppSharp.Server` serves one loaded model over HTTP using OpenAI's
audio routes (`/v1/audio/transcriptions`, `/v1/audio/translations`, `/v1/models`,
`/health`). It is a thin layer over the wrapper — windowing reuses the CLI's
`WindowPlanner`/`TranscriptMerger` — and adds no native call of its own.

Limits that shape what it is good for: one request at a time (a long file blocks
the rest); `response_format` is `json`, `text`, `verbose_json`, `srt` or `vtt`;
`prompt` and a non-zero `temperature` are refused with a 400 naming the field;
`verbose_json` carries only `task`, `language`, `duration`, `text`, `segments`
(with `speaker` when attribution ran) and `words` (only when the model reports
word timestamps) — no `avg_logprob`, `compression_ratio`, `no_speech_prob` or
token arrays. Downloads are opt-in (`--allow-download`), auth is optional
(`--api-key` or `TRANSCRIBE_SERVER_API_KEY`), and it binds loopback by default.
There is no window fallback: `--chunk` must be chosen for the device. The
endpoint contract is covered by tests driving the real Kestrel pipeline with a
fake backend. A manual run against `ggml-tiny.bin` + `jfk.wav` returned the
expected transcript in `json`, `text` and `verbose_json`. The server is not
packaged and not wired into the release workflow.

Related server behaviour: an upload over the configured limit answers `413
payload_too_large` naming `--max-upload-mb` (verified against a real server
with `--max-upload-mb 1` and a 1.4 MB file); `response_format=srt|vtt` is
accepted (`vtt` as `text/vtt`, `srt` as `application/x-subrip`); `--diarizer
<model>` runs a DIARIZE-role model (Sortformer) over the whole file after the
transcript and joins its turns with `SpeakerAssignment` (word-level when
timestamps exist, segment-level otherwise) — without the flag, `--diarize`
keeps asking the ASR model. Both models stay loaded and the diarizer holds the
whole file in memory (no streaming diarization upstream; speaker ids are per
run). Off by default; `diarize=false` never runs the pass. Verified: options
parsing, the endpoint contract, and a real run with ggml-tiny + Sortformer on
`jfk.wav` (4 turns, segment assigned speaker 1; that clip has one speaker and
is too short for a quality claim, so the run verifies the plumbing, not the
attribution). The word-level branch was not exercised end to end (no local
model reports word timestamps); covered by the `SpeakerAssignment` tests only.
On `ErrOom`, the server halves the window and restarts the file, down to 5 s,
for at most ten values, logging each retry to stderr; the failed attempt's
merged transcript is discarded, so the output cannot contain duplicates. Only
`ErrOom` is retried; any other native failure is a 500. **Not verified: a real
out-of-memory failure** (no GPU on the local machine to force one).

#### Seam deduplication: a tolerance, not a full dedup

Found while testing the server on a multi-window file (`--chunk 5` on the 11 s
`jfk.wav`): the merged text read `... ask not! not what ...`. A segment that
starts slightly before the previous one ended is kept rather than dropped,
because dropping it would lose the audio after the seam; up to 500 ms can
therefore be transcribed twice. That is `TranscriptMerger`'s existing, tested
tradeoff, not a regression — the docs said "deduplicated" without the tolerance
and now say what actually happens. `docs/server.md` and `docs/diarization.md`
carry the measured example.

### Model roles (upstream v0.4)

The pinned upstream version moves from v0.3.0 to v0.4.0. Roles are the central
change: a model serves one or more of ASR, DIARIZE, LANGID and VAD, fixed at
load. Wrapper additions: `TranscribeRole` and `Model.Roles`; new `LangIdSession`
and `VadSession` (`Model.CreateLangIdSession` / `CreateVadSession`) with their
builders and result records; a new offline `DiarizeSession` (with
`SortformerDiarizeExtBuilder`) replacing the removed
`SortformerStreamExtBuilder`/`StreamParamsBuilder.WithSortformerExt`.
Capabilities are now ASR-only: `Model.GetCapabilities` throws `UNSUPPORTED_ROLE`
on a model without the ASR role; the UI capability probe reads `Roles` first.
Backend allowlist: `BackendMask`, `Backends.InitDefault`,
`Backends.Init(dir, BackendMask)`, `Backends.AllowedBackends`. New status codes
20 (`UNSUPPORTED_ROLE`) and 21 (`INPUT_TOO_SHORT`), flowing through
`TranscribeException`. The native ABI change is additive at the symbol level
(no function removed, no existing enum value changed), with one removal
(`transcribe_sortformer_stream_ext` and `SortformerStreamExt`). Catalog re-pinned
to v0.4.0: 76 records (4 new: `parakeet-ultra`, `orukeet`,
`silero-vad-v6.2`, `lang-id-voxlingua107-ecapa`). Behaviour changes needing no
wrapper work but worth knowing: non-finite audio (NaN/±Inf) is rejected with
`INVALID_ARG`; a stream whose family hook throws now ends `FAILED`; host
thread-pool failures return an error instead of terminating the process.
Tooling: `scripts/regenerate-bindings.sh` now vendors and parses
`include/transcribe/*.h` as well as the root header; the generator maps a
pointer return to `IntPtr`; `FetchNative --update-hashes` merges into
`build/native-sha256.json`. Verified: full solution builds; 544 Interop and 79
UI tests pass against the 0.4.0 native library. The three new sessions are
exercised end to end against the real weights. New page:
[docs/roles.md](docs/roles.md).

The four new role models are now aliases, each pinned to a HuggingFace revision
with its sha256: `parakeet-ultra` (ASR, 25 European languages, Q5_K_M, 524 MB),
`orukeet` (ASR, 25 European languages, Q5_K_M, 523 MB),
`lang-id-voxlingua107-ecapa` (LANGID, Q8_0, 25 MB) and `silero-vad-v6.2`
(VAD, F32, 1.2 MB). The manifest goes from 72 to 76 aliases. Two detail-pane
rules were relaxed for models upstream records differently: a language model's
headline is an accuracy (on FLEURS), now shown; and a VAD model has no language
and no accuracy, so an explicitly empty language list is allowed (a *missing*
field is still an error) and a blank accuracy line is the record, not a broken
reader. The WER column stays an error-rate column: a language model shows a
dash there.

The CLI and the desktop app follow the model's role. The 0.4 bind left the front
ends behind: both only ever created an ASR session, so a Sortformer crashed the
CLI with an unhandled `UNSUPPORTED_ROLE`, and a langid/vad model could not be
run at all. The CLI reads `Model.Roles` and dispatches: an ASR model keeps the
transcription path; a diarizer prints speaker turns; a language model prints the
ranked languages; a VAD model prints the speech segments. A `roles :` line names
what the model serves. New `--allowed en,fr,de` restricts a language model, and
`--out` writes a plain or JSON report for the non-ASR roles (`--format vtt` is
refused with an explanation, since a WebVTT cue is an ASR shape). The desktop
app runs the model's role and shows the result, with a `runs:` line under the
picker, a **Languages** grid for a language model and a **Speech** grid for a
VAD model (a diarizer fills the existing Speakers grid). A language model also
gets an **Allowed languages** field. The Models tab's *Check diarization* now
asks the DIARIZE role as well as the ASR feature, so a Sortformer reads
*supported*. Long-form audio, honestly: VAD is `chunked-unbounded` upstream, so
the CLI and the app score it window by window and union the intervals across the
seams (memory is one window, not the file; `--chunk` sets the window). Each
window is scored independently, so a seam can bridge a short silence a one-pass
run keeps as a gap: the speech found and its total duration match, the exact
boundaries at a seam do not. Language identification is a 30 s hard cap, so
the head of the file is used. Diarization is a hard cap with no streaming entry
point and per-run speaker ids, so it is run whole and not split — windowing it
would renumber speakers at every seam. Verified: `RoleCommandTests` (CLI),
`VadWindowingTests` (merge rules and a windowed run on ~138 s of synthesized
audio) and `TranscriptionServiceRoleTests` (service) against the three role
weights.

### Transcription

#### Windows, timestamps and memory

The Window setting now actually splits the audio: `TranscriptionService.RunAsr`
passed `WindowSeconds` to `WindowPlanner.TryPlan`, which takes milliseconds.
Every value below 1000 therefore meant a window shorter than the 1 s overlap:
planning failed and the run sent the whole file to the model in one native call.
The 10.1 GB Vulkan and 113.6 GB CPU allocations in the reported Parakeet
failure were the whole file, not a 300 s window. The service now converts
seconds to milliseconds. Measured after the fix: 300 s of audio at 75 s planned
five windows (T_enc=938 each), at 300 s it planned one, and a 20-minute file
requested at 1200 s fell back to 300 s and ran five windows. The memory probe
already used seconds and is unchanged.

The Window default is 30 s, not 300 s: the 300 s default was a generic value,
not a measurement, and the setting was not being applied at all (see above). A
30 s default is conservative: the compute buffer grows with the window and the
native API does not report its size, so the tab starts at the value measured
completing on both the CPU and a 4 GB Vulkan device. The field's hint and
tooltip say so, and the UI docs call the value a default rather than a model
limit. Higher windows are still available for devices that measured them; **Test
memory** remains the measurement. The CLI default is unchanged (300 s,
`--chunk`).

**Adjust window automatically** (on by default) makes the first out-of-memory
failure a retry instead of a dead end: the service halves the window, down to 5 s
and at most ten values, recreates the session and starts the audio over. The
progress line says the window was lowered while the retry runs, and the final
status names both values. The value that worked is remembered per model, backend
and device in `ui-settings.json`, so the next run starts there. Only the first
window can trigger the retry: re-running after a later window would duplicate
the text already produced, so that failure is reported. The service never raises
the window on its own; the native API gives no measurement that a larger one
fits. **Not verified: a real out-of-memory failure** — the retry path is covered
by a fake attempt, not by a native OOM.

**Test memory**: the Transcription tab offers **Test memory**: it runs at most
one window of the selected file with the selected settings, without producing a
transcript, and reports the lowest free figure the device reached. That is the
real allocation that fails in an `ErrOom`, measured rather than estimated, at
the cost of one window of compute. It does not test later windows, the
diarizer, or the decoder's output length, and the result line says so. An
`ErrOom` names Window, the quantization and the CPU backend; a successful probe
is stored like a finished run, so the next pre-run warning uses its
measurement.

What can be said about memory before a run: the Settings tab now shows **Total**
and **Free** per device and names the device the current backend/device choice
resolves to, with its live free figure; **Refresh devices** re-reads both,
because the native figure is a snapshot, not a monitor. The Transcription tab
can state two facts before a run: the pinned model file does not fit in the
target's free memory (the service then refuses before loading it, naming both
figures), or a previous run of the same model, window, backend and device
recorded a lower free figure than the device has now. The second is a measured
warning, not an estimate. `transcribe_device_info.memory_free` is read after the
model loads and after each window; the lowest figure is stored per run
configuration in `ui-settings.json`. The compute graph's size is not exposed by
the native API, so a run whose weights fit can still run out of memory; the
page says so, and nothing retries automatically beyond the first OOM nor raises
the window on its own.

An `ErrOom` report that names the run: the out-of-memory status now names the
model and the window in use, and puts lowering **Window** first: it caps the
audio and compute buffer of one native call, which is the most direct knob
before quantization and context size. The copied error report also records the
run's model, window, KV type, context size, backend, device and diarizer.
Nothing is retried automatically: a silent retry would report a result for
settings the reader did not choose. (The automatic retry of the first OOM
described above is the only exception, and it applies only to the first window.)

An `ErrOom` report that says which device and which window failed: the copied
error report already carried the run's settings. For an `ErrOom` it now also
carries what the service saw at the failure: the device the model actually ran
on with its live free memory, the session limits (`n_ctx`, KV bytes, max audio),
and which window of how many was running. That distinguishes a first-window
allocation (configuration or transient pressure) from a later-window growth (a
long file), which the settings alone cannot. `DescribeRun` re-reads the devices
when the report is written, so the memory line says what the machine looked like
then rather than what the last Settings refresh said.

A decoder stop keeps its partial transcript: `transcribe_run` returns
`ErrOutputTruncated` (18) and `ErrOutputRepetition` (19) with the partial
transcript intact and `was_truncated` set; the native header calls them
result-bearing. `Session.Run` threw on both, so the UI threw away a transcript
that existed and reported a failed run. The wrapper now returns those two
statuses as results, `Transcript` exposes the status, and the tab says the
transcript stopped early and why instead of calling it complete. The batch labels
the item (truncated or output repetition) instead of failing it. Every other
non-OK status still throws.

The model resolves the timestamp granularity: the tab asked every model for
SEGMENT timestamps. granite-speech-4.1-2b-plus reports a maximum of WORD
(numerically finer) and still rejects an explicit SEGMENT with
`ErrUnsupportedTimestamps` (12), so every run of that model failed before
producing text. The run now always asks for AUTO, the one value the dispatcher
never rejects and which the family resolves to its own maximum; the batch path
does the same. Verified through the UI service on the granite model: one 30 s
window completed with 107 characters, where SEGMENT failed.

#### Cancellation and progress

Cancellation of a transcription or a batch: the Transcription and Batch tabs can
now stop a run in flight. The token goes through the service to `Session.Run`,
which already turns the native abort into `OperationCanceledException`. A
cancelled run is not a failure: the status says "cancelled", nothing is written
to the log, and the tab returns to idle. Two limits are stated rather than
hidden. The native abort is polled between decode steps, not inside the encoder,
so stopping is not instant. And the wrapper turns the abort into cancellation
before it reads the result, so the partial transcript is discarded — the
Transcription tab says so. A batch keeps the items that finished before the
token fired (the service returns them and the caller's own token distinguishes
cancelled from complete); the file in flight is dropped. A batch cancelled
before it starts never resolves or downloads the model.

Real progression, with indeterminate phases where nothing is measured: the
progress block now sits in the run card, directly under the button that starts
the run, so the feedback cannot appear below the fold; the status line is hidden
while it is showing and returns with the completion or the failure. It
distinguishes what the service can observe. Loading the model and reading or
decoding the audio are indeterminate, with a line saying the native library
reports no fraction for them. The offline ASR path fills the bar by finished
windows and names them (`Transcribing — window 3 of 12`); a file that fits in
one window is indeterminate with a note saying the native call reports no
fraction inside it, rather than a determinate bar frozen at 0%. A chained
diarizer is indeterminate too, and the card says it runs the whole file in one
native call, instead of holding a percentage at 95%. The percentage is hidden
while the bar is indeterminate. An elapsed clock is measured from the start of
the run, and after a second finished window the card adds an estimate computed
from the windows measured so far, labelled as an estimate. The countable step
and the estimate are not time claims from the project: one is the planner's
window list, the other is arithmetic on the windows already finished.

A countable step beside the progress bar: a run now says what its percentage is
of: `Transcribing — window 3 of 12` above the bar, or `Diarizing — one pass, no
finer progress` during the second pass. The window count is the planner's own,
so it is the number of native calls the run will make, not a time estimate. The
Batch tab names the file in flight and its position (`File 2 of 5`) and its
state (reading, done, failed). Its bar still counts finished files: a batch runs
each file in one native call, so there is no percentage inside one to report,
and none is invented.

Live text while a run is in flight, and a copyable error report: the
Transcription tab shows the text of the windows finished so far while a run is
in progress, cleared when the result takes over. It is one update per window —
the Window setting decides how often — not word by word: the offline run offers
no finer callback, and the panel says so. A file shorter than one window shows
no preview because there is nothing to show before the end.

A failed run now keeps its full report and offers **Copy error**: exception
type, message, the native status and failed method when there is one, the log
path and the stack trace. The status line is selectable too. An `ErrOom`
failure is named as such with the next step that can help — a smaller
quantization, a lower context size, or the CPU backend.

The progress bar shows its percentage: the Transcription tab's progress bar now
carries the percentage it is filling to, beside it. It is a reading of the same
`Progress` value the bar already used, not a second estimate.

#### Error handling and robustness

Several failure paths used to end in an unhandled exception or in lost work
rather than a message: `ui-settings.json` is written to a sibling file and
renamed into place, so a crash mid-write no longer leaves a truncated file that
the next launch reads as "no settings at all"; the History tab returns the
entries it could read when its folder cannot be enumerated, and a delete that
races another delete is not an error; exporting a single transcript or a batch
result reports a write failure in the status line and the log instead of
throwing out of the async command on a read-only, full or vanished disk; a batch
records a file it cannot read as an `Error:` row and continues with the rest
(before, one missing file discarded the items that had finished and skipped
every file after it); a cached weight file whose size disagrees with the
manifest/LFS record is downloaded again instead of being reused as a permanent
cache hit (same-size corruption is still not detected: hashing multi-GB weights
on every resolve would cost more than it prevents); and the CLI catches a
failure that has no specific handler (for example a file that is not a GGUF
reaching `Model.Load`) and prints one line with exit code 1, as its help
promises, instead of a stack trace and a crash.


### Exports

`--format srt` and `--format ass` join `plain`, `vtt`, `json` and `html` in
the CLI. Both are rendered by `src/Shared/SubtitleWriter.cs`, compiled into the
CLI, the server and the desktop app, so the same transcript produces the same
bytes wherever it is exported from. Cues are the model's own segment boundaries
— nothing is re-timed, split or merged — and speaker labels are written only
when attribution was requested, because `Speaker 0:` on every line of a
non-diarized run is noise. The desktop app's export menu now offers `.srt` and
`.ass` alongside `.txt`, `.vtt`, `.json`, `.html` and `.md`, through the same
writer; speaker names typed in the tab replace "Speaker N" in both formats, as
they already did in the WebVTT export. The server accepts
`response_format=srt|vtt` (`vtt` as `text/vtt`, `srt` as
`application/x-subrip`).

`--format html` writes a self-contained document — no external CSS, no
JavaScript, no images — with one table row per segment, numbered from 1, and a
speaker label per row. Timestamps (`[hh:mm:ss]`) appear on the first segment,
on every speaker change, and at most once a minute. The numbers are literal
table cells rather than a CSS counter, so a word processor's HTML import keeps
them. The desktop app's **Export** now offers `.html` as well, through the same
shared renderer. The duration is now recorded by the run, so the app's HTML
export uses the real duration when it is available and shows `audio: unknown`
for older history entries that did not record it. The document records the
source, the duration (CLI only), the model, the language, whether diarization
was requested, the window and the segment count. It records no accuracy figure,
because this project publishes none. `html`, like `vtt`, applies to an ASR
model only; a model serving another role is refused with a message pointing at
`plain` or `json`. The app also exports Markdown (`.md`): a header, then one
paragraph per segment with the speaker in bold and the timestamp in code, using
the same interval and pause settings as the HTML export; characters that would
otherwise be read as markup are escaped. This is a note format for the app, not
a new CLI format.

The three export settings (timestamp interval, pause markers and their
threshold) are now read from `ui-settings.json` on launch and written on every
change, in the same file that already held each tab's last model. A value
outside the range the controls allow is read back as its default rather than
passed to the export, and the two features share the file without erasing each
other. An audio file can be dropped onto the Transcription tab. The drop handler
uses the view model's own extension list, so a drop and the **Browse…** dialog
accept the same files, and a file that is missing or has another extension
leaves the current path alone. The cursor shows a copy effect only over a file
that would be accepted.

Speaker names, pause markers and a configurable timestamp interval in the
export: the Transcription tab's export now reads three settings, all of which
change the exported document and nothing else: **Timestamps every** (default 60 s;
`0` leaves only the first segment and each speaker change stamped), **Mark
pauses** (off by default, with a threshold in seconds; a longer silence between
two segments becomes an unnumbered row with one dot per whole second, capped at
ten), and a **Names** field per speaker in the Speakers tab, which replaces
`Speaker N` in the HTML and WebVTT exports. A blank name keeps the id. The
rows appear only when the result has more than one speaker or speaker turns, and
a name typed before a rerun is kept when the same speaker is still there. None
of the three rewrites the transcript: the Text tab still shows what the model
returned, and the `.txt` export is unchanged. The pause markers describe the
silence *between* recognized segments only; silence inside a segment is not
visible to them. The CLI keeps the previous output — a 60 s interval and no
pause markers — since it has no flag for the new settings.

### Desktop application

#### Goal-driven transcription tab

The Transcription tab now opens with **Goal**: speech to text, speech to text +
speakers, speakers only, identify language or detect speech. It is the outcome,
not a native role: the model picker is filtered to the catalog role the goal
needs (`asr`, `diarize`, `langid`, `vad`), so a diarizer is no longer offered
for a text run and the LANGID/VAD models stop being invisible among the 73
transcribers. If the goal's role has no model on disk, the picker is empty, the
hint names the role, and **Get more…** opens the Models tab with that role
filter already set; the run button says the same thing. A pipeline line states
the actual order — one pass when the model diarizes itself, two when a dedicated
diarizer follows the transcriber.

Speaker labels are now part of the speaker goal, not a separate checkbox that
could contradict it: the speaker card appears only for **Speech to text +
speakers** and is where the diarizer is chosen. The text-only goal has no
speaker card. The goal also hides the controls a role ignores: language,
task/window, decoder tuning and the memory probe are only present for the ASR
goals; the language model keeps its allowed-languages field and the VAD model
its window.

The tab is also laid out as a short ordered form — **Audio**, **Model and
language**, **Run options**, **Speaker labels**, then the primary action — with
the decoder tuning behind one collapsed **Advanced settings** disclosure. A
disabled button is no longer a dead end: the line under it names the missing
piece (no model, no file, a file that has since been deleted, a capability
check still running, or a speaker request that cannot be honoured) and where to
get it. A summary under the model restates the run in plain words. **Test
memory** is offered only for ASR, because the probe runs one ASR window and the
other roles have no session to probe.

Speaker attribution is one checkbox, **Add speaker labels**, instead of the old
*Diarization: model default / off / on* picker plus a separate **Diarizer**
picker. A model that attributes speakers itself satisfies the request in one
pass (`WithDiarize` on); a model that cannot shows a dedicated **Diarizer
model** picker, preselects the only downloaded one, and the plan line names the
two passes. With no diarizer downloaded the run is refused before it starts
rather than returning every segment as `Speaker 0`, and **Get a diarizer…**
opens the Models tab filtered to the diarize capability. The checkbox is not
offered at all for a model that reports no segment timings, and the note in its
place explains why.

This is the recipe layer the existing roles can actually support today. It is
not a free-form step editor: no pre-ASR VAD, no word-level speaker fusion, and
no multi-role combined run are exposed, because the wrapper does not implement
them.

The goal and the opt-in Skip silence choice are now written to
`ui-settings.json`, beside the last-run models, the compute choice and the
export options. Both are restored before the model picker is built, so
reopening the app starts on the role you last chose and with the VAD choice you
left. The VAD alias is remembered too; a model that is no longer on disk falls
back to the first downloaded VAD model, and with none the option stays on and
the run stays refused instead of being silently switched off. An unknown goal
falls back to speech to text.

#### Dedicated diarizer

(See Model roles / Transcription — a model that cannot attribute speakers itself
can be paired with a dedicated DIARIZE-role model. The **Diarizer** picker is
offered only when the transcriber reports at least segment timings; 37 of the 73
ASR aliases declare no timestamps. Under the picker, a plan line names the two
models. A warning quotes the diarizer's catalog record when the chosen language
is not among its declared languages. A chained run is two visible phases: pass
one covers 90% of the bar, the status says **Diarizing…** while the second pass
runs, and the bar holds at 95% until the diarizer returns. The service exposes
`TranscriptionPhase` through the progress callback. The join is this app's, not
the library's — transcribe.cpp has no combined ASR+diarizer entry point — and
it is heuristic; no accuracy claim is made. The word-level join (each word takes
the turn that overlaps it most; the segment takes the speaker of most of its
words) is still a heuristic, not a verified alignment: the words are the
decoder's, the turns are the diarizer's, and the two can disagree. A segment
with no words, or whose words overlap no turn, falls back to the old
segment-level rule, so a model without word timestamps behaves as before.)

A model that can also attribute speakers (MOSS, granite-speech-4.1-2b-plus) can
now be explicitly asked to do so from the app: `DiarizeMode` is no longer
hardcoded to the model default. The tab has a diarization picker (model default /
off / on), shown only for a model that transcribes *and* can diarize. The run
button is also named for the model's role: Transcribe, Diarize, Identify
language or Detect speech.

#### Opt-in VAD trim

(See Transcription — `transcribe <audio> --vad silero-vad-v6.2` runs a VAD model
first and transcribes only the speech intervals, off by default and documented
as unmeasured. Measured on a single clip: a 500 ms merge gap split the clip
into four cues and one decoded as "IS NOT" instead of "ask not"; at 1 s it
merged back into one cue and the sentence was right. Not wired: the server and
the desktop app do not expose `--vad`. The desktop app's Advanced settings of
an ASR goal offer **Skip silence with a VAD model**, off by default, with a VAD
model picker and **Get a VAD model…** opening the Models tab filtered to the
`vad` role. The VAD model is disposed before the ASR model is loaded, so only
one model is in memory at a time. The status after a run names the VAD model
used; the saved history does not carry that field yet.)

#### Models tab

The catalogue already carries upstream's `role` field, but the GUI never read it:
the 73 ASR entries, the dedicated diarizer, the language identifier and the VAD
model looked alike, and nothing said which was which. The role is now read and
shown under the alias for the three non-ASR models — ASR is the ordinary case
and stays unlabelled — and a **Role** filter narrows the grid to speech to text,
diarization, language identification or speech detection. The detail pane names
the role and says it comes from transcribe.cpp's catalog, while the loaded model
reports its own role. This makes the existing LANGID and VAD support
discoverable without adding a model or an algorithm. The role filter uses the
catalog, so it works before a download; a model with no record is hidden while a
role is set, because unknown is not the role asked for.

The grid is filterable by alias, family, size, licence or what is on disk, with
per-row download, measure and delete. It can time a model on audio you choose:
one discarded warm-up and three timed passes, the best kept, in memory for the
session only. Diarization support is a third state — `?` until a model has
actually been loaded and asked.

A download can be cancelled from the Models tab: `ModelStore.Resolve` takes an
optional `CancellationToken`; the token reaches the HuggingFace metadata
lookups and the chunked body copy, so a transfer stops at its next chunk
instead of at the end. The partial file is deleted in the same `finally` that
already cleaned up after a checksum mismatch, so a cancelled download caches
nothing truncated and can be retried. The Models tab adds **Cancel download**,
enabled only while a download runs. It reports *cancelled* rather than *failed*,
and the row returns to its idle state so the same alias can be started again.
This is the first caller that passes a token: the CLI still resolves models
without one and keeps its previous behaviour.

The diarization check skips what the catalog already answers: upstream's
catalog records `capabilities.diarize` for every model — four supported, the
rest not. *Check diarization* loaded each downloaded model regardless, so a
model the catalog already records as "no" was loaded only to re-ask an answered
question. The check now loads only what could change the answer: models the
catalog records as supported — the four it marks `verified: false` — and models
it records nothing about. Rows already checked in the session are skipped too,
and the status says how many were skipped and why.

Download feedback and an empty detail pane on the Models tab: the two progress
bars at the bottom shared one visibility rule, so the empty benchmark bar sat on
top of the download bar for the whole download; each now shows only for its own
action. A downloading row said nothing about itself — the button read "Download"
and only the far-away bar moved — so the row now carries its own state: the
button reads "Downloading…", a bar appears beside it, and the status line names
the model and the percentage. And the detail pane reserved half the window while
nothing was selected, which read as a broken pane; it now shows a line saying to
select a model.

#### Desktop review tools

The result is now something to review, not only to export. Each row of the
Segments grid has a play button: it plays that segment's audio with `ffplay`
(shipped with ffmpeg, but a separate executable; its absence is reported in the
status). Starting another segment stops the first. This is not an editor —
playback does not follow the cursor and the text cannot be changed.

The Text tab is a read-only text box with **Find next** over a search box,
wrapping and case-insensitive; the selection is the match. **Copy + time**
writes one `[hh:mm:ss] Speaker: text` line per segment, using the names given to
the speakers. The service now records the audio file's length in the result, so
the HTML `audio:` line is the real duration instead of `unknown`, and the
History tab shows a **Length** column. A run restored from an older history
entry that did not record it keeps saying `unknown` rather than guessing.

The decoder-language picker names each language beside its code: the list still
comes from the loaded model, and the value passed to the run is still the code
the model reported. What changes is the label: `fr` now reads `fr — French`,
and `auto` reads `auto — detect the language`. The names are the app's table of
the codes the Whisper family uses; a code outside it keeps its bare form, so an
unrecognised language is not given a guessed name.

Video containers are accepted by the file picker and by a drop: the
Transcription tab's **Browse…** dialog and its drop handler now accept the
audio track of `mp4`, `m4v`, `mkv`, `mov`, `avi`, `webm` and `3gp` files,
along with `aac`, `opus` and `wma` audio. `ffmpeg` already decoded these — it
is asked for raw 16 kHz mono PCM, so no video stream is selected — but the
picker filtered them out and a drop of one was refused before the decoder saw
it. Each listed extension was decoded with the loader's own command rather than
assumed from the container name. A container with no audio track still fails in
the loader with `ffmpeg`'s error.

**Copy** writes the whole transcript to the clipboard: it sits beside **Export**.
It writes the text the Text tab shows, unchanged, to the clipboard and confirms
it on the status line; it does not build a new format. Selecting a passage in
the Text tab still works for copying part of a result, and the button is the
shortcut for all of it.

A vocabulary field for the models that declare the feature:
`transcribe_run_params` has carried a vocabulary list all along (`vocabulary` /
`n_vocabulary`, gated by `TRANSCRIBE_FEATURE_VOCABULARY`), and the wrapper did
not expose it. `RunParamsBuilder.WithVocabulary` now marshals the terms as UTF-8
in priority order, copies them into unmanaged memory and frees them with the
builder; blank terms are dropped. The Transcription tab has a **Vocabulary**
box, one term per line, shown while the selected model reports the feature (an
unloaded model keeps it visible). It applies to offline runs; the CLI has no
flag. The honesty limit is upstream's: a model without the feature ignores the
list with a native warning, and this project has not measured how much a
vocabulary changes a transcript. The page says so instead of promising better
accuracy.

Point at the accuracy figures that exist instead of saying there are none: the
Models banner and the grid tooltips used to lead with what this project has not
measured. They now say where the useful numbers are: the **WER** column quotes
transcribe.cpp's own headline error rate, and each cell's tooltip names the
metric, dataset, split and language. The figures still mix WER, CER and DER
across datasets, so they narrow the field but are not one ranking, and `Measure`
reports speed on your audio only. Two links make the source reachable: the
banner opens transcribe.cpp's per-model accuracy tables at the pinned revision,
and the detail pane opens the selected model's Hugging Face card with its full
quant table and language matrix. No new measurement is claimed.

#### Compute and devices

Only offer backends and devices that can actually run: the Backend picker listed
all seven request values on every machine: Metal on Linux, CUDA and ROCm without
the matching build, and CPU (accelerated) even when no host accelerator was
registered. An unavailable backend could only fail at load, and CPU
(accelerated) without an accelerator just fell back to plain CPU. The list now
comes from the native `BackendAvailable` probe and contains Auto plus the
backends this build and machine can satisfy. CPU (accelerated) is kept only when
an ACCEL device (BLAS/AMX) is actually registered. If the probe itself fails,
the full list is shown with a hint saying why.

The Device picker is narrowed in the same way: only devices the selected backend
accepts are offered. A pin that the new backend rejects is cleared back to Auto
when the backend changes. The devices grid still shows the full inventory, and
its Type column now reads **GPU**, **iGPU**, **CPU** or **accelerator** instead
of `DeviceTypeGpu`. A saved backend that is not available at launch leaves Auto
selected and stays in the file, like a missing device id.

Auto is a device choice, and the compute choice is remembered: the Settings
tab's Device picker had no Auto row: "Let the backend choose" was only a
placeholder, shown while nothing was selected. Once a device was picked there was
no item to select to go back, so the only way to unpin it was to restart the
app. **Auto — let the backend choose** is now the first item in the list. The
Backend picker uses plain labels — **Auto**, **CPU**, **CPU (accelerated)**,
**Metal**, **Vulkan**, **CUDA**, **ROCm** — instead of the enum members'
`BackendAuto` spelling.

The backend and device are also remembered between launches, in
`ui-settings.json` beside the log. The backend is stored by name and the device
by its stable `DeviceId` — the runtime handle is valid only in the process that
enumerated it. A saved device that this launch no longer enumerates leaves Auto
selected and its id in the file, so a device that comes back is used again. A
line under the pickers now says that a change applies to the next run, is
remembered for the next launch, and needs no Save button.

The Settings compute panel says it is optional: the backend and device pickers
are unchanged, but a line under them now says that `Auto` is the default and
that the panel exists to force a backend or pin a device. It also names the
backends this build and machine report as available, so the seven-item list no
longer reads as a choice that must be made. The availability comes from
`Backends.BackendAvailable`; when that call fails, the sentence without the list
is still shown.

#### Remembered configuration and navigation

File dialogs reopen in the last folder used: the **Browse…** dialogs on the
Transcription, Batch and Models tabs, and the two export dialogs, now start in
the folder the last picker used. Picking a file or choosing where to export
records that folder in `ui-settings.json` beside the log, in the same file that
holds each tab's last model and the export options; none of the sections erases
another. A folder that no longer exists makes `LastDirectory` return null for
that launch, so the dialog keeps the platform's own default rather than failing;
the path is left in the file, so a folder that comes back is used again. Only a
local path is recorded: a selection whose storage item has none leaves the
previous folder alone.

Each tab reselects the model it last ran: the pickers opened on the first alias
alphabetically, so a reader who uses one model had to pick it again on every
launch. Each tab now records the model a run actually started with, in
`ui-settings.json` beside the log, and reselects it when the picker is built.
Picking a model and not running it is not recorded. A recorded alias that is no
longer offered — deleted, or, on the Streaming tab, no longer passing the
streaming filter — is ignored and the first entry is selected; a missing or
unreadable settings file falls back to the same first entry. The file holds
only this note: no model, backend or device selection is persisted beyond it.

Lock the settings during a run, and report the run that actually started: the
Transcription tab now disables its settings while a transcription or a memory
probe is in flight. A run already worked from the values captured at its start,
so an edit made during it changed nothing the run did — but it changed what the
reader believed it did, and the error report read the pickers at the moment the
failure was caught. That is how a report could name a window or a model that
had never been used. The run now keeps its `TranscriptionOptions` and model
alias as a snapshot. The error report, the result's provenance and the history
entry are built from that snapshot; the live device and memory lines are still
re-read at report time. The tab's settings are disabled through
`CanEditSettings` for both the run and the probe.

Icons, page headings, and a Batch grid that uses its space: the six tabs and the
actions inside them now carry Material Design icons, and each tab opens with an
icon, its name and a one-line description of what it is for. The icons come
from `Material.Icons.Avalonia` (MIT) and are decorative: every action keeps its
text label and its accessible name. The Transcription result tabs carry icons
too. The Batch results grid now takes the height left over rather than sitting
under a star-sized spacer, which had left only its column headers visible at the
bottom of an empty window.

An About section, with the app's version and whose the native work is: the
Settings tab ends with **About**: the app version read from the assembly (the
same number the release archive is named after, taken from
`build/WRAPPER_VERSION`), a line saying that the native library is transcribe.cpp
by its own authors and that this project only binds and packages it, and the two
repository links as selectable text. The native version was already shown under
**Backend Version**; the section points at it rather than repeating the number.

#### History

(See New features — Local history.)

### Streaming

#### Memory and model capabilities

The streaming tab bounds memory by closing the stream into utterances: a live
stream's memory grows with its audio — measured at roughly 2.8 MiB per second of
the current utterance on one machine and model, because the native stream keeps
the current utterance and a family that re-decodes from the start on every feed
grows with it (`docs/streaming-bench.md`). The tab fed one utterance for the
whole session, so a long stream grew without bound and reached the native ~82 s
CPU abort sooner. A new **Utterance (s)** field, default 30 s, finalizes the
stream, resets it, and begins a new utterance on the same model at that
interval. The committed text of each closed utterance is kept and prepended to
the next, so nothing already transcribed disappears; the model loses its audio
context across the boundary, and the accuracy cost of the interval is not
measured. `0` keeps one utterance for the whole session, which is the previous
behaviour.

The streaming tab refuses a model that upstream calls streaming but the machine
says has no ASR stream: Sortformer (`diar_streaming_sortformer_4spk-v2.1`) is
recorded upstream as `streaming.supported = true`, but that flag covers its
streaming *diarization* API; this tab runs the ASR stream, which the loaded
model reports it does not have. Upstream and the machine can therefore disagree,
and the tab used to keep the model in the picker under a "does not support
streaming" warning. The picker now reads the machine's answer once a model has
been checked — from the capability cache shared with the Transcription tab —
and leaves out a model the machine reports cannot stream, selecting the next
one. Start is still gated on that same answer.

A model deleted while its streaming capabilities are read drops out quietly: the
Streaming tab reads the selected model's capabilities when it opens. A model
deleted between the picker listing it and that read failed with
`ErrFileNotFound` and was written to the log with a full stack trace — for a
file the reader had already removed. The read now notices the file is gone,
re-reads the cache so the model leaves the picker, and logs nothing: a deleted
file is not a capability failure. A model that is present but cannot be loaded —
a corrupt or foreign GGUF — is still logged as before.

The streaming tab's picker offers only models that can stream: the picker listed
every downloaded model, MOSS — the default — among them, and MOSS cannot
stream. Choosing it and pressing Start was refused by the native library with
`ErrNotImplemented`, which came back as a status line. The picker now leaves out
the models upstream records as unable to stream (`streaming.supported = false`),
reading the same pinned catalog the Models tab's capability filter does; a model
upstream says nothing about is still offered, because unknown is not a no.
Start is still gated on the loaded model's own answer for the models that
remain, so a model this machine cannot stream is refused with a reason instead
of a failed run. With only MOSS on disk the picker is empty and the line under
it points at the Models tab rather than starting a run that cannot work.

The streaming tab refuses a model that cannot stream: Start is gated on the
model's streaming capability (the loaded model's answer when checked, otherwise
upstream's pinned catalog entry). A model that declares nothing is not blocked.
When the selected model cannot stream, the button is disabled and a line under
it says why and points at the Models tab.

#### Text, logging and extensions

Clear, selectable text, family extensions and log: the committed and tentative
areas filled up over a session with no way to start the display over. A Clear
button beside Stop empties both. It does not reset the native stream: the
committed prefix is append-only, so a clear during a run hides what was
committed up to the click and shows what comes after it, and the model keeps the
audio context it already has. After a run, Clear just empties the two panels.

The transcription result and the streaming committed/tentative areas were plain
`TextBlock`s, which draw text but take no selection. They are
`SelectableTextBlock`s now: drag for a range, double-click a word, triple-click
for all of it, Ctrl+C to copy, and a right-click Copy flyout from the Fluent
theme. Nothing in the wrapper changed; the text is still produced by the native
run or stream.

The family-extension fields (Moonshine decode interval, Parakeet
attention/buffered context, Voxtral delay tokens/interval) started empty, and an
empty field is not "off": the extension is left off the run entirely, so the
model's own default applies. Nothing said so, no default was shown, and there
was no way back to that state once a field was filled. Each field now carries a
`(model default)` placeholder, a Reset button clears them all (enabled only
when something is set), and the tooltips state what the knob does and what the
default is. The wrapper's `*ExtBuilder` types now expose the values the native
`*_ext_init` sets — read from the initialized struct, not written by hand — and
a test pins them at the -1 "model default" sentinel the interop headers
document.

A failure the UI catches was turned into a line of status text and nothing
else: no type, no inner exception, no stack trace, and the status line cannot be
copied. Every catch that reports a failure now also appends it, in full, to
`<cache>/../logs/ui.log` (beside the `models` directory), and the status message
names that path. The file is appended to and never rotated. The app still has no
other log: Avalonia's `LogToTrace` goes to a Trace listener that nothing
attaches.

The transcription tab and the streaming tab each read the default model's
capabilities when they are built. With a cache per view model, that loaded the
same 700 MB file twice at once. On a machine already short of memory the
doubled peak was enough for the kernel's OOM killer to take the app — verified
in the kernel log (`global_oom`, `Killed process … anon-rss 1,491,572kB`). The
two tabs now share one `ModelCapabilityCache`, so a caller that asks while a
load is in progress joins it instead of starting another. This lowers the
startup peak to one model; it does not make the app safe on a machine that is
already out of RAM.

#### Measured streaming numbers

The wrapper had no real-time figures at all. `docs/cli.md` omits its timing
lines by choice, and every streaming test is a `SkippableFact` that returns
early on `Status.ErrNotImplemented`, so nothing had established whether a
stream keeps up with live audio. Added `samples/StreamingBench` and
[docs/streaming-bench.md](docs/streaming-bench.md), which records what it
measures and, just as importantly, what it does not.

Findings, on one machine, from `moonshine-streaming-tiny-Q8_0`:

- The incremental API behaves as documented: first partial text at 37–74 ms and
  first committed word at 134–221 ms.
- The model default does not keep up. Over 66 s of audio on the CPU backend the
  worst single `Feed()` took 11 248 ms against a one-second chunk, and RTF was
  1.224. A decode interval cut that worst feed to 1614 ms and RTF to 0.783,
  with an identical transcript; `WithCommitPolicy(StablePrefix)` was similar.
  Setting one of the two is not optional for a live pipeline.
- RTF degrades as a stream grows: 0.35 at 33 s, 1.280 at 132 s on the same
  hardware and model, because per-feed cost climbs with accumulated context and
  never returns to its starting value. A short clip flatters the number.
- On the CPU backend, a stream of about 88 s of audio aborts the process:
  `GGML_ASSERT(i01 >= 0 && i01 < ne01) failed` in `ggml-cpu/ops.cpp:5015`, an
  out-of-range KV-cache index. It is an abort inside the native library rather
  than a status code, so `StreamSession.Feed` never returns and no `try`/`catch`
  in .NET intercepts it. Reproduced with a decode interval, with `OnFinalize`,
  and on Vulkan at 132 s; not fixed by raising the session context to 16384 or
  32768. This is upstream behaviour in transcribe.cpp 0.2.4, not something the
  wrapper can catch or work around in-process.

Documented rather than changed: the crash and the RTF growth need a fix in
transcribe.cpp. A live service should isolate the transcriber in a child
process it can restart. Only the Moonshine family was measured, only at a 1000
ms chunk, and on tiled rather than real audio — all stated on the page.

### Distribution and maintenance

#### Memory and audio loading

Peak memory no longer scales with the audio twice over: two allocations in the
audio path each held a second full-size copy of the audio alongside the result,
and both are on the way in for every transcription.

Fixed:

- The ffmpeg fallback fed the decoded PCM through a pipe into a `MemoryStream`,
  then converted it to `float[]`. A pipe carries no length, so the whole stream
  had to be buffered before its size was known, and the `MemoryStream` held up
  to three full-size copies at once: its growth buffer, the `ToArray()` copy, and
  the `float[]` being filled next to both. Measured peak RSS of the CLI for 30
  minutes of audio: 610 MiB, against 299 MiB for a WAV read directly. ffmpeg
  now writes to a temporary file, whose length is known before allocating, so
  the `float[]` is the only large allocation — the same input now peaks at
  274 MiB, within 5 MiB of the direct reader. The temp file is always removed,
  and a temp directory that cannot be written now reports one line like every
  other load failure instead of a .NET stack trace.
- `PcmExtensions.ReadWavToPcm` read the entire data chunk into a `byte[]` and
  only then converted it, holding the audio twice: once as 16-bit integers, once
  as floats. For 30 minutes that was 164 MiB allocated for a 109 MiB result
  (1.50x, all on the large object heap); it is now 109 MiB for 109 MiB (1.00x),
  because the reader converts 64 KiB at a time.
- The CLI's window loop copied every window into a fresh `float[]` before
  calling `Session.Run`, which already takes a `ReadOnlySpan<float>`. A 300 s
  window is 19 MB, so each window was a large object heap allocation that lived
  only for the call. Peak RSS on 451 s of audio: 516 -> 498 MiB.
- `StreamSession.Feed` rented a 48-byte scratch struct from `ArrayPool` and
  pinned it with a `GCHandle` on every call, while `Complete()` and
  `GetCurrentText()` used `stackalloc` for the same struct. Now consistent
  (94 ns -> 12 ns per feed). This one is a consistency and allocation fix, not
  a performance claim: at 100 ms chunks it is 0.0008% of a second of audio.
- A cancellable `Run` (or `Batch.Run`) installed its token as
  `SetAbortCallback(() => ct.IsCancellationRequested)`, allocating a closure
  over the token, a second delegate to wrap it, and a third for the matching
  `ClearAbortCallback` — on every call. The token is now stored and a single
  interop delegate, created once per session, reads whichever abort source is
  current. Measured 89 bytes/call -> 0 with the delegates forced to escape. This
  closes the long-standing `FINDINGS_OPEN.md` §4.3.

Also cleaned up, on the way:

- The ffmpeg invocation left a `RedirectStandardOutput = true` behind when its
  output moved from a pipe to a file: .NET created the pipe and nothing drained
  it. It was dead code rather than a live hang — with that command line ffmpeg
  writes 0 bytes to stdout — but leaving it would have reintroduced a hang that
  no test catches the moment a future change made ffmpeg write there. ffmpeg is
  now also run with `-nostdin`, so it cannot consume the user's keystrokes or
  block on an input stream nobody feeds.
- `Session.GetLimits` and `Model.GetCapabilities` allocated their native struct
  with `Marshal.AllocHGlobal` while every other struct-shaped call goes through
  `StackAllocHelper`. Now consistent; both structs (56 and 32 bytes) use the
  stack path. A consistency change, not a performance one.

Corrected:

- The XML doc on `Session.Run` said it returns a `Transcript` with "FullText
  and DetectedLanguage eagerly loaded" and told the caller to call
  `ReadSegments()`, `ReadWords()`, `ReadTokens()` for the rest. `ReadResults()`
  has always populated segments, words, tokens and speaker segments
  unconditionally, so the doc described an opt-in API that did not exist and
  invited callers to re-read rows the returned `Transcript` already holds. The
  doc now says what happens. The eager loading itself is unchanged and intended:
  it costs 2.0 µs, against seconds of inference.
- The README did not mention that the ffmpeg fallback now needs a writable
  temporary directory.

#### CLI first-run robustness

`transcribe` is what most people run first, and the paths a new user hits before
any model is loaded were untested. They are now covered, and five of them no
longer end in a crash, a hang or a needless download.

Fixed:

- A file that is not audio (a text file named `.wav`, a truncated file) ended
  the process with a .NET stack trace and exit code 134. It now produces one
  actionable line.
- `--out` into a directory that does not exist did the same. The parent
  directory is now checked while parsing, and a permission failure is reported
  instead of thrown.
- `--chunk 1` looped forever: consecutive windows overlap by 1 s, so a 1 s
  window never advanced. A window that leaves no room for the overlap is now
  rejected, with the smallest usable value in the message.
- The model was resolved (and downloaded — 667 MB for the default alias)
  *before* the input file, the flags and the compute choice were checked. Every
  check now happens first, so a mistyped filename or a `--backend cuda` on a
  machine without CUDA costs nothing.
- An unknown option was ignored: `--formt json` silently wrote the plain format.
  Unknown options, an extra positional argument, an unusable `--chunk` and an
  empty `--lang` are now reported.
- A HuggingFace spec naming a file in a subdirectory
  (`owner/repo/q4/model.gguf`) could never resolve and reported the file as
  "not found": only the bare file name was compared against the repo listing.
  The full path is accepted too.
- The README claimed that pinning `@<revision>` in a HuggingFace spec makes a
  later run offline. It does not: a spec carries no sha256, so the hash is read
  from the HuggingFace metadata on **every** run. Only the curated aliases are
  offline from the second run on, because the shipped manifest pins their
  revision *and* their hash. The README now says that.

Added: test coverage for the CLI (argument parsing, window/overlap math, audio
loader, plain/WebVTT/JSON writers, model manifest, download and sha256
verification with a stubbed HTTP handler, the command in process, and the real
executable end to end); the CLI assembly goes from effectively no coverage to
~92% line coverage (224 -> 360 tests). The README's "All options" block had
drifted from `transcribe --help` (missing `--backend`, `--device` and
`--list-devices`); a test now compares the two. A "Speaker diarization" section
in the README: the four ways to ask for it (capability probe, per-run toggle,
batch, CLI), verified against the MOSS diarization model, and the one that is
not available — the streaming API, whose upstream
`transcribe_stream_params` has no `diarize` field in transcribe.cpp v0.2.4.

#### Release assets: SBOM and checksums

Every GitHub Release now attaches `TranscribeCppSharp.sbom.spdx.json` (the SPDX
SBOM the `sbom` job already produced) and `SHA256SUMS.txt` — one `sha256sum`
line per shipped file, computed over the exact bytes uploaded — so a download
can be verified. v0.4.0 was published before this and had both attached by hand.



## [0.4.0] - wrapper release

### Desktop app: self-contained archives per platform

`TranscribeCppSharp.UI` was documented as source-only ("no installer, no published binary,
no NuGet package"). It now also ships as a self-contained archive per platform, attached to
the GitHub Release by a new `pack-ui` CI job:

`TranscribeCppSharp.UI-<version>-<rid>.zip` for `win-x64`, `linux-x64`, `linux-arm64`,
`osx-arm64` and `osx-x64`. Each archive carries the .NET runtime and the matching native
transcribe.cpp build, so no SDK is needed. `TranscribeCppSharp.UI.csproj` injects the per-RID
native (the same mechanism the CLI uses) and now follows the wrapper's SemVer.

What the archives are not, stated plainly: **not code-signed** (Windows SmartScreen and macOS
Gatekeeper will warn; there is no installer and no notarization), and **not bundled with
`ffmpeg`**, which the app still needs on `PATH` for any non-WAV input and for the microphone.
All five RIDs were published locally and each archive carries its matching native
(`libtranscribe.so`, `libtranscribe.dylib` or `transcribe.dll`); only the Linux x64 archive
was launched. CI builds the archives but does not launch them on their target OS. There is
still no NuGet package for the app.

Follow-ups after inspecting the first archives:

- **Windows opened a console window.** The project used `OutputType=Exe`, so the released
  `.exe` had the console PE subsystem and showed a black window beside the GUI. Now `WinExe`
  (GUI subsystem); it is the same executable on Linux and macOS. This was invisible through
  `dotnet run` in a terminal, which is why it shipped unnoticed — it took inspecting the
  built `.exe` to see it.
- **The archives carried no licence at all.** The native injection glob was `native/*`, so
  the `licenses/` subdirectory the native release ships (ggml, miniz, Silero VAD, ...) was
  dropped, and a plain publish does not add the `README`/`LICENSE` that NuGet packing does.
  The glob is recursive now and the project `LICENSE` and `README.md` are copied into the
  output, so a distributed archive carries both.
- **macOS ships a bare executable, not a `.app`.** No `Info.plist`, no code signature, so
  Gatekeeper quarantines it like any unsigned download. Documented rather than fixed: a real
  bundle (and signing) is a separate piece of work.

### One icon across every surface

The project already had an identity — a signal-teal waveform on the dark
"listening-room" ground, drawn once as the docs header logo. This adds the square
form of that same mark and puts it on every surface that had none: the NuGet
packages (`PackageIcon`), the Windows `.exe` of the CLI and the desktop app
(`ApplicationIcon`), the desktop app's window icon (title bar and taskbar), and
the docs favicon, README and docs home page.

No new identity was invented: the two colours are the ones already declared in
`docs/_sass/color_schemes/waveform.scss`. `docs/assets/images/icon.svg` is the
only authored file; `scripts/generate-icons.sh` derives the PNGs and the
multi-resolution `.ico` from it, so no committed bitmap can drift from the vector.

### Bind transcribe.cpp v0.4.0 (roles; diarize/langid/vad; backend allowlist)

The pinned upstream version moves from v0.3.0 to v0.4.0 (released 2026-10-12;
v0.3.1 in between was a small release). Everything below is upstream's work; this
change re-binds the wrapper to their release, regenerates the P/Invoke surface,
and migrates the wrapper for the behaviour that changed. The wrapper's own version
moves to `0.4.0` too, because the C# API changes are breaking.

Native ABI, verified by diffing the two public headers:

- **Additive at the symbol level**: no function removed, no existing enum value
  changed, and only one new public struct (`transcribe_backend_init_params`).
  Four new functions (`transcribe_model_roles`, `transcribe_allowed_backends`,
  `transcribe_init_backends_ex`, `transcribe_backend_init_params_init`) and 21 new
  enum constants.
- One **removal**: `transcribe_sortformer_stream_ext` and its struct
  `SortformerStreamExt` are gone (the Sortformer preset moved to
  `transcribe_diarize_params`, the DIARIZE_RUN extension slot).

Upstream 0.4 introduces **roles**: a model serves one or more of ASR, DIARIZE,
LANGID and VAD, fixed at load. What that means for this wrapper:

- `TranscribeRole` and `Model.Roles` (`transcribe_model_roles`).
- **Diarization is a role of its own.** A Sortformer model no longer runs through
  the ASR session — `Model.CreateSession` throws `UNSUPPORTED_ROLE` — and there is
  no streaming diarization. New offline `DiarizeSession` (with
  `SortformerDiarizeExtBuilder` for the preset). The old
  `SortformerStreamExtBuilder` and `StreamParamsBuilder.WithSortformerExt` are
  removed, and the desktop app's streaming "Sortformer preset" control with them.
- **New `LangIdSession` and `VadSession`** (`Model.CreateLangIdSession` /
  `CreateVadSession`), with their parameter builders and result records.
- **Capabilities are ASR-only.** `Model.GetCapabilities` now throws
  `UNSUPPORTED_ROLE` on a model without the ASR role instead of returning a zeroed
  struct; the UI capability probe reads `Roles` first.
- **Backend allowlist**: `BackendMask`, `Backends.InitDefault(BackendMask)`,
  `Backends.Init(dir, BackendMask)`, `Backends.AllowedBackends`.
- New status codes **20** (`UNSUPPORTED_ROLE`) and **21** (`INPUT_TOO_SHORT`).
  They flow through `TranscribeException` via the native status string.
- Behaviour changes that need no wrapper work but are worth knowing: non-finite
  audio (NaN/±Inf) is rejected with `INVALID_ARG`, a stream whose family hook
  throws now ends `FAILED`, and host thread-pool failures return an error instead
  of terminating the process.

Tooling and pin:

- `scripts/regenerate-bindings.sh` now vendors and parses `include/transcribe/*.h`
  as well as the root header (`CHeaderDoc.FromDirectory`). The root header does
  not include the sub-headers, so extension, VAD, LANGID and diarize functions
  were generated without their doc comments; the golden test now uses the same
  header set.
- The generator maps a pointer return to `IntPtr`: 0.4's `transcribe_vad_probs`
  returns `const float *`, which mapped to `ReadOnlySpan<float>` as a return and
  failed to build (SYSLIB1051). Regenerating at v0.3.0 is byte-identical.
- `FetchNative --update-hashes` merges into `build/native-sha256.json` instead of
  overwriting it, so older versions keep their checksums.
- Catalog re-pinned to v0.4.0: 76 records (4 new: `parakeet-ultra`, `orukeet`,
  `silero-vad-v6.2`, `lang-id-voxlingua107-ecapa`). The CLI's curated `models.json`
  was left at its 72 aliases in this commit; the 4 new records are curated later
  (see "The four new role models are curated" below).

Verified: full solution builds; 544 Interop and 79 UI tests pass against the 0.4.0
native library (83.6% total line coverage). The three new sessions are exercised
end to end against the real weights — Silero VAD v6.2 (1.2 MB), VoxLingua107
ECAPA-TDNN Q8_0 (27 MB) and Sortformer Q8_0 (139 MB) — which
`scripts/run-integration-tests.sh` now fetches, each pinned to a HuggingFace
revision and verified by sha256. `VadSessionTests`, `LangIdSessionTests` and
`DiarizeSessionTests` cover the role check, info, run, streaming, timings,
disposal, non-finite-audio rejection and mid-run cancellation. The added code is
at 98% line coverage; the remainder is defensive `throw`s on native getters that
a healthy session never returns non-OK from (unreachable without fault injection).
New page: [docs/roles.md](docs/roles.md).

### The CLI and the desktop app follow the model's role

The 0.4 bind left the front ends behind: both only ever created an ASR session,
so a Sortformer — which is in the CLI's curated alias list — crashed the CLI with
an unhandled `UNSUPPORTED_ROLE`, and a langid/vad model could not be run at all.

- **CLI**: `transcribe` reads `Model.Roles` and dispatches. An ASR model keeps the
  transcription path; a diarizer prints speaker turns; a language model prints the
  ranked languages; a VAD model prints the speech segments. A `roles :` line names
  what the model serves. New `--allowed en,fr,de` restricts a language model, and
  `--out` writes a plain or JSON report for the non-ASR roles (`--format vtt` is
  refused with an explanation, since a WebVTT cue is an ASR shape).
- **Desktop app**: the Transcription tab runs the model's role and shows the
  result, with a `runs:` line under the picker, a **Languages** grid for a
  language model and a **Speech** grid for a VAD model (a diarizer fills the
  existing Speakers grid). A language model also gets an **Allowed languages**
  field. The Models tab's *Check diarization* now asks the DIARIZE role as well
  as the ASR feature, so a Sortformer reads *supported*.
- **Long-form audio, honestly.** VAD is `chunked-unbounded` upstream, so the CLI
  and the app score it window by window and union the intervals across the seams
  (memory is one window, not the file; `--chunk` sets the window). Each window is
  scored independently, so a seam can bridge a short silence a one-pass run keeps
  as a gap: the speech found and its total duration match, the exact boundaries at
  a seam do not. Language identification is a 30 s hard cap, so the head of the
  file is used. Diarization is a hard cap with no streaming entry point and
  per-run speaker ids, so it is run whole and not split — windowing it would
  renumber speakers at every seam.

Tests: `RoleCommandTests` (CLI), `VadWindowingTests` (the merge rules and a
windowed run on ~138 s of synthesized audio) and `TranscriptionServiceRoleTests`
(service) run against the three role weights. 562 Interop + 85 UI tests pass.

### The four new role models are curated

The 0.4 catalog added four models the manifest did not offer, so the roles the CLI
and the app now run could only be reached with a raw HuggingFace spec. They are
aliases now, each pinned to a HuggingFace revision with its sha256:

| alias | role | quant | size |
|---|---|---|---|
| `parakeet-ultra` | ASR (25 European languages) | Q5_K_M | 524 MiB |
| `orukeet` | ASR (25 European languages) | Q5_K_M | 523 MiB |
| `lang-id-voxlingua107-ecapa` | LANGID | Q8_0 | 25 MiB |
| `silero-vad-v6.2` | VAD | F32 | 1.2 MiB |

The manifest goes from 72 to 76 aliases. Two detail-pane rules were relaxed for
models upstream records differently: a language model's headline is an accuracy
(on FLEURS), now shown — `HeadlineAccuracyDetail` and the CLI's catalog section
handle both an error rate and an accuracy — and a VAD model has no language and no accuracy, so an
explicitly empty language list is allowed (a *missing* field is still an error)
and a blank accuracy line is the record, not a broken reader. The WER column stays
an error-rate column: a language model shows a dash there.

### Desktop app: an Avalonia GUI over the wrapper

`src/TranscribeCppSharp.UI` is a five-tab desktop app (Transcription, Streaming,
Batch, Models, Settings) over the same wrapper and the same native library as
the `transcribe` CLI. It adds no transcription logic of its own, so the
documented limits — the thread-safety rule, the native abort on long streams,
the absent CUDA runtime — apply to it unchanged.

It ships as unsigned, self-contained archives (see the entry above): no installer, no code
signature, no NuGet package. The app also runs from a clone, after
`dotnet run --project tools/FetchNative` the same way the CLI does. `CopyNativeLib.targets` now covers the GUI as well, which
it did not used to: the GUI silently fell back to the `TranscribeCppSharp.Native`
package, 90 of the 99 entry points the Interop declares were missing from it, and
every device call in Settings threw.

What it does:

- **Transcription** — audio file, model, language, threads, KV type, context
  size, window length, Whisper prompt and temperature, task and target language,
  speculative-decoding drafts, keep-special-tags. Results as text, segments,
  words or speaker segments; export to `.txt`, `.vtt` or `.json`.
- **Streaming** — live microphone transcription at 16 kHz mono 16-bit PCM, with
  committed text beside tentative text, the commit policy, the stable-prefix
  agreement count, and the family extensions for Moonshine, Parakeet, Sortformer
  and Voxtral.
- **Batch** — a queue of files run one after another against one loaded model,
  exported as `.txt` or `.json`.
- **Models** — the 76 curated aliases, filterable by alias, family, size,
  licence or what is on disk, with per-row download, measure and delete. It can
  time a model on audio you choose: one discarded warm-up and three timed
  passes, the best kept, in memory for the session only. Diarization support is a
  third state — `?` until a model has actually been loaded and asked.
- **Settings** — backend and device, the shared cache directory, the native
  library version, and the devices this build can see. The pickers here reach
  the three transcription tabs; they were displayed and read by nothing before.

Options a model does not have are hidden from the loaded model's own reported
capabilities rather than from its name, and a model that is not downloaded keeps
everything visible with the reason shown — hiding on a guess would hide a working
option. All five views carry automation ids and accessible names.

The desktop app is documented on its own page, [docs/ui.md](docs/ui.md), which
carries a screenshot of each tab, and the documentation home page shows one. The
pictures are generated by [`samples/UIScreenshot`](samples/UIScreenshot), which
runs the real `App` on Avalonia's headless platform with the real Skia renderer
and takes the frame itself — no hand-placed image, and no fabricated result to fill
a frame. They are byte-identical across runs on one machine, so a changed PNG means
the UI changed; they are not reproducible between machines, because the fonts, the
compute devices and the cache counts come from the machine that drew them.

Documented, not changed: the app ranks nothing by accuracy, because this project
publishes no accuracy comparison between models. `Measure` reports speed over
your audio and nothing more. [docs/ui.md](docs/ui.md) has the per-tab detail;
README.md points at it.

Tests: headless Avalonia tests in `tests/TranscribeCppSharp.UI.Tests`, run by
the same integration script on all three CI platforms. They cover the automation
ids, the option-visibility rules, the defaults and the catalogue item's display
states, and they realize the grid rows to read back bindings inside
`DataTemplate`s — a property with no binding fails silently, which is how the
Models tab banner told readers to start with the model marked 'default' while no
row was marked. No test drives the window interactively, and nothing compares
pixels: the screenshot tool is a generator run by hand, not a check, so a visual
regression passes unnoticed and the automation ids are what CI pins down.

### Bind transcribe.cpp v0.3.0 (breaking upstream ABI, additive C# API)

The pinned upstream version moves from v0.2.4 to v0.3.0 (released 2026-10-03).
Everything below is upstream's work; this commit only re-binds the wrapper to
their release and regenerates the P/Invoke surface from it.

Native ABI change, verified against the two upstream artifacts this project
mirrors:

- `transcribe_run_params` grows from 72 to 104 bytes: new `vocabulary` +
  `n_vocabulary`, `prompt` and `prefix` fields (caller-owned strings, copied by
  the library before the call returns). The regenerated `RunParams` C# struct
  matches the new layout, offset for offset (checked by the ABI tests).
- New `Status.ErrOutputRepetition` (19): a greedy decode that started looping
  on the same token block is stopped early. Result-bearing like
  `ErrOutputTruncated` (partial transcript readable, `was_truncated()` true);
  the two codes differ only in why the decode stopped.
- New `Task.TaskInstruct` (2): offline instruction-following via `prompt`,
  gated by the model's `FeatureInstruct`.
- New feature flags `FeatureVocabulary` (7), `FeatureContextPrompt` (8),
  `FeatureInstruct` (9), `FeatureTranscriptPrefix` (10).

C# changes: all additive — one new status value, one new task value, four new
feature values, and four new fields on the marshalled `RunParams`. Nothing was
removed or renamed. Streaming is untouched: `transcribe_stream_params` is
unchanged between v0.2.4 and v0.3.0, so the documented streaming behaviour and
its limits are not altered by this bump.

The wrapper does not (yet) expose `vocabulary` / `prompt` / `prefix` as
`With...` convenience methods; the regenerated struct fields and the enum
values are available, and the run-params builder still covers the pre-existing
knobs (`WithSpecKDrafts` and earlier).

`FetchNative` archive names are unchanged in v0.3.0, so the fetch logic needed
no change; `build/native-sha256.json` gains the v0.3.0 hashes (computed from
the downloads themselves, matching the values upstream publishes) and keeps the
older entries so older commits still fetch.

### Streaming: first measured real-time numbers, and a native crash they expose

The wrapper had no real-time figures at all. `docs/cli.md` omits its timing
lines by choice, and every streaming test is a `SkippableFact` that returns
early on `Status.ErrNotImplemented`, so nothing had established whether a stream
keeps up with live audio. Added `samples/StreamingBench` and
[docs/streaming-bench.md](docs/streaming-bench.md), which records what it
measures and, just as importantly, what it does not.

Findings, on one machine, from `moonshine-streaming-tiny-Q8_0`:

- The incremental API behaves as documented: first partial text at 37–74 ms and
  first committed word at 134–221 ms.
- The model default does not keep up. Over 66 s of audio on the CPU backend the
  worst single `Feed()` took **11 248 ms** against a one-second chunk, and RTF
  was 1.224. A decode interval cut that worst feed to **1614 ms** and RTF to
  0.783, with an identical transcript; `WithCommitPolicy(StablePrefix)` was
  similar. Setting one of the two is not optional for a live pipeline.
- RTF **degrades as a stream grows**: 0.35 at 33 s, 1.280 at 132 s on the same
  hardware and model, because per-feed cost climbs with accumulated context and
  never returns to its starting value. A short clip flatters the number.
- On the CPU backend, **a stream of about 88 s of audio aborts the process**:
  `GGML_ASSERT(i01 >= 0 && i01 < ne01) failed` in `ggml-cpu/ops.cpp:5015`, an
  out-of-range KV-cache index. It is an abort inside the native library rather
  than a status code, so `StreamSession.Feed` never returns and no `try`/`catch`
  in .NET intercepts it. Reproduced with a decode interval, with
  `OnFinalize`, and on Vulkan at 132 s; **not** fixed by raising the session
  context to 16384 or 32768. This is upstream behaviour in transcribe.cpp 0.2.4,
  not something the wrapper can catch or work around in-process.

Documented rather than changed: the crash and the RTF growth need a fix in
transcribe.cpp. A live service should isolate the transcriber in a child
process it can restart. Only the Moonshine family was measured, only at a
1000 ms chunk, and on tiled rather than real audio — all stated on the page.

### Audio loading: peak memory no longer scales with the audio twice over

Two allocations in the audio path each held a second full-size copy of the
audio alongside the result, and both are on the way in for every transcription.

Fixed:

- The ffmpeg fallback fed the decoded PCM through a pipe into a `MemoryStream`,
  then converted it to `float[]`. A pipe carries no length, so the whole stream
  had to be buffered before its size was known, and the `MemoryStream` held up
  to three full-size copies at once: its growth buffer, the `ToArray()` copy,
  and the `float[]` being filled next to both. Measured peak RSS of the CLI for
  30 minutes of audio: **610 MiB**, against 299 MiB for a WAV read directly.
  ffmpeg now writes to a temporary file, whose length is known before
  allocating, so the `float[]` is the only large allocation — the same input
  now peaks at **274 MiB**, within 5 MiB of the direct reader. The temp file is
  always removed, and a temp directory that cannot be written now reports one
  line like every other load failure instead of a .NET stack trace.
- `PcmExtensions.ReadWavToPcm` read the entire data chunk into a `byte[]` and
  only then converted it, holding the audio twice: once as 16-bit integers, once
  as floats. For 30 minutes that was **164 MiB allocated for a 109 MiB result**
  (1.50x, all on the large object heap); it is now **109 MiB for 109 MiB**
  (1.00x), because the reader converts 64 KiB at a time.
- The CLI's window loop copied every window into a fresh `float[]` before
  calling `Session.Run`, which already takes a `ReadOnlySpan<float>`. A 300 s
  window is 19 MB, so each window was a large object heap allocation that lived
  only for the call. Peak RSS on 451 s of audio: 516 -> 498 MiB.
- `StreamSession.Feed` rented a 48-byte scratch struct from `ArrayPool` and
  pinned it with a `GCHandle` on every call, while `Complete()` and
  `GetCurrentText()` used `stackalloc` for the same struct. Now consistent
  (94 ns -> 12 ns per feed). This one is a consistency and allocation fix, not a
  performance claim: at 100 ms chunks it is 0.0008% of a second of audio.
- A cancellable `Run` (or `Batch.Run`) installed its token as
  `SetAbortCallback(() => ct.IsCancellationRequested)`, allocating a closure
  over the token, a second delegate to wrap it, and a third for the matching
  `ClearAbortCallback` — on every call. The token is now stored and a single
  interop delegate, created once per session, reads whichever abort source is
  current. Measured **89 bytes/call -> 0** with the delegates forced to escape.
  This closes the long-standing `FINDINGS_OPEN.md` §4.3.

Also cleaned up, on the way:

- The ffmpeg invocation left a `RedirectStandardOutput = true` behind when its
  output moved from a pipe to a file: .NET created the pipe and nothing drained
  it. It was dead code rather than a live hang — with that command line ffmpeg
  writes 0 bytes to stdout — but leaving it would have reintroduced a hang that
  no test catches the moment a future change made ffmpeg write there. ffmpeg is
  now also run with `-nostdin`, so it cannot consume the user's keystrokes or
  block on an input stream nobody feeds.
- `Session.GetLimits` and `Model.GetCapabilities` allocated their native struct
  with `Marshal.AllocHGlobal` while every other struct-shaped call goes through
  `StackAllocHelper`. Now consistent; both structs (56 and 32 bytes) use the
  stack path. A consistency change, not a performance one.

Corrected:

- The XML doc on `Session.Run` said it returns a `Transcript` with "FullText
  and DetectedLanguage eagerly loaded" and told the caller to call
  `ReadSegments()`, `ReadWords()`, `ReadTokens()` for the rest. `ReadResults()`
  has always populated segments, words, tokens and speaker segments
  unconditionally, so the doc described an opt-in API that did not exist and
  invited callers to re-read rows the returned `Transcript` already holds. The
  doc now says what happens. The eager loading itself is unchanged and intended:
  it costs 2.0 µs, against seconds of inference.
- The README did not mention that the ffmpeg fallback now needs a writable
  temporary directory.

Added:

- Tests for the paths these changes opened up: a WAV payload spanning several
  conversion chunks (both sides of the boundary, and the stereo downmix stride
  across one), a truncated data chunk, the ffmpeg path compared sample-for-sample
  against the WAV reader over 1.28 MB of decoded audio, resample plus stereo
  downmix, temp-file cleanup, the new `StackAllocHelper` overload, and the abort
  source (a token and a user callback staying distinguishable, and switching
  between them without allocating).

### Command-line tool: first-run robustness and test coverage

`transcribe` is what most people run first, and the paths a new user hits
before any model is loaded were untested. They are now covered, and five of
them no longer end in a crash, a hang or a needless download.

Fixed:

- A file that is not audio (a text file named `.wav`, a truncated file) ended
  the process with a .NET stack trace and exit code 134. It now produces one
  actionable line.
- `--out` into a directory that does not exist did the same. The parent
  directory is now checked while parsing, and a permission failure is reported
  instead of thrown.
- `--chunk 1` looped forever: consecutive windows overlap by 1 s, so a 1 s
  window never advanced. A window that leaves no room for the overlap is now
  rejected, with the smallest usable value in the message.
- The model was resolved (and downloaded — 667 MB for the default alias)
  *before* the input file, the flags and the compute choice were checked. Every
  check now happens first, so a mistyped filename or a `--backend cuda` on a
  machine without CUDA costs nothing.
- An unknown option was ignored: `--formt json` silently wrote the plain
  format. Unknown options, an extra positional argument, an unusable `--chunk`
  and an empty `--lang` are now reported.
- A HuggingFace spec naming a file in a subdirectory
  (`owner/repo/q4/model.gguf`) could never resolve and reported the file as
  "not found": only the bare file name was compared against the repo listing.
  The full path is accepted too.
- The README claimed that pinning `@<revision>` in a HuggingFace spec makes a
  later run offline. It does not: a spec carries no sha256, so the hash is read
  from the HuggingFace metadata on **every** run. Only the curated aliases are
  offline from the second run on, because the shipped manifest pins their
  revision *and* their hash. The README now says that.

Added:

- Test coverage for the CLI: argument parsing, the window/overlap math, the
  audio loader, the plain/WebVTT/JSON writers, the model manifest, the download
  and its sha256 verification (with a stubbed HTTP handler, so no 600 MB fetch
  is needed), the command in process, and the real executable end to end. The
  CLI assembly goes from effectively no coverage to ~92% line coverage
  (224 -> 360 tests).
- The README's "All options" block had drifted from `transcribe --help` (it was
  missing `--backend`, `--device` and `--list-devices`); a test now compares the
  two.
- A "Speaker diarization" section in the README: the four ways to ask for it
  (capability probe, per-run toggle, batch, CLI), verified against the MOSS
  diarization model, and the one that is not available — the streaming API,
  whose upstream `transcribe_stream_params` has no `diarize` field in
  transcribe.cpp v0.2.4.

## [0.3.1] - wrapper release

Binds to **transcribe.cpp v0.2.4** (was v0.2.3). No C# API change; this is a
packaging release, so the wrapper takes a patch bump while `Interop` and
`Native.*` move to 0.2.4.

- The `TranscribeCppSharp.Native.*` packages now redistribute the upstream
  v0.2.4 archives. On the native side that means ggml 0.25.3, an upstream fix
  for decode-budget scaling, lower Parakeet memory use, and an offline Voxtral
  patch — all authored upstream by the transcribe.cpp authors.
- **No ABI change**: the upstream Rust FFI file is byte-for-byte identical
  between v0.2.3 and v0.2.4, and the public header differs only by its version
  define and one doc comment. The only visible effect on this package is a
  corrected doc comment on `ItnMode.ItnModeDefault`: upstream v0.2.4 documents
  that `DEFAULT` resolves to **ON** for `sensevoice` (its ITN toggle is the only
  source of casing and punctuation there), while `funasr_nano` keeps the
  upstream `itn=False` default.

## [0.3.0] - wrapper release

Binds to **transcribe.cpp v0.2.3** (unchanged). No C# API change; this is a
packaging + tooling release.

**Packaging (breaking install behavior):**

- `TranscribeCppSharp.Interop` no longer depends on the native runtime
  packages. Previously every consumer pulled all five `Native.*` packages
  (~200 MB) regardless of platform. Add the native package for your RID
  (`dotnet add package TranscribeCppSharp.Native.linux-x64`), or the new
  `TranscribeCppSharp.Bundle` meta-package that pulls the wrapper plus every
  native runtime.
- The wrapper now depends only on the (platform-agnostic) Interop core.

**New command-line tool:**

- `TranscribeCppSharp.Cli` — a `.NET tool` exposing `transcribe`. Published
  RID-specific (SDK 10): `dotnet tool install` selects the package for your
  platform and only that RID's native binaries are embedded; an `any` fallback
  is framework-dependent. Install is a few MB, nothing is downloaded at first
  run for the native.
- Models are never bundled. `--model` accepts a file path, a curated alias
  (72 models across every family transcribe.cpp supports — Whisper, Moonshine,
  Parakeet, Canary, GigaAM, Voxtral, Qwen3-ASR, Granite-speech, MOSS and
  streaming/multitalker diarization, …), or a generic HuggingFace spec
  `<owner>/<repo>/<file.gguf>[@<revision>]`. Downloads use a pinned revision,
  are verified by sha256 (read from the HF LFS metadata) and cached.
- `--list-models` / `--model-info <alias>` show each model's license
  (non-commercial licenses are flagged); `--quant` selects a quantization.
- **GPU by default.** The tool no longer pins the CPU backend: it uses the
  upstream `AUTO` policy (every discrete GPU probed before the integrated ones,
  CPU as last resort) and prints the backend the model actually landed on.
  `--backend <auto|cpu|cpu-accel|metal|vulkan|cuda|rocm>` forces one and
  `--device <n>` pins an exact device (indices from `--list-devices`); a forced
  choice fails loudly rather than falling back silently.
- `tools/UpdateModelManifest` regenerates the model manifest from the
  HuggingFace API.

**Versioning:** `TranscribeCppSharp.Bundle` and `TranscribeCppSharp.Cli` follow
the wrapper's SemVer (this release / the tag); `Interop` and `Native.*` keep
tracking the upstream transcribe.cpp version.

## [0.2.0] - wrapper release

**Breaking API change** (wrapper minor bump per SemVer 0.x): the device-selection
API no longer uses integer GPU indices. Binds to **transcribe.cpp v0.2.3** (was
v0.1.3). Upstream v0.2.0 was a
deliberate pre-1.0 ABI break — see upstream
[docs/migrating-to-0.2.md](https://github.com/handy-computer/transcribe.cpp/blob/v0.2.3/docs/migrating-to-0.2.md).

- Device selection: `ModelLoadParamsBuilder.WithGpuDevice(int)` **removed**
  (obsolete error) — use `WithDevice(BackendDevice)` / `WithDevice(IntPtr)`
  with a handle from `Backends.EnumerateDevices()`. `BackendDevice` now
  carries the runtime-owned `Handle`; `Backends.GetDeviceInfo(handle)`
  resolves metadata for any handle; `Model.Device` reports the loaded
  model's device. `0` is now a selectable device, no longer the auto
  sentinel (omit `WithDevice` for automatic selection).
- New bindings: `BackendRequest.Rocm`, `Feature.Diarization`,
  `RunParamsBuilder.WithDiarize(DiarizeMode)`, `Session.RawText`,
  `Session.ReadSpeakerSegments()` / `SpeakerSegmentCount` (+ batch
  mirrors), `SegmentResult.SpeakerId`, `Transcript.RawText` /
  `Transcript.SpeakerSegments`, `SortformerStreamExtBuilder`
  (+ `StreamParamsBuilder.WithSortformerExt`).
- Regenerated `NativeMethods` from v0.2.3 (`DeviceInfo`,
  `SpeakerSegment`, `SortformerStreamExt`, `RawText`, …); native
  `sha256` hashes for all 5 RIDs in `build/native-sha256.json`.

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

- **v0.2.4** — current version packaged by this project.
- **v0.2.3** — packaged by 0.2.0 and 0.3.0.
- **v0.1.3** — first version packaged by this project.

[0.4.0]: https://github.com/manuc66/TranscribeCppSharp/compare/v0.3.1...v0.4.0
[0.3.1]: https://github.com/manuc66/TranscribeCppSharp/compare/v0.3.0...v0.3.1
[0.3.0]: https://github.com/manuc66/TranscribeCppSharp/compare/v0.2.0...v0.3.0
[0.2.0]: https://github.com/manuc66/TranscribeCppSharp/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/manuc66/TranscribeCppSharp/compare/v0.1.3...v0.1.0
