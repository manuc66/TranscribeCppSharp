#nullable enable

using System;
using System.Runtime.InteropServices;
using TranscribeCppSharp.Interop;

namespace TranscribeCppSharp;

/// <summary>Task for the transcription engine.</summary>
public enum TranscriptionTask
{
    /// <summary>Transcribe audio in the source language.</summary>
    Transcribe = 0,
    /// <summary>Translate audio to English.</summary>
    Translate = 1
}

/// <summary>
/// Fluent builder for transcription run parameters.
/// </summary>
public sealed class RunParamsBuilder : IDisposable
{
    private IntPtr _handle;
    private RunParams _params;
    private IntPtr _languagePtr;
    private IntPtr _targetLanguagePtr;
    private WhisperExtBuilder? _whisperExt;
    private bool _disposed;

    public RunParamsBuilder()
    {
        AbiValidation.ValidateSize<RunParams>(AbiStruct.AbiRunParams, nameof(RunParams));
        _handle = Marshal.AllocHGlobal(Marshal.SizeOf<RunParams>());
        NativeMethods.RunParamsInit(_handle);
        _params = Marshal.PtrToStructure<RunParams>(_handle);
    }

    /// <summary>Task: transcribe or translate to English.</summary>
    public RunParamsBuilder WithTask(TranscriptionTask task)
    {
        _params.task = task switch
        {
            TranscriptionTask.Transcribe => Interop.Task.TaskTranscribe,
            TranscriptionTask.Translate => Interop.Task.TaskTranslate,
            _ => throw new ArgumentOutOfRangeException(nameof(task))
        };
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

    /// <summary>Whether to keep special tags (e.g. &lt;|notimestamps|&gt;) in the output.</summary>
    public RunParamsBuilder WithKeepSpecialTags(bool keep)
    {
        _params.keepSpecialTags = keep;
        return this;
    }

    /// <summary>Number of speculative decoding drafts (0 to disable).</summary>
    public RunParamsBuilder WithSpecKDrafts(int drafts)
    {
        _params.specKDrafts = drafts;
        return this;
    }

    /// <summary>
    /// Whisper-specific extension parameters (prompt, temperature, etc.).
    /// Takes ownership of the builder — it will be disposed when this
    /// RunParamsBuilder is disposed or when another ext is set.
    /// Do not reuse the WhisperExtBuilder after passing it here.
    /// </summary>
    public RunParamsBuilder WithWhisperExt(WhisperExtBuilder ext)
    {
        ArgumentNullException.ThrowIfNull(ext);
        _whisperExt?.Dispose();
        _whisperExt = ext;
        _params.family = ext.Build();
        return this;
    }

    internal IntPtr Build()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(RunParamsBuilder));
        Marshal.StructureToPtr(_params, _handle, false);
        return _handle;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            FreePtr(ref _languagePtr);
            FreePtr(ref _targetLanguagePtr);
            _whisperExt?.Dispose();
            _whisperExt = null;
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
