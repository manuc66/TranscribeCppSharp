#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using TranscribeCppSharp.Interop;

namespace TranscribeCppSharp;

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

    private static bool initialized;

    /// <summary>
    /// Initialize backends on demand (idempotent). Called by <see cref="Model.Load"/>
    /// so a plain load works without an explicit <see cref="InitDefault"/> first.
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
        var d = Marshal.PtrToStructure<Interop.DeviceInfo>(devicePtr);
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
