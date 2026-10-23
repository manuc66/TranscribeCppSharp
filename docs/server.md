---
title: OpenAI-compatible server
nav_order: 16
---

# OpenAI-compatible server

`TranscribeCppSharp.Server` exposes one loaded model over HTTP, with the routes
OpenAI's audio API uses, so a client that already speaks that API can point at a
machine running transcribe.cpp.

It is **not** a re-implementation of OpenAI's models and it is not a gateway: one
process serves one model, the one given at startup.

```bash
dotnet run --project src/TranscribeCppSharp.Server -- --model whisper-tiny --allow-download
```

```bash
curl -s http://127.0.0.1:8080/v1/audio/transcriptions \
  -F file=@interview.mp3 \
  -F model=whisper-tiny \
  -F response_format=verbose_json
```

The first run with `--allow-download` fetches the alias from HuggingFace, verifies
its sha256 and caches it — the same cache the `transcribe` CLI uses, so a model
either front end fetched is available to the other.

## Endpoints

| Route | What it does |
|---|---|
| `POST /v1/audio/transcriptions` | multipart: `file`, `model`, `language`, `response_format`, `timestamp_granularities[]`, `diarize` |
| `POST /v1/audio/translations` | the same fields; runs the native translate task |
| `GET /v1/models` | the single model this process serves |
| `GET /health` | liveness, and whether the model is in memory |

`response_format` accepts `json` (default), `text`, `verbose_json`, `srt` and
`vtt`.

`verbose_json` returns `task`, `language`, `duration`, `text`, `segments` and
`words`; a segment carries `speaker` only when attribution actually ran. The
fields OpenAI returns that transcribe.cpp does not produce — `avg_logprob`,
`compression_ratio`, `no_speech_prob`, token arrays — are **absent, not zeroed**.
A client that requires them will see them missing, which is the truthful outcome.

`srt` and `vtt` return the merged segments as cues, from the same renderer the
CLI uses (`src/Shared/SubtitleWriter.cs`); `vtt` is served as `text/vtt` and
`srt` as `application/x-subrip`. Speaker labels follow the request's `diarize`
setting, and cues are the model's own segment boundaries — nothing is re-timed to
look shorter.

Word timestamps are returned only when the model reports it can produce them
(`Capabilities.MaxTimestampKind`); otherwise `words` is an empty list rather than
an error, because the transcript itself is still valid.

## What it deliberately refuses

A request that asks for something this server does not do gets a `400` with a
message naming the field, instead of a `200` that quietly ignored it:

- `prompt` and a non-zero `temperature` — not wired to transcribe.cpp by this
  server;
- any `model` other than the one this process loaded (`404 model_not_found`).

## Concurrency, and why requests queue

transcribe.cpp allows **one run per model at a time**, across all of that model's
sessions. The server therefore serializes every request behind one semaphore: a
second request waits for the first instead of racing it. A long file blocks the
queue for as long as it runs. Raising that ceiling means one process per model on
different ports — not a second thread in the same process.

## Long audio, memory, and the window

Above `--chunk` seconds (default 300, and never above the model's own limit) the
audio is split with a 1 s overlap and the seam is deduplicated, one window in
memory at a time. That is the same math as the CLI, with the same results.

Two things to know before running a long file on a small GPU:

- If a window does not fit in memory, the native layer returns its out-of-memory
  status and the server **halves the window and restarts the file**, down to 5 s,
  for at most ten values. Each retry is logged to stderr. The failed attempt's
  partial transcript is discarded, so the result cannot contain duplicates; the
  cost is re-transcribing the windows that had already succeeded. The policy is
  `WindowAdjustment`, shared with the desktop app.
- Only that out-of-memory status is retried. Any other native failure is returned
  to the client as a `500`; the server never falls back to another backend or
  device.

## Access and safety

- It binds `http://127.0.0.1:8080` by default. `--urls` changes that; binding a
  public interface is your decision.
- Without `--api-key` (or `TRANSCRIBE_SERVER_API_KEY`), **anyone who can reach the
  port can transcribe**. With a key, every route but `/health` requires
  `Authorization: Bearer <key>`. The comparison is fixed-time; this is not a full
  auth system, and `--urls` does not add TLS.
- Downloads are **off** by default: a request cannot make the server fetch
  something from the network unless the process was started with
  `--allow-download`. A path to an existing GGUF always works.
- Uploads are capped at `--max-upload-mb` (default 1024). Files are buffered to
  disk by ASP.NET, then copied to a temporary file that is deleted after the
  request, on success or failure.
- Nothing is logged but request lines and errors; no audio or transcript is
  written to disk by the server itself.

## What it is not

- **Not a streaming endpoint.** There is no SSE and no chunked transcript. The
  native streaming API exists but the library documents its limits; the server
  does not expose it.
- **Not a model router.** One process, one model. `GET /v1/models` lists that one.
- **Not packaged.** There is no NuGet package and no release archive for it yet;
  run it from a clone. It needs the native runtime, which `tools/FetchNative`
  places in the output directory, and `ffmpeg` on `PATH` for any input that is not
  a 16 kHz mono 16-bit WAV.
