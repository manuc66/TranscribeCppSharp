#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using TranscribeCppSharp.Interop;

namespace TranscribeCppSharp;

/// <summary>
/// A compute backend device (CPU, GPU, iGPU).
/// </summary>
public record BackendDevice(
    string Name,
    string Description,
    string Kind,
    string DeviceId,
    ulong MemoryTotal,
    ulong MemoryFree,
    DeviceType DeviceType);

/// <summary>
/// Static API for backend initialization and device enumeration.
/// </summary>
public static class Backends
{
    /// <summary>
    /// Initialize all available backends with default settings.
    /// Call once at application startup.
    /// </summary>
    public static void InitDefault()
    {
        var status = NativeMethods.InitBackendsDefault();
        if (status != Status.Ok)
            throw new TranscribeException(status, nameof(NativeMethods.InitBackendsDefault));
    }

    /// <summary>
    /// Initialize backends with a specific artifact directory (for DLLs, shaders, etc.).
    /// </summary>
    public static void Init(string artifactDir)
    {
        var status = NativeMethods.InitBackends(artifactDir);
        if (status != Status.Ok)
            throw new TranscribeException(status, nameof(NativeMethods.InitBackends));
    }

    /// <summary>
    /// Enumerate all available compute devices.
    /// </summary>
    public static unsafe IReadOnlyList<BackendDevice> EnumerateDevices()
    {
        var count = NativeMethods.BackendDeviceCount();
        if (count <= 0) return [];

        var devices = new List<BackendDevice>(count);
        var deviceSize = (int)NativeMethods.AbiStructSize(AbiStruct.AbiBackendDevice);
        StackAllocHelper.ThrowIfTooLarge(deviceSize, nameof(BackendDevice));
        Span<byte> buffer = stackalloc byte[deviceSize];

        fixed (byte* pBuffer = buffer)
        {
            var devicePtr = (IntPtr)pBuffer;

            for (int i = 0; i < count; i++)
            {
                NativeMethods.BackendDeviceInit(devicePtr);
                var status = NativeMethods.GetBackendDevice(i, devicePtr);
                if (status != Status.Ok) continue;

                var d = Marshal.PtrToStructure<Interop.BackendDevice>(devicePtr);
                var name = d.name != IntPtr.Zero ? Marshal.PtrToStringUTF8(d.name) ?? "" : "";
                var description = d.description != IntPtr.Zero ? Marshal.PtrToStringUTF8(d.description) ?? "" : "";
                var kind = d.kind != IntPtr.Zero ? Marshal.PtrToStringUTF8(d.kind) ?? "" : "";
                var deviceId = d.deviceId != IntPtr.Zero ? Marshal.PtrToStringUTF8(d.deviceId) ?? "" : "";

                devices.Add(new BackendDevice(
                    Name: name,
                    Description: description,
                    Kind: kind,
                    DeviceId: deviceId,
                    MemoryTotal: d.memoryTotal,
                    MemoryFree: d.memoryFree,
                    DeviceType: d.deviceType));
            }
        }

        return devices;
    }
}
