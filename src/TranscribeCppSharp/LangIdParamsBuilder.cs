namespace TranscribeCppSharp;

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using TranscribeCppSharp.Interop;

/// <summary>
/// Fluent builder for language-identification run parameters
/// (<c>transcribe_langid_params</c>).
/// </summary>
public sealed class LangIdParamsBuilder : IDisposable
{
    private readonly IntPtr handle;
    private readonly List<IntPtr> allowedStrings = [];
    private LangidParams @params;
    private IntPtr allowedArray;
    private bool disposed;

    /// <inheritdoc/>
    public LangIdParamsBuilder()
    {
        AbiValidation.ValidateSize<LangidParams>(AbiStruct.AbiLangidParams, nameof(LangidParams));
        handle = Marshal.AllocHGlobal(Marshal.SizeOf<LangidParams>());
        NativeMethods.LangidParamsInit(handle);
        @params = Marshal.PtrToStructure<LangidParams>(handle);
    }

    /// <summary>
    /// Restrict the ranking to <paramref name="codes"/> (language codes or
    /// aliases the model knows). With no restriction the result ranks every label.
    /// The strings are copied; the caller keeps ownership of the input.
    /// </summary>
    public LangIdParamsBuilder WithAllowed(IReadOnlyList<string> codes)
    {
        ArgumentNullException.ThrowIfNull(codes);
        FreeAllowed();

        foreach (string code in codes)
        {
            allowedStrings.Add(Marshal.StringToCoTaskMemUTF8(code));
        }

        if (allowedStrings.Count > 0)
        {
            allowedArray = Marshal.AllocCoTaskMem(IntPtr.Size * allowedStrings.Count);
            for (int i = 0; i < allowedStrings.Count; i++)
            {
                Marshal.WriteIntPtr(allowedArray, i * IntPtr.Size, allowedStrings[i]);
            }
        }

        @params.allowed = allowedArray;
        @params.nAllowed = allowedStrings.Count;
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
            FreeAllowed();
            Marshal.FreeHGlobal(handle);
            disposed = true;
        }
    }

    private void FreeAllowed()
    {
        foreach (IntPtr p in allowedStrings)
        {
            Marshal.FreeCoTaskMem(p);
        }

        allowedStrings.Clear();
        if (allowedArray != IntPtr.Zero)
        {
            Marshal.FreeCoTaskMem(allowedArray);
            allowedArray = IntPtr.Zero;
        }
    }
}
