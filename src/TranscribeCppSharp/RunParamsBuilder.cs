namespace TranscribeCppSharp;

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using TranscribeCppSharp.Interop;

/// <summary>Task for the transcription engine.</summary>
public enum TranscriptionTask
{
    /// <summary>Transcribe audio in the source language.</summary>
    Transcribe = 0,

    /// <summary>Translate audio to English.</summary>
    Translate = 1,
}

/// <summary>
/// Fluent builder for transcription run parameters.
/// </summary>
public sealed class RunParamsBuilder : IDisposable
{
    private readonly IntPtr handle;
    private readonly List<IntPtr> vocabularyStrings = new();
    private RunParams @params;
    private IntPtr languagePtr;
    private IntPtr targetLanguagePtr;
    private IntPtr vocabularyArray;
    private WhisperExtBuilder? whisperExt;
    private bool disposed;

    /// <inheritdoc/>
    public RunParamsBuilder()
    {
        AbiValidation.ValidateSize<RunParams>(AbiStruct.AbiRunParams, nameof(RunParams));
        handle = Marshal.AllocHGlobal(Marshal.SizeOf<RunParams>());
        NativeMethods.RunParamsInit(handle);
        @params = Marshal.PtrToStructure<RunParams>(handle);
    }

    /// <summary>Task: transcribe or translate to English.</summary>
    public RunParamsBuilder WithTask(TranscriptionTask task)
    {
        @params.task = task switch
        {
            TranscriptionTask.Transcribe => Task.TaskTranscribe,
            TranscriptionTask.Translate => Task.TaskTranslate,
            _ => throw new ArgumentOutOfRangeException(nameof(task)),
        };
        return this;
    }

    /// <summary>Timestamp granularity.</summary>
    public RunParamsBuilder WithTimestamps(TimestampKind timestamps)
    {
        @params.timestamps = timestamps;
        return this;
    }

    /// <summary>Punctuation and capitalization mode.</summary>
    public RunParamsBuilder WithPnc(PncMode pnc)
    {
        @params.pnc = pnc;
        return this;
    }

    /// <summary>Inverse text normalization mode.</summary>
    public RunParamsBuilder WithItn(ItnMode itn)
    {
        @params.itn = itn;
        return this;
    }

    /// <summary>
    /// Speaker-diarization toggle. Non-default values against a model without
    /// <c>FeatureDiarization</c> emit a native WARN and keep default behavior.
    /// </summary>
    public RunParamsBuilder WithDiarize(DiarizeMode diarize)
    {
        @params.diarize = diarize;
        return this;
    }

    /// <summary>
    /// Source language of the audio as a BCP-47-ish short code (e.g. "fr",
    /// "en"). Per the upstream API, the value must be a language code — there
    /// is no "auto" sentinel: pass NULL (do not call this method) to
    /// autodetect, only if the model supports language detection.
    /// </summary>
    public RunParamsBuilder WithLanguage(string language)
    {
        FreePtr(ref languagePtr);
        languagePtr = Marshal.StringToCoTaskMemUTF8(language);
        @params.language = languagePtr;
        return this;
    }

    /// <summary>Target language for translation.</summary>
    public RunParamsBuilder WithTargetLanguage(string language)
    {
        FreePtr(ref targetLanguagePtr);
        targetLanguagePtr = Marshal.StringToCoTaskMemUTF8(language);
        @params.targetLanguage = targetLanguagePtr;
        return this;
    }

    /// <summary>Whether to keep special tags (e.g. &lt;|notimestamps|&gt;) in the output.</summary>
    public RunParamsBuilder WithKeepSpecialTags(bool keep)
    {
        @params.keepSpecialTags = keep;
        return this;
    }

    /// <summary>Number of speculative decoding drafts (0 to disable).</summary>
    public RunParamsBuilder WithSpecKDrafts(int drafts)
    {
        @params.specKDrafts = drafts;
        return this;
    }

    /// <summary>
    /// Custom terms the decoder should prefer, in priority order — names, jargon,
    /// acronyms. Each term is trimmed; blank terms are dropped. The strings are
    /// copied into unmanaged memory and released when this builder is disposed.
    /// </summary>
    /// <remarks>
    /// A model that does not support the feature
    /// (<see cref="Interop.Feature.FeatureVocabulary"/>) ignores the list with a
    /// native warning; it is not an error. The size the wrapper passes is the
    /// number of terms it kept.
    /// </remarks>
    public RunParamsBuilder WithVocabulary(IReadOnlyList<string> terms)
    {
        ArgumentNullException.ThrowIfNull(terms);
        FreeVocabulary();

        foreach (string term in terms)
        {
            if (!string.IsNullOrWhiteSpace(term))
            {
                vocabularyStrings.Add(Marshal.StringToCoTaskMemUTF8(term.Trim()));
            }
        }

        if (vocabularyStrings.Count > 0)
        {
            vocabularyArray = Marshal.AllocCoTaskMem(IntPtr.Size * vocabularyStrings.Count);
            for (int i = 0; i < vocabularyStrings.Count; i++)
            {
                Marshal.WriteIntPtr(vocabularyArray, i * IntPtr.Size, vocabularyStrings[i]);
            }
        }

        @params.vocabulary = vocabularyArray;
        @params.nVocabulary = vocabularyStrings.Count;
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
        whisperExt?.Dispose();
        whisperExt = ext;
        @params.family = ext.Build();
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
            FreePtr(ref languagePtr);
            FreePtr(ref targetLanguagePtr);
            FreeVocabulary();
            whisperExt?.Dispose();
            whisperExt = null;
            Marshal.FreeHGlobal(handle);
            disposed = true;
        }
    }

    /// <summary>Releases the term strings and the array that points at them.</summary>
    private void FreeVocabulary()
    {
        foreach (IntPtr term in vocabularyStrings)
        {
            Marshal.FreeCoTaskMem(term);
        }

        vocabularyStrings.Clear();

        if (vocabularyArray != IntPtr.Zero)
        {
            Marshal.FreeCoTaskMem(vocabularyArray);
            vocabularyArray = IntPtr.Zero;
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
