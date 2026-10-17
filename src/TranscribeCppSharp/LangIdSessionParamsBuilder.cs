namespace TranscribeCppSharp;

using System;
using System.Runtime.InteropServices;
using TranscribeCppSharp.Interop;

/// <summary>
/// Fluent builder for a language-identification session's parameters
/// (<c>transcribe_langid_session_params</c>).
/// </summary>
public sealed class LangIdSessionParamsBuilder : IDisposable
{
    private readonly IntPtr handle;
    private LangidSessionParams @params;
    private bool disposed;

    /// <inheritdoc/>
    public LangIdSessionParamsBuilder()
    {
        AbiValidation.ValidateSize<LangidSessionParams>(AbiStruct.AbiLangidSessionParams, nameof(LangidSessionParams));
        handle = Marshal.AllocHGlobal(Marshal.SizeOf<LangidSessionParams>());
        NativeMethods.LangidSessionParamsInit(handle);
        @params = Marshal.PtrToStructure<LangidSessionParams>(handle);
    }

    /// <summary>Number of CPU threads. 0 = library default.</summary>
    public LangIdSessionParamsBuilder WithThreads(int nThreads)
    {
        @params.nThreads = nThreads;
        return this;
    }

    internal IntPtr Build()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        Marshal.StructureToPtr(@params, handle, false);
        return handle;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!disposed)
        {
            Marshal.FreeHGlobal(handle);
            disposed = true;
        }
    }
}
