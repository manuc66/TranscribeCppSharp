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
    /// <summary>Native library version string (e.g. "0.1.3").</summary>
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
    /// Initialize backends with a specific artifact directory (for DLLs, shaders, etc.).
    /// </summary>
    public static void Init(string artifactDir)
    {
        var status = NativeMethods.InitBackends(artifactDir);
        if (status != Status.Ok)
        {
            throw new TranscribeException(status, nameof(NativeMethods.InitBackends));
        }
    }

    /// <summary>
    /// Enumerate all available compute devices.
    /// </summary>
    public static IReadOnlyList<BackendDevice> EnumerateDevices()
    {
        var count = NativeMethods.BackendDeviceCount();
        if (count <= 0)
        {
            return [];
        }

        var devices = new List<BackendDevice>(count);
        var deviceSize = (int)NativeMethods.AbiStructSize(AbiStruct.AbiBackendDevice);
        StackAllocHelper.RunWithBuffer(deviceSize, devicePtr =>
        {
            for (int i = 0; i < count; i++)
            {
                devices.Add(ReadDeviceAt(devicePtr, i));
            }
        });

        return devices;
    }

    private static BackendDevice ReadDeviceAt(IntPtr devicePtr, int index)
    {
        NativeMethods.BackendDeviceInit(devicePtr);
        var status = NativeMethods.GetBackendDevice(index, devicePtr);
        if (status != Status.Ok)
        {
            throw new TranscribeException(status, nameof(NativeMethods.GetBackendDevice));
        }

        return ConvertDevice(devicePtr);
    }

    private static BackendDevice ConvertDevice(IntPtr devicePtr)
    {
        var d = Marshal.PtrToStructure<Interop.BackendDevice>(devicePtr);
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
            DeviceType: d.deviceType);
    }

    private static string PtrToStringOrEmpty(IntPtr ptr)
        => ptr != IntPtr.Zero ? Marshal.PtrToStringUTF8(ptr) ?? string.Empty : string.Empty;
}
