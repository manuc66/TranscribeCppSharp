namespace TranscribeCppSharp;

using System;

/// <summary>
/// Which compute backends may be initialized. Values match the upstream
/// <c>TRANSCRIBE_BACKEND_MASK_*</c> constants (include/transcribe.h in
/// transcribe.cpp 0.4).
/// </summary>
/// <remarks>
/// Passed to <see cref="Backends.InitDefault(BackendMask)"/> or
/// <see cref="Backends.Init(string, BackendMask)"/> to restrict initialization
/// (for example, to force CPU on a machine whose GPU driver is unreliable).
/// <see cref="Backends.AllowedBackends"/> reports the mask in effect after
/// initialization.
/// </remarks>
[Flags]
public enum BackendMask : uint
{
    /// <summary>Initialize nothing.</summary>
    None = 0,

    /// <summary>CPU backend.</summary>
    Cpu = 0x00000001,

    /// <summary>Metal (Apple GPU).</summary>
    Metal = 0x00000002,

    /// <summary>Vulkan.</summary>
    Vulkan = 0x00000004,

    /// <summary>CUDA (NVIDIA).</summary>
    Cuda = 0x00000008,

    /// <summary>ROCm (AMD).</summary>
    Rocm = 0x00000010,

    /// <summary>Any other backend the library knows about.</summary>
    Other = 0x80000000,

    /// <summary>Every backend (the default).</summary>
    All = 0xFFFFFFFF,
}
