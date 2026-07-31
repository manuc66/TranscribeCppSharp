#nullable enable

using System;
using System.Runtime.InteropServices;
using TranscribeCppSharp.Interop;

namespace TranscribeCppSharp;

/// <summary>
/// A loaded transcription model. Disposing this frees the native model.
/// Thread-safe: safe to create multiple sessions from different threads.
/// </summary>
public sealed class Model : IDisposable
{
    private ModelHandle _handle;
    private bool _disposed;

    private Model(ModelHandle handle) => _handle = handle;

    /// <summary>
    /// Load a model from a GGUF file.
    /// </summary>
    public static Model Load(string modelPath, Action<ModelLoadParamsBuilder>? configure = null)
    {
        if (modelPath == null)
            throw new ArgumentNullException(nameof(modelPath));

        using var buildParams = new ModelLoadParamsBuilder();
        configure?.Invoke(buildParams);

        var outModel = Marshal.AllocHGlobal(IntPtr.Size);
        try
        {
            var status = NativeMethods.ModelLoadFile(modelPath, buildParams.Build(), outModel);
            if (status != Status.Ok)
                throw new TranscribeException(status, nameof(NativeMethods.ModelLoadFile));

            var handle = new ModelHandle(Marshal.ReadIntPtr(outModel));
            return new Model(handle);
        }
        finally
        {
            Marshal.FreeHGlobal(outModel);
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(Model));
    }

    /// <summary>Create a new transcription session from this model.</summary>
    public Session CreateSession(Action<SessionParamsBuilder>? configure = null)
    {
        ThrowIfDisposed();
        return Session.Create(_handle, this, configure);
    }

    /// <summary>
    /// Query a metadata string from the loaded model.
    /// The returned string is a snapshot copy — safe to keep after the call.
    /// The native pointer is borrowed from the model and must not be freed.
    /// </summary>
    public string? GetMetaValue(string key)
    {
        ThrowIfDisposed();
        var ptr = NativeMethods.ModelMetaValStr(_handle, key);
        return ptr == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(ptr);
    }

    /// <summary>
    /// Architecture of the model (e.g. "whisper").
    /// Returns a managed copy of the native string — safe to keep.
    /// </summary>
    public string Architecture
    {
        get
        {
            ThrowIfDisposed();
            var ptr = NativeMethods.ModelArchString(_handle);
            var result = ptr == IntPtr.Zero ? "" : Marshal.PtrToStringUTF8(ptr) ?? "";
            GC.KeepAlive(this);
            return result;
        }
    }

    /// <summary>
    /// Variant of the architecture (e.g. "tiny").
    /// Returns a managed copy of the native string — safe to keep.
    /// </summary>
    public string Variant
    {
        get
        {
            ThrowIfDisposed();
            var ptr = NativeMethods.ModelVariantString(_handle);
            var result = ptr == IntPtr.Zero ? "" : Marshal.PtrToStringUTF8(ptr) ?? "";
            GC.KeepAlive(this);
            return result;
        }
    }

    /// <summary>
    /// Name of the backend being used (e.g. "cpu").
    /// Returns a managed copy of the native string — safe to keep.
    /// </summary>
    public string Backend
    {
        get
        {
            ThrowIfDisposed();
            var ptr = NativeMethods.ModelBackend(_handle);
            var result = ptr == IntPtr.Zero ? "" : Marshal.PtrToStringUTF8(ptr) ?? "";
            GC.KeepAlive(this);
            return result;
        }
    }

    /// <summary>Model capabilities.</summary>
    public record ModelCapabilities(
        int NativeSampleRate,
        string[] Languages,
        TimestampKind MaxTimestampKind,
        bool SupportsLanguageDetect,
        bool SupportsTranslate,
        bool SupportsStreaming,
        bool SupportsSpecDecode,
        long MaxAudioMs,
        string[] TranslateTargetLanguages);

    /// <summary>Get capabilities of the loaded model.</summary>
    public ModelCapabilities GetCapabilities()
    {
        ThrowIfDisposed();
        var size = (int)NativeMethods.AbiStructSize(AbiStruct.AbiCapabilities);
        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            NativeMethods.CapabilitiesInit(ptr);
            var status = NativeMethods.ModelGetCapabilities(_handle, ptr);
            if (status != Status.Ok)
                throw new TranscribeException(status, nameof(NativeMethods.ModelGetCapabilities));

            var caps = Marshal.PtrToStructure<Interop.Capabilities>(ptr);

            var languages = new string[caps.nLanguages];
            for (int i = 0; i < caps.nLanguages; i++)
            {
                var strPtr = Marshal.ReadIntPtr(caps.languages, i * IntPtr.Size);
                languages[i] = strPtr == IntPtr.Zero ? "" : Marshal.PtrToStringUTF8(strPtr) ?? "";
            }

            var targetLanguages = new string[caps.nTranslateTargetLanguages];
            for (int i = 0; i < caps.nTranslateTargetLanguages; i++)
            {
                var strPtr = Marshal.ReadIntPtr(caps.translateTargetLanguages, i * IntPtr.Size);
                targetLanguages[i] = strPtr == IntPtr.Zero ? "" : Marshal.PtrToStringUTF8(strPtr) ?? "";
            }

            return new ModelCapabilities(
                NativeSampleRate: caps.nativeSampleRate,
                Languages: languages,
                MaxTimestampKind: caps.maxTimestampKind,
                SupportsLanguageDetect: caps.supportsLanguageDetect,
                SupportsTranslate: caps.supportsTranslate,
                SupportsStreaming: caps.supportsStreaming,
                SupportsSpecDecode: caps.supportsSpecDecode,
                MaxAudioMs: caps.maxAudioMs,
                TranslateTargetLanguages: targetLanguages);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    /// <summary>Check if the model supports a given feature.</summary>
    public bool Supports(Feature feature)
    {
        ThrowIfDisposed();
        return NativeMethods.ModelSupports(_handle, feature);
    }

    /// <summary>
    /// Tokenize text using the model's tokenizer.
    /// </summary>
    /// <param name="text">The text to tokenize.</param>
    /// <param name="maxTokens">Maximum number of tokens to output.</param>
    /// <returns>Array of token IDs.</returns>
    public int[] Tokenize(string text, int maxTokens = 1024)
    {
        ThrowIfDisposed();
        if (text == null)
            throw new ArgumentNullException(nameof(text));
        if (maxTokens <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxTokens), maxTokens, "Must be greater than zero.");

        var tokensPtr = Marshal.AllocHGlobal(maxTokens * sizeof(int));
        try
        {
            var count = NativeMethods.Tokenize(_handle, text, tokensPtr, (nuint)maxTokens);
            if (count < 0)
                throw new TranscribeException(Status.ErrInvalidArg, nameof(NativeMethods.Tokenize));
            if (count > maxTokens)
                count = maxTokens;

            var tokens = new int[count];
            Marshal.Copy(tokensPtr, tokens, 0, count);
            return tokens;
        }
        finally
        {
            Marshal.FreeHGlobal(tokensPtr);
        }
    }

    internal ModelHandle Handle => _handle;

    public void Dispose()
    {
        if (!_disposed)
        {
            _handle.Dispose();
            _disposed = true;
        }
    }
}
