---
title: Roles and sessions
nav_order: 5.5
---

# Roles and sessions

transcribe.cpp 0.4 gives a loaded model one or more **roles**: a kind of work it
can do. The roles are ASR (speech to text), DIARIZE (speaker turns), LANGID
(language identification) and VAD (voice activity detection). A model serves one
or more of them, fixed at load and never changed for its lifetime.

`Model.Roles` reads them:

```csharp
if (model.Roles.HasFlag(TranscribeRole.Asr))
{
    using var session = model.CreateSession();
    // session.Run(...)
}
```

Entry points that need a role the model lacks throw a `TranscribeException` with
`Status.ErrUnsupportedRole` (20). In particular, `Model.GetCapabilities` is an
**ASR** query in 0.4: on a model without the ASR role it throws rather than
returning a zeroed struct. Ask `Model.Roles` first.

## The four session types

| Role | Create with | Run |
|---|---|---|
| ASR | `Model.CreateSession` | `Session.Run` (offline) / `Session.CreateStream` |
| DIARIZE | `Model.CreateDiarizeSession` | `DiarizeSession.Run` → speaker segments |
| LANGID | `Model.CreateLangIdSession` | `LangIdSession.Run` → ranked language candidates |
| VAD | `Model.CreateVadSession` | `VadSession.Run` → speech probabilities and segments |

`Model.GetDiarizeInfo()`, `GetLangIdInfo()` and `GetVadInfo()` return each role's
own facts (sample rate, and the speaker, label or frame counts), and throw
`ErrUnsupportedRole` for a model without that role.

### Diarization

Diarization is **offline**: upstream has no stream entry point for the DIARIZE
role. A Sortformer model carries its latency/quality preset on the diarize params:

```csharp
using var model = Model.Load("diar_streaming_sortformer_4spk-v2.1-Q8_0.gguf", /* … */);
using var diarize = model.CreateDiarizeSession();
using var preset = new SortformerDiarizeExtBuilder()
    .WithPreset(SortformerPreset.SortformerPresetDefault);
var segments = diarize.Run(pcm, p => p.WithSortformerExt(preset));
```

The speaker-attributing **ASR** models (MOSS, granite, multitalker parakeet) are
unchanged: they still attribute speakers through the run-time toggle described on
[Diarization](diarization.md).

### Language identification and VAD

`LangIdSession.Run` ranks the model's labels (optionally restricted to a set of
codes with `LangIdParamsBuilder.WithAllowed`); `VadSession.Run` returns one speech
probability per frame plus the speech segments, and `Feed`/`Flush`/`Reset` drive
the streaming form.

## Backend allowlist

0.4 lets you restrict which compute backends initialize:

```csharp
Backends.InitDefault(BackendMask.Cpu | BackendMask.Vulkan);
```

`Backends.AllowedBackends` reports the mask in effect.

## What is verified

The role enum, the parameter builders and the role/capability contract are covered
by `UpstreamV04Tests` against a real ASR model: a Whisper model reports `Asr` and
refuses the other three roles with `UNSUPPORTED_ROLE`.

The three sessions are exercised end to end against the real weights, which
`scripts/run-integration-tests.sh` fetches (each pinned to a HuggingFace revision
and verified by sha256):

| Session | Weights | Test |
|---|---|---|
| VAD | Silero VAD v6.2, 1.2 MB | `VadSessionTests` |
| LANGID | VoxLingua107 ECAPA-TDNN Q8_0, 27 MB | `LangIdSessionTests` |
| DIARIZE | Sortformer Q8_0, 139 MB | `DiarizeSessionTests` |

They cover the role check, the per-role info, a run and its results, the VAD
streaming form, timings, disposal, rejection of non-finite audio, and cancellation
(both before the call and mid-run). The JFK sample is the input; the assertions are
properties (well-formed segments, English identified, the allowed set respected),
not recorded golden values that a model change would invalidate.
