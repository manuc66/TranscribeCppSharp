#nullable enable

using System;
using System.Runtime.InteropServices;
using TranscribeCppSharp.Interop;

namespace TranscribeCppSharp;

/// <summary>
/// Fluent builder for transcription run parameters.
/// </summary>
public sealed class RunParamsBuilder : IDisposable
{
    private IntPtr _handle;
    private RunParams _params;
    private IntPtr _languagePtr;
    private IntPtr _targetLanguagePtr;
    private bool _disposed;

    public RunParamsBuilder()
    {
        var abiSize = (int)NativeMethods.AbiStructSize(AbiStruct.AbiRunParams);
        var csSize = Marshal.SizeOf<RunParams>();
        if (csSize != abiSize)
            throw new InvalidOperationException(
                $"ABI struct size mismatch for RunParams: C# expects {csSize} bytes, native reports {abiSize} bytes. " +
                $"Regenerate bindings or update the struct definition.");
        _handle = Marshal.AllocHGlobal(abiSize);
        NativeMethods.RunParamsInit(_handle);
        _params = Marshal.PtrToStructure<RunParams>(_handle);
    }

    /// <summary>Task: transcribe or translate to English.</summary>
    public RunParamsBuilder WithTask(TranscribeCppSharp.Interop.Task task)
    {
        _params.task = task;
        return this;
    }

    /// <summary>Timestamp granularity.</summary>
    public RunParamsBuilder WithTimestamps(TimestampKind timestamps)
    {
        _params.timestamps = timestamps;
        return this;
    }

    /// <summary>Punctuation and capitalization mode.</summary>
    public RunParamsBuilder WithPnc(PncMode pnc)
    {
        _params.pnc = pnc;
        return this;
    }

    /// <summary>Inverse text normalization mode.</summary>
    public RunParamsBuilder WithItn(ItnMode itn)
    {
        _params.itn = itn;
        return this;
    }

    /// <summary>Source language of the audio (e.g. "fr", "en", "auto").</summary>
    public RunParamsBuilder WithLanguage(string language)
    {
        FreePtr(ref _languagePtr);
        _languagePtr = Marshal.StringToCoTaskMemUTF8(language);
        _params.language = _languagePtr;
        return this;
    }

    /// <summary>Target language for translation.</summary>
    public RunParamsBuilder WithTargetLanguage(string language)
    {
        FreePtr(ref _targetLanguagePtr);
        _targetLanguagePtr = Marshal.StringToCoTaskMemUTF8(language);
        _params.targetLanguage = _targetLanguagePtr;
        return this;
    }

    internal IntPtr Handle
    {
        get
        {
            Marshal.StructureToPtr(_params, _handle, false);
            return _handle;
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            FreePtr(ref _languagePtr);
            FreePtr(ref _targetLanguagePtr);
            Marshal.FreeHGlobal(_handle);
            _disposed = true;
        }
    }

    private static void FreePtr(ref IntPtr ptr)
    {
        if (ptr != IntPtr.Zero)
        {
            Marshal.FreeCoTaskMem(ptr);
            ptr = IntPtr.Zero;
        }
    }
}
