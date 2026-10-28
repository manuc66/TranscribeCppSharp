---
title: Exporting
nav_order: 6
---

# Exporting

`--out` writes the transcript to a file; `--format` picks the shape:

| `--format` | Output |
| --- | --- |
| `plain` (default) | header comments + timestamped lines |
| `vtt` | WebVTT, with speaker cues |
| `html` | standalone HTML: numbered paragraphs, timestamps, speakers |
| `json` | `{"language", "text", "segments":[{start,end,speaker,text}]}` |
| `srt` | SubRip subtitles: numbered cues, `HH:MM:SS,mmm` |
| `ass` | Advanced SubStation Alpha: one script with a `Default` style |

```bash
transcribe jfk.wav --model whisper-tiny --out jfk.vtt --format vtt
```

```vtt
WEBVTT
NOTE source: /audio/jfk.wav
NOTE audio 00:00:11 model whisper/whisper-tiny lang en diarize on window 300s generated 2026-01-01 09:00:00

00:00:00.000 --> 00:00:10.500
<v Speaker 0>And so my fellow Americans ask not what your country can do for you, ask what you can do for your country.</v>

NOTE done 2026-01-01 09:00:00
```

Real output, with the input path and timestamps replaced by placeholders. The
`NOTE source` line records the absolute path of the input file, as does the
`# source` header of the `plain` format.

## JSON

`--format json` writes the language, the full text and the segments, with times
in seconds:

```json
{
  "language": "en",
  "text": "And so my fellow Americans ask not what your country can do for you, ask what you can do for your country.",
  "segments": [
    {"start":0,"end":10.5,"speaker":0,"text":"And so my fellow Americans ask not what your country can do for you, ask what you can do for your country."}
  ]
}
```

Line endings in the exported file are `\n` on every platform — `TranscriptWriter`
does not go through `TextWriter.WriteLine`, which would emit `\r\n` on Windows —
so a transcript exported on Windows diffs cleanly against one exported on Linux.

## Subtitles: SRT and ASS

`--format srt` and `--format ass` write the same segments as cues, for a video
player or a subtitle editor:

```srt
1
00:00:00,000 --> 00:00:10,500
And so my fellow Americans ask not what your country can do for you, ask what you can do for your country.
```

The ASS form is one script with a `Default` style (Arial 48, bottom-centred), so
it opens in VLC, mpv or Subtitle Edit without a style to pick first. Both formats
use the model's own segment boundaries: nothing is re-timed, split or merged to
make cues look shorter. A model that returns one long segment therefore produces
one long cue.

Speaker labels are included only when attribution was requested
(`--no-diarize` removes them); with them, SRT prefixes `Speaker N:` and ASS puts
the same prefix in the dialogue text. Both are plain renderings of
`SpeakerSegments`/`Segments`, not a second diarization pass.

The renderer is `src/Shared/SubtitleWriter.cs`, compiled into the CLI, the server
and the desktop app, so the bytes do not depend on which front end exported them.
The desktop app offers the same two formats in its export menu, and uses the
speaker names typed in the tab when there are any.

## HTML

`--format html` writes one self-contained document: no external CSS, no images,
no JavaScript. It opens in a browser and imports into a word processor, which is
what the literal line numbers in the first column are for — they are table cells,
not a CSS counter, so Word keeps them.

```html
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="generator" content="TranscribeCppSharp">
<title>jfk.wav — transcript</title>
...
</head>
<body>
<h1>jfk.wav</h1>
<p class="meta">
source: /audio/jfk.wav<br>
audio: 00:11 · model whisper/whisper-tiny · language en · diarize on · window 300s · segments 2<br>
generated: 2026-01-01 09:00:00
</p>
<table class="transcript">
<tr><td class="ln">1</td><td class="text"><span class="ts">[00:00:00]</span> <span class="speaker">Speaker 0:</span> And so my fellow Americans ask not what your country can do for you.</td></tr>
<tr><td class="ln">2</td><td class="text"><span class="ts">[00:00:10]</span> <span class="speaker">Speaker 2:</span> Ask what you can do for your country.</td></tr>
</table>
</body>
</html>
```

Elements removed for brevity are marked `...`; the stylesheet is a handful of
rules for text, the number column and the timestamp. One table row is one
segment, numbered from 1.

**Timestamps** (`[hh:mm:ss]`) are inserted on the first segment, on every speaker
change, and at most once per interval — 60 s by default, so a long uninterrupted
turn still carries a position every minute while the text stays readable. The
interval is a setting in the desktop app's export options; `0` leaves only the
first segment and the speaker changes stamped. The CLI writes the 60 s default.

**Pauses** are opt-in, in the desktop app only: a gap of at least the chosen
threshold (1 s by default) between two segments becomes its own unnumbered row,
with one dot per whole second of silence, capped at ten. The dots are a marker of
the silence *between recognized segments*; silence inside a segment is not
visible to this export, and the transcript itself is never rewritten to include
them.

**Speaker names** are opt-in as well: the app's Speakers tab lists one field per
speaker id, and a name typed there replaces `Speaker N` in the HTML and WebVTT
exports. A blank field keeps `Speaker N`. The transcript text is untouched — a
name is a label on the export, not a correction of what the model said.

The document records the model, the language, whether diarization was requested,
the window and the segment count; it records nothing about accuracy, because this
project publishes no accuracy figure.

**Text that looks like markup** is treated as text. The transcript is whatever
the audio said, so it can contain `<`, `&`, `{` or a backtick — and the same is
true of a speaker name typed in the app and of the audio file's own name. The
HTML and WebVTT exports escape those characters (`&lt;`, `&amp;`), the ASS export
escapes its `{...}` override blocks, and the Markdown note puts the source, model
and language inside a code fence long enough to survive a backtick in the value.
SRT, plain and JSON keep the text, but on one line per segment: a line break
inside a cue would end it, and let the rest of the sentence pose as a new cue or
as a header. The same text printed to the console is stripped of control
characters (ESC, CR, DEL, the C1 range and the invisible bidi overrides), because
a terminal reads those as instructions rather than as text. None of this changes
what was transcribed; it changes only how a consumer is allowed to read it.

**What it is not.** This is a static report, not an editable transcript. The
desktop app has no editor: there is no audio-follows-cursor playback and no
search-and-replace over the transcript. The line numbers exist so a reader can
cite a position, not so the file can be edited in place.

`html`, `vtt`, `srt` and `ass` apply to a transcription (ASR) model only; the CLI
refuses them for a model that serves another role and points at `plain` or `json`
instead.

## Speaker attribution in the output

With a diarization model, the attribution appears per format as `Speaker N:`,
`<v Speaker N>` or `"speaker":N`. Which models can do it, and what you get on one
that cannot, is on [Long audio and speaker diarization](diarization.md).
