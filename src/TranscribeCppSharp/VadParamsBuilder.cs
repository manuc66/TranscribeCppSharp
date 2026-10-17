namespace TranscribeCppSharp;

using System;
using System.Runtime.InteropServices;
using TranscribeCppSharp.Interop;

/// <summary>
/// Fluent builder for VAD run parameters (<c>transcribe_vad_params</c>).
/// Each setter overrides the native default, which is stated on the method.
/// </summary>
public sealed class VadParamsBuilder : IDisposable
{
    private readonly IntPtr handle;
    private VadParams @params;
    private bool disposed;

    /// <inheritdoc/>
    public VadParamsBuilder()
    {
        AbiValidation.ValidateSize<VadParams>(AbiStruct.AbiVadParams, nameof(VadParams));
        handle = Marshal.AllocHGlobal(Marshal.SizeOf<VadParams>());
        NativeMethods.VadParamsInit(handle);
        @params = Marshal.PtrToStructure<VadParams>(handle);
    }

    /// <summary>Probability at or above which audio is classified as speech. Default 0.5; range 0..1.</summary>
    public VadParamsBuilder WithThreshold(double threshold)
    {
        @params.threshold = threshold;
        return this;
    }

    /// <summary>
    /// Probability below which ongoing speech is treated as silence. Default -1,
    /// which uses <c>max(threshold - 0.15, 0.01)</c>; 0 means "use threshold".
    /// </summary>
    public VadParamsBuilder WithNegThreshold(double negThreshold)
    {
        @params.negThreshold = negThreshold;
        return this;
    }

    /// <summary>Discard speech segments this short or shorter (ms). Default 250.</summary>
    public VadParamsBuilder WithMinSpeechMs(int minSpeechMs)
    {
        @params.minSpeechMs = minSpeechMs;
        return this;
    }

    /// <summary>How long to wait after silence starts before ending speech (ms). Default 100.</summary>
    public VadParamsBuilder WithMinSilenceMs(int minSilenceMs)
    {
        @params.minSilenceMs = minSilenceMs;
        return this;
    }

    /// <summary>Extra audio kept before and after speech (ms). Default 30.</summary>
    public VadParamsBuilder WithSpeechPadMs(int speechPadMs)
    {
        @params.speechPadMs = speechPadMs;
        return this;
    }

    /// <summary>Maximum speech segment length (ms); longer segments are split. Default 0 (no limit).</summary>
    public VadParamsBuilder WithMaxSpeechMs(int maxSpeechMs)
    {
        @params.maxSpeechMs = maxSpeechMs;
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
