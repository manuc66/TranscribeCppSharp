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
| `json` | `{"language", "text", "segments":[{start,end,speaker,text}]}` |

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

## Speaker attribution in the output

With a diarization model, the attribution appears per format as `Speaker N:`,
`<v Speaker N>` or `"speaker":N`. Which models can do it, and what you get on one
that cannot, is on [Long audio and speaker diarization](diarization.md).
