---
title: Audio input
nav_order: 8
---

# Audio input

16 kHz mono 16-bit WAV is read directly. Any other format is decoded by shelling
out to `ffmpeg`, which must be installed and on `PATH`. The decoded audio is
staged in a temporary file (removed afterwards), so decoding needs write access
to the system temp directory.

Verified to decode, and covered by tests:

| kind | tested |
|---|---|
| audio containers/codecs | WAV, MP3, Ogg (Vorbis), Opus, M4A (AAC), ADTS AAC, FLAC |
| sample rates | 8 kHz, 16 kHz, 44.1 kHz, 48 kHz, 96 kHz |
| bit depths | 16-bit, 24-bit, 32-bit float |
| channel counts | mono, stereo, 8-channel |
| video containers | MP4 (H.264+AAC), MKV (H.264+AC3), WebM (VP9+Opus) |

Video input is read for its audio track; a file with no audio track is reported
as one line. When a video has several audio tracks, ffmpeg's default stream is
used — the first one — and there is no flag to choose another.

The list above is what the tests decode, not a claim about what ffmpeg can
decode. A format outside it is not refused — it is simply unverified here.

Audio is read one window at a time rather than loaded whole, so peak memory does
not grow with the length of the file: one hour of 16 kHz mono audio is 219 MiB
held as a single array, and it is never held. Measured peak RSS of the tool on
one hour of audio, before and after: 700 MiB → 479 MiB. The saving is in
proportion to the duration, so short files gain nothing.

Convert it yourself if you prefer:

```bash
ffmpeg -i input.mp3 -ar 16000 -ac 1 output.wav
```
