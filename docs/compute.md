---
title: Compute
nav_order: 4
---

# Compute

## The GPU is used by default

`transcribe` runs on the GPU whenever one initializes — no flag needed. The
`compute:` line in the console output reports the backend the model **actually**
landed on (from the native `transcribe_model_backend`), so you can see it rather
than assume it. The default is the upstream `AUTO` policy: every discrete GPU is
probed before the integrated ones, and the CPU is the last-resort fallback. A
machine with no usable GPU still works; the line then says so explicitly.

To pin it, or to force a backend:

```bash
transcribe jfk.wav --list-devices          # what this build/machine can see
transcribe jfk.wav --backend cpu           # strict CPU, no GPU
transcribe jfk.wav --backend cuda          # require CUDA (errors if unavailable)
transcribe jfk.wav --device 1              # that exact device, from the list
```

`--list-devices` output (the columns and layout are the real format; the devices
shown are illustrative, not a specific machine):

```text
index kind     type       memory  device
0     vulkan   GPU        8.0 GB  NVIDIA RTX 4090
1     vulkan   IGPU      11.4 GB  Intel Iris Xe
2     cpu      CPU       31.2 GB  AMD Ryzen 9 7950X
```

`--backend` accepts `auto` (default), `cpu`, `cpu-accel`, `metal`, `vulkan`,
`cuda`, `rocm`. A **forced** backend or device is never silently retried
elsewhere: if it is unavailable, or does not match, the run fails with a clear
message. The bundled runtimes include Vulkan (Windows/Linux) and Metal (macOS);
CUDA needs your own build (see below). Whether the GPU is *faster* depends on the
model, the audio length and your hardware — the tool reports the device it used
and the measured time, it does not promise a speed-up.

## Backends.InitDefault

`Model.Load` initializes the compute backends automatically on first use, but for
custom setups do it explicitly before loading a model:

```csharp
Backends.InitDefault();
```

## Using CUDA

The NuGet packages do **not** bundle a CUDA runtime (the bundled binaries are
CPU + Vulkan on Windows/Linux and Metal on macOS). The upstream releases do
include CUDA archives, but shipping and supporting CUDA builds is out of scope
for this packaging layer — so to use an NVIDIA GPU you provide your own CUDA
build of transcribe.cpp and place it next to your app; the wrapper prefers
native binaries in the app output directory over the packaged ones.

1. **Download** the upstream CUDA archive for your platform (this project is
   bound to transcribe.cpp v0.3.0):

   - Linux x64: `transcribe-native-0.3.0-linux-x86_64-cuda.tar.gz`
   - Windows x64: `transcribe-native-0.3.0-windows-x86_64-cuda.tar.gz`

   from the [transcribe.cpp v0.3.0 release](https://github.com/handy-computer/transcribe.cpp/releases/tag/v0.3.0).

2. **Extract** it and copy `libtranscribe.so` (Linux) or `transcribe.dll`
   (Windows) — plus the sibling `libggml*.so` / `ggml*.dll` files — into your
   app's output directory (where your `.dll`/`.exe` is produced).

3. **Request the CUDA backend** at load time:

   ```csharp
   using var model = Model.Load("model.gguf", p => p
       .WithBackend(BackendRequest.BackendCuda));
   // Exact device: enumerate and pass the handle (omit for automatic selection)
   var cuda = Backends.EnumerateDevices().First(d => d.Kind == "cuda");
   using var modelOnGpu = Model.Load("model.gguf", p => p.WithDevice(cuda));
   ```

   You can verify CUDA is actually available in the current build with
   `BackendAvailable(BackendRequest.BackendCuda)`. If no CUDA build is
   installed, that returns `false` and a `BackendCuda` request will fail with
   `ErrBackend`.

Copying a native build next to your app is also the route for Alpine (musl) and
any other variant upstream does not ship as a package — see
[Building from source](development.md#building-from-source).
