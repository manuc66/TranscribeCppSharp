---
title: Error handling
nav_order: 12
---

# Error handling

The high-level wrapper throws `TranscribeException` when a native call fails. You can filter by `StatusCode` to handle specific errors.

*Note: See the `Status` enum in the `TranscribeCppSharp.Interop` namespace for the full list of error codes.*

## Statuses you will meet in practice

Two of them come up often enough to name here, because both are *expected* rather
than exceptional and both are reported rather than swallowed:

- **`ErrBackend`** — a compute backend was requested that this build or this machine
  does not have. This is what `Model.Load` with `BackendCuda` throws on a machine with
  no CUDA build installed, and it is why `BackendAvailable(BackendRequest.BackendCuda)`
  exists as a way to ask first ([Using CUDA](compute.md#using-cuda)). A **forced**
  backend or device never falls back silently to another one.
- **Backend or device mismatch** — the same refusal from the command-line tool: you
  asked for a specific device and the run uses a different one, so it fails with a
  clear message instead of transcribing on hardware you did not choose
  ([Compute](compute.md#the-gpu-is-used-by-default)).

## Missing native library

A missing or unloadable `libtranscribe` is not a `TranscribeException` — it surfaces as
a `DllNotFoundException` that names the exact runtime package to add for your platform
and lists the paths that were searched. That is deliberate: the common cause is a
forgotten platform package, and a bare "not found" would send you looking in the wrong
place. See [Installation](getting-started.md#installation).

## When a model cannot do what you asked

Asking a model for speaker attribution it does not support is **not** an error. The
upstream logs a warning, the run proceeds, and every `SpeakerId` comes back 0 — a
non-DEFAULT mode passed to such a model is accepted, not rejected. Ask
`model.Supports(Feature.FeatureDiarization)` first if you need to know before running;
see [Long audio and speaker diarization](diarization.md).
