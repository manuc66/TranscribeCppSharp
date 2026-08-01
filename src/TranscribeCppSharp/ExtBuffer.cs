#nullable enable

using System;
using System.Runtime.InteropServices;

namespace TranscribeCppSharp;

/// <summary>
/// Owns the native memory and managed snapshot of an extension-parameter struct
/// shared by the fluent <c>*ExtBuilder</c> types. Encapsulates the allocate →
/// native-init → snapshot → size-guard → write-back → free lifecycle that the
/// five extension builders previously duplicated.
/// </summary>
/// <typeparam name="T">The native ext struct (must embed an <c>Ext</c> header).</typeparam>
internal sealed class ExtBuffer<T> : IDisposable
    where T : struct
{
    private readonly IntPtr handle;
    private readonly string typeName;
    private T @params;
    private bool disposed;

    /// <summary>
    /// Allocate an unmanaged buffer, ask the native library to initialize it,
    /// snapshot it into the managed struct, and verify the native-reported size
    /// (first field of the embedded <c>Ext</c> header) matches the C# layout.
    /// </summary>
    public ExtBuffer(Action<IntPtr> init, Func<T, ulong> nativeSize, string typeName)
    {
        var size = Marshal.SizeOf<T>();
        handle = Marshal.AllocHGlobal(size);
        init(handle);
        @params = Marshal.PtrToStructure<T>(handle)!;
        this.typeName = typeName;

        var reportedSize = nativeSize(@params);
        if (reportedSize != (ulong)size)
        {
            throw new InvalidOperationException(
                $"ABI struct size mismatch for {typeName}: C# expects {size} bytes, native reports {reportedSize} bytes. " +
                "Regenerate bindings or update the struct definition.");
        }
    }

    /// <summary>Mutable snapshot of the native struct; mutated by the fluent builders.</summary>
    public ref T Params => ref @params;

    /// <summary>Write the snapshot back to unmanaged memory and return the buffer.</summary>
    public IntPtr Build()
    {
#pragma warning disable CA1513 // Keep the builder-specific typeName: ObjectDisposedException.ThrowIf would report "ExtBuffer`1" instead of e.g. "WhisperExtBuilder"
        if (disposed)
        {
            throw new ObjectDisposedException(typeName);
        }
#pragma warning restore CA1513

        Marshal.StructureToPtr(@params, handle, false);
        return handle;
    }

    public void Dispose()
    {
        if (!disposed)
        {
            Marshal.FreeHGlobal(handle);
            disposed = true;
        }
    }
}
