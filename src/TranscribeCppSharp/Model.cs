#nullable enable

using System;
using System.Runtime.InteropServices;
using System.Threading;
using TranscribeCppSharp.Interop;

namespace TranscribeCppSharp;

/// <summary>
/// A loaded transcription model. Disposing this frees the native model.
/// Thread-safe: Dispose waits for all in-flight operations to complete before freeing the handle.
/// </summary>
public sealed class Model : IDisposable
{
    private ModelHandle _handle;
    private int _useCount;
    private int _disposed;

    private Model(ModelHandle handle) => _handle = handle;

    /// <summary>
    /// Load a model from a GGUF file.
    /// </summary>
    public static Model Load(string modelPath, Action<ModelLoadParamsBuilder>? configure = null)
    {
        using var buildParams = new ModelLoadParamsBuilder();
        configure?.Invoke(buildParams);

        var outModel = Marshal.AllocHGlobal(IntPtr.Size);
        try
        {
            var status = NativeMethods.ModelLoadFile(modelPath, buildParams.Build(), outModel);
            if (status != Status.Ok)
                throw new TranscribeException(status, nameof(NativeMethods.ModelLoadFile));

            var handle = Marshal.PtrToStructure<ModelHandle>(outModel);
            return new Model(handle);
        }
        finally
        {
            Marshal.FreeHGlobal(outModel);
        }
    }

    private void BeginUse()
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(Model));

        Interlocked.Increment(ref _useCount);

        if (Volatile.Read(ref _disposed) != 0)
        {
            Interlocked.Decrement(ref _useCount);
            throw new ObjectDisposedException(nameof(Model));
        }
    }

    private void EndUse()
    {
        Interlocked.Decrement(ref _useCount);
    }

    /// <summary>Create a new transcription session from this model.</summary>
    public Session CreateSession(Action<SessionParamsBuilder>? configure = null)
    {
        BeginUse();
        try
        {
            return Session.Create(_handle, configure);
        }
        finally
        {
            EndUse();
        }
    }

    /// <summary>
    /// Query a metadata string from the loaded model.
    /// The returned string is a snapshot copy — safe to keep after the call.
    /// The native pointer is borrowed from the model and must not be freed.
    /// </summary>
    public string? GetMetaValue(string key)
    {
        BeginUse();
        try
        {
            var ptr = NativeMethods.ModelMetaValStr(_handle, key);
            return ptr == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(ptr);
        }
        finally
        {
            EndUse();
        }
    }

    /// <summary>Check if the model supports a given feature.</summary>
    public bool Supports(Feature feature)
    {
        BeginUse();
        try
        {
            return NativeMethods.ModelSupports(_handle, feature);
        }
        finally
        {
            EndUse();
        }
    }

    /// <summary>
    /// Tokenize text using the model's tokenizer.
    /// </summary>
    /// <param name="text">The text to tokenize.</param>
    /// <param name="maxTokens">Maximum number of tokens to output.</param>
    /// <returns>Array of token IDs.</returns>
    public int[] Tokenize(string text, int maxTokens = 1024)
    {
        BeginUse();
        try
        {
            var tokensPtr = Marshal.AllocHGlobal(maxTokens * sizeof(int));
            try
            {
                var count = NativeMethods.Tokenize(_handle, text, tokensPtr, (nuint)maxTokens);
                if (count < 0)
                    throw new InvalidOperationException("Tokenization failed");

                var tokens = new int[count];
                Marshal.Copy(tokensPtr, tokens, 0, count);
                return tokens;
            }
            finally
            {
                Marshal.FreeHGlobal(tokensPtr);
            }
        }
        finally
        {
            EndUse();
        }
    }

    internal ModelHandle Handle => _handle;

    ~Model()
    {
        // Mark as disposed to prevent new operations
        Volatile.Write(ref _disposed, 1);

        // Wait for all in-flight operations to complete before freeing
        var sw = new SpinWait();
        while (Volatile.Read(ref _useCount) > 0)
            sw.SpinOnce();

        NativeMethods.ModelFree(_handle);
        _handle = ModelHandle.Null;
    }

    public void Dispose()
    {
        Volatile.Write(ref _disposed, 1);

        var sw = new SpinWait();
        while (Volatile.Read(ref _useCount) > 0)
            sw.SpinOnce();

        NativeMethods.ModelFree(_handle);
        _handle = ModelHandle.Null;
        GC.SuppressFinalize(this);
    }
}
