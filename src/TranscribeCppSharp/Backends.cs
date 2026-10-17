namespace TranscribeCppSharp;

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using TranscribeCppSharp.Interop;

/// <summary>
/// Static API for backend initialization, device enumeration, and version info.
/// </summary>
public static class Backends
{
    /// <summary>Native library version string (e.g. "0.2.3").</summary>
    public static string Version
    {
        get
        {
            var ptr = NativeMethods.Version();
            return ptr == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUTF8(ptr) ?? string.Empty;
        }
    }

    /// <summary>Native library git commit hash.</summary>
    public static string VersionCommit
    {
        get
        {
            var ptr = NativeMethods.VersionCommit();
            return ptr == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUTF8(ptr) ?? string.Empty;
        }
    }

    /// <summary>
    /// Initialize all available backends with default settings.
    /// Call once at application startup.
    /// </summary>
    public static void InitDefault()
    {
        var status = NativeMethods.InitBackendsDefault();
        if (status != Status.Ok)
        {
            throw new TranscribeException(status, nameof(NativeMethods.InitBackendsDefault));
        }

        initialized = true;
    }

    /// <summary>
    /// Initialize backends with default settings, restricted to
    /// <paramref name="allowed"/>. Upstream 0.4 added the allowlist; a mask of
    /// <see cref="BackendMask.All"/> is the default behavior.
    /// </summary>
    public static void InitDefault(BackendMask allowed)
        => InitCore(artifactDir: null, allowed);

    /// <summary>
    /// Initialize backends with a specific artifact directory (for DLLs, shaders, etc.).
    /// </summary>
    public static void Init(string artifactDir)
    {
        var status = NativeMethods.InitBackends(artifactDir);
        if (status != Status.Ok)
        {
            throw new TranscribeException(status, nameof(NativeMethods.InitBackends));
        }

        initialized = true;
    }

    /// <summary>
    /// Initialize backends with a specific artifact directory, restricted to
    /// <paramref name="allowed"/> (upstream 0.4 allowlist).
    /// </summary>
    public static void Init(string artifactDir, BackendMask allowed)
        => InitCore(artifactDir, allowed);

    /// <summary>
    /// The backend allowlist currently in effect
    /// (<c>transcribe_allowed_backends</c>). Before initialization this is the
    /// library default; after, what was requested.
    /// </summary>
    public static BackendMask AllowedBackends => (BackendMask)NativeMethods.AllowedBackends();

    private static void InitCore(string? artifactDir, BackendMask allowed)
    {
        var size = (int)NativeMethods.AbiStructSize(AbiStruct.AbiBackendInitParams);
        var dirPtr = artifactDir is null ? IntPtr.Zero : Marshal.StringToCoTaskMemUTF8(artifactDir);
        try
        {
            StackAllocHelper.RunWithBuffer(size, ptr =>
            {
                NativeMethods.BackendInitParamsInit(ptr);
                var p = Marshal.PtrToStructure<BackendInitParams>(ptr);
                p.artifactDir = dirPtr;
                p.allowedBackends = (uint)allowed;
                Marshal.StructureToPtr(p, ptr, false);

                var status = NativeMethods.InitBackendsEx(ptr);
                if (status != Status.Ok)
                {
                    throw new TranscribeException(status, nameof(NativeMethods.InitBackendsEx));
                }
            });

            initialized = true;
        }
        finally
        {
            if (dirPtr != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(dirPtr);
            }
        }
    }

    private static bool initialized;

    /// <summary>
    /// Initialize backends on demand (idempotent). Called by <see cref="Model.Load"/>
    /// so a plain load works without an explicit <see cref="InitDefault()"/> first.
    /// </summary>
    internal static void EnsureInitialized()
    {
        if (!initialized)
        {
            InitDefault();
        }
    }

    /// <summary>
    /// Whether a backend request can be satisfied by a currently registered
    /// device. AUTO is true whenever any device exists; CPU/CPU_ACCEL when a
    /// CPU device exists; METAL/VULKAN/CUDA when a device of that kind exists.
    /// Unknown or invalid request values answer false (never an error).
    /// Corresponds to native transcribe_backend_available.
    /// </summary>
    public static bool BackendAvailable(BackendRequest kind)
        => NativeMethods.BackendAvailable(kind);

    /// <summary>
    /// Enumerate all available compute devices.
    /// Handles are runtime-owned and valid for the life of the process.
    /// </summary>
    public static IReadOnlyList<BackendDevice> EnumerateDevices()
    {
        var count = NativeMethods.DeviceCount();
        if (count <= 0)
        {
            return [];
        }

        var devices = new List<BackendDevice>(count);
        var deviceSize = (int)NativeMethods.AbiStructSize(AbiStruct.AbiDeviceInfo);
        StackAllocHelper.RunWithBuffer(deviceSize, devicePtr =>
        {
            for (int i = 0; i < count; i++)
            {
                var handle = NativeMethods.DeviceGet(i);
                if (handle == IntPtr.Zero)
                {
                    continue;
                }

                devices.Add(ReadDeviceInfo(handle, devicePtr));
            }
        });

        return devices;
    }

    private static BackendDevice ReadDeviceInfo(IntPtr handle, IntPtr devicePtr)
    {
        NativeMethods.DeviceInfoInit(devicePtr);
        var status = NativeMethods.DeviceGetInfo(handle, devicePtr);
        if (status != Status.Ok)
        {
            throw new TranscribeException(status, nameof(NativeMethods.DeviceGetInfo));
        }

        return ConvertDevice(handle, devicePtr);
    }

    /// <summary>
    /// Resolve metadata for a runtime-owned device handle (e.g. from
    /// <see cref="Model.Device"/>). Returns null when handle is zero.
    /// </summary>
    public static BackendDevice? GetDeviceInfo(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        BackendDevice? result = null;
        var deviceSize = (int)NativeMethods.AbiStructSize(AbiStruct.AbiDeviceInfo);
        StackAllocHelper.RunWithBuffer(deviceSize, devicePtr =>
        {
            result = ReadDeviceInfo(handle, devicePtr);
        });

        return result;
    }

    private static BackendDevice ConvertDevice(IntPtr handle, IntPtr devicePtr)
    {
        var d = Marshal.PtrToStructure<DeviceInfo>(devicePtr);
        var name = PtrToStringOrEmpty(d.name);
        var description = PtrToStringOrEmpty(d.description);
        var kind = PtrToStringOrEmpty(d.kind);
        var deviceId = PtrToStringOrEmpty(d.deviceId);

        return new BackendDevice(
            Name: name,
            Description: description,
            Kind: kind,
            DeviceId: deviceId,
            MemoryTotal: d.memoryTotal,
            MemoryFree: d.memoryFree,
            DeviceType: d.deviceType)
        {
            Handle = handle,
        };
    }

    private static string PtrToStringOrEmpty(IntPtr ptr)
        => ptr != IntPtr.Zero ? Marshal.PtrToStringUTF8(ptr) ?? string.Empty : string.Empty;
}
