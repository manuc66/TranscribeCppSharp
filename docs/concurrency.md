---
title: Concurrency
nav_order: 10
---

# Concurrency

## Concurrency model

All transcription calls (`Session.Run`, `Batch.Run`, etc.) are **blocking**. This mirrors the native library, whose C API is fully synchronous (no async entry points); the wrapper does not add a "fake" async-over-sync layer on top.

## Recommended patterns

1.  **Desktop/CLI Apps**: Run transcription on a background thread using `Task.Run()` to keep the UI responsive.
2.  **Web APIs (ASP.NET Core)**: Use a pool of `Session` objects combined with a `SemaphoreSlim` to limit concurrent native calls and prevent thread pool starvation.

```csharp
// Example: Pooling sessions in a service
private readonly SemaphoreSlim _semaphore = new(Environment.ProcessorCount);
public async Task<string> TranscribeAsync(float[] pcm)
{
    await _semaphore.WaitAsync();
    try {
        return await Task.Run(() => _session.Run(pcm).FullText);
    } finally {
        _semaphore.Release();
    }
}
```

## Thread safety

The native library and this wrapper are **not** thread-safe by default. The relevant rules:

- **Concurrent compute is limited**: at most one `Session.Run`, `Batch.Run`, or active stream may be in flight across **all sessions of the same model** at a time. Sessions share the model's backend instances and some per-family state, so overlapping runs on the same model race (per the upstream library: corrupted decodes on CPU, command-buffer failures on Metal). This is a **known limitation of the upstream native library in 0.x**, documented in its [public header](https://github.com/handy-computer/transcribe.cpp/blob/v0.3.0/include/transcribe.h) (see "KNOWN 0.x LIMITATION — concurrent COMPUTE"), not something this wrapper imposes or can lift.
  - For **parallel transcription**, load **one model per worker** (each worker gets its own `Model`, hence its own backend instances).
  - **Serialized** use of many sessions on one model (e.g. a session pool behind a mutex) is fully supported.
- **`Model`**: believed **thread-safe** for creating sessions — you can create multiple `Session` objects from a single `Model` instance across different threads, as long as their runs do not overlap (see the concurrent-compute limit above). Not covered by concurrency tests yet.
- **`Session`**: **Not thread-safe**. A session maintains internal state (KV cache) for transcription. Do not run two operations on the same session concurrently; serialize them or use separate sessions.
- **`Batch`**: **Not thread-safe**. Calls into the provided session internally. Use separate sessions for concurrent batch processing.
- **`StreamSession`**: **Not thread-safe**. It is a view over a `Session` and shares its state.

The `Model` verdict is the one to read carefully: "believed" is not "verified". It is
not covered by concurrency tests yet.

## Dispose discipline

> **Dispose discipline**: dispose explicitly (`using`/`Dispose()`) — do not rely on the GC finalizer for cleanup. The native contract requires the model to outlive its sessions, and while a `Session` keeps its parent `Model` alive for the session's lifetime, the **order in which finalizers run during GC-only collection is not guaranteed**. Dispose the `StreamSession`/`Session` before their `Model` (the `using var model; using var session;` declaration order does this). This is consistent with the upstream requirement that `transcribe_model_free` be called only after all derived contexts are freed.

## Memory and disk

> Memory and disk usage depend on the model file, quantization, and backend you use.
> These are not documented here; refer to the model documentation and
> [transcribe.cpp](https://github.com/handy-computer/transcribe.cpp) for accurate numbers.

The memory figure this project *has* measured is the tool's peak RSS on one hour of
audio, 700 MiB → 479 MiB, and it is documented with the measurement conditions and its
non-applicability on [Audio input](audio-input.md#audio-input). Beyond that, memory and
disk depend on the model and backend, and the upstream project is the source of truth.
