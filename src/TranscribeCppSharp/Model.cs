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
    private ModelHandle handle;
    private bool disposed;

    private Model(ModelHandle handle) => this.handle = handle;

    /// <summary>
    /// Load a model from a GGUF file.
    /// </summary>
    public static Model Load(string modelPath, Action<ModelLoadParamsBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(modelPath);

        using var buildParams = new ModelLoadParamsBuilder();
        configure?.Invoke(buildParams);

        var outModel = Marshal.AllocHGlobal(IntPtr.Size);
        try
        {
            var status = NativeMethods.ModelLoadFile(modelPath, buildParams.Build(), outModel);
            if (status != Status.Ok)
            {
                throw new TranscribeException(status, nameof(NativeMethods.ModelLoadFile));
            }

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
        ObjectDisposedException.ThrowIf(disposed, this);
    }

    /// <summary>Create a new transcription session from this model.</summary>
    public Session CreateSession(Action<SessionParamsBuilder>? configure = null)
    {
        ThrowIfDisposed();
        return Session.Create(handle, this, configure);
    }

    /// <summary>
    /// Query a metadata string from the loaded model.
    /// The returned string is a snapshot copy — safe to keep after the call.
    /// The native pointer is borrowed from the model and must not be freed.
    /// </summary>
    public string? GetMetaValue(string key)
    {
        ThrowIfDisposed();
        var ptr = NativeMethods.ModelMetaValStr(handle, key);
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
            var ptr = NativeMethods.ModelArchString(handle);
            var result = ptr == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUTF8(ptr) ?? string.Empty;
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
            var ptr = NativeMethods.ModelVariantString(handle);
            var result = ptr == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUTF8(ptr) ?? string.Empty;
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
            var ptr = NativeMethods.ModelBackend(handle);
            var result = ptr == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUTF8(ptr) ?? string.Empty;
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
            var status = NativeMethods.ModelGetCapabilities(handle, ptr);
            if (status != Status.Ok)
            {
                throw new TranscribeException(status, nameof(NativeMethods.ModelGetCapabilities));
            }

            var caps = Marshal.PtrToStructure<Interop.Capabilities>(ptr);

            var languages = new string[caps.nLanguages];
            for (int i = 0; i < caps.nLanguages; i++)
            {
                var strPtr = Marshal.ReadIntPtr(caps.languages, i * IntPtr.Size);
                languages[i] = strPtr == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUTF8(strPtr) ?? string.Empty;
            }

            var targetLanguages = new string[caps.nTranslateTargetLanguages];
            for (int i = 0; i < caps.nTranslateTargetLanguages; i++)
            {
                var strPtr = Marshal.ReadIntPtr(caps.translateTargetLanguages, i * IntPtr.Size);
                targetLanguages[i] = strPtr == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUTF8(strPtr) ?? string.Empty;
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
        return NativeMethods.ModelSupports(handle, feature);
    }

    /// <summary>
    /// Tokenize text using the model's tokenizer.
    /// </summary>
    /// <param name="text">The text to tokenize.</param>
    /// <param name="initialCapacity">
    /// Initial buffer size in tokens. This is not a limit: if the text needs
    /// more tokens, the buffer is grown automatically and the full result is
    /// returned.
    /// </param>
    /// <returns>Array of token IDs for the entire text.</returns>
    public int[] Tokenize(string text, int initialCapacity = 1024)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(text);

        if (initialCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(initialCapacity), initialCapacity, "Must be greater than zero.");
        }

        // Buffer contract (transcribe.h): a negative return is the negated
        // required buffer size — reallocate and retry. INT_MIN is a hard error.
        // The retry loop is bounded: a healthy native call converges in one or
        // two attempts, so 32 is a generous safety net.
        var capacity = initialCapacity;
        for (int attempt = 0; attempt < 32; attempt++)
        {
            var tokensPtr = Marshal.AllocHGlobal(capacity * sizeof(int));
            try
            {
                var count = NativeMethods.Tokenize(handle, text, tokensPtr, (nuint)capacity);
                if (count == int.MinValue)
                {
                    throw new TranscribeException(Status.ErrInvalidArg, nameof(NativeMethods.Tokenize));
                }

                if (count >= 0)
                {
                    var tokens = new int[count];
                    Marshal.Copy(tokensPtr, tokens, 0, count);
                    return tokens;
                }

                capacity = -count;
            }
            finally
            {
                Marshal.FreeHGlobal(tokensPtr);
            }
        }

        throw new InvalidOperationException(
            $"Native tokenizer did not converge on a buffer size after 32 attempts (last capacity {capacity}).");
    }

    internal ModelHandle Handle => handle;

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!disposed)
        {
            handle.Dispose();
            disposed = true;
        }
    }
}
