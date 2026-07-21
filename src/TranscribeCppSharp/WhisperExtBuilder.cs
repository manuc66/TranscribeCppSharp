#nullable enable

using System;
using System.Runtime.InteropServices;
using TranscribeCppSharp.Interop;

namespace TranscribeCppSharp;

/// <summary>
/// Fluent builder for Whisper-specific extension parameters.
/// </summary>
public sealed class WhisperExtBuilder : IDisposable
{
    private IntPtr _handle;
    private WhisperRunExt _params;
    private IntPtr _initialPromptPtr;
    private IntPtr _promptTokensPtr;
    private bool _disposed;

    public WhisperExtBuilder()
    {
        var size = Marshal.SizeOf<WhisperRunExt>();
        _handle = Marshal.AllocHGlobal(size);
        NativeMethods.WhisperRunExtInit(_handle);
        _params = Marshal.PtrToStructure<WhisperRunExt>(_handle);
    }

    /// <summary>Initial prompt to guide transcription (e.g. "Bonjour, comment allez-vous?").</summary>
    public WhisperExtBuilder WithInitialPrompt(string prompt)
    {
        if (_initialPromptPtr != IntPtr.Zero)
            Marshal.FreeCoTaskMem(_initialPromptPtr);
        _initialPromptPtr = Marshal.StringToCoTaskMemUTF8(prompt);
        _params.initialPrompt = _initialPromptPtr;
        return this;
    }

    /// <summary>Pre-tokenized prompt tokens.</summary>
    public WhisperExtBuilder WithPromptTokens(int[] tokens)
    {
        if (_promptTokensPtr != IntPtr.Zero)
            Marshal.FreeHGlobal(_promptTokensPtr);
        _promptTokensPtr = Marshal.AllocHGlobal(tokens.Length * sizeof(int));
        Marshal.Copy(tokens, 0, _promptTokensPtr, tokens.Length);
        _params.promptTokens = _promptTokensPtr;
        _params.nPromptTokens = (nuint)tokens.Length;
        return this;
    }

    /// <summary>How to condition on the prompt.</summary>
    public WhisperExtBuilder WithPromptCondition(WhisperPromptCondition condition)
    {
        _params.promptCondition = condition;
        return this;
    }

    /// <summary>Whether to condition on previous tokens for context.</summary>
    public WhisperExtBuilder WithConditionOnPrevTokens(bool condition)
    {
        _params.conditionOnPrevTokens = condition;
        return this;
    }

    /// <summary>Maximum number of previous context tokens to use.</summary>
    public WhisperExtBuilder WithMaxPrevContextTokens(int maxTokens)
    {
        _params.maxPrevContextTokens = maxTokens;
        return this;
    }

    /// <summary>Sampling temperature (0.0 = greedy, higher = more random).</summary>
    public WhisperExtBuilder WithTemperature(float temperature)
    {
        _params.temperature = temperature;
        return this;
    }

    /// <summary>Temperature increment for fallback decoding.</summary>
    public WhisperExtBuilder WithTemperatureInc(float temperatureInc)
    {
        _params.temperatureInc = temperatureInc;
        return this;
    }

    /// <summary>Compression ratio threshold for fallback detection.</summary>
    public WhisperExtBuilder WithCompressionRatioThold(float thold)
    {
        _params.compressionRatioThold = thold;
        return this;
    }

    /// <summary>Log probability threshold for fallback detection.</summary>
    public WhisperExtBuilder WithLogprobThold(float thold)
    {
        _params.logprobThold = thold;
        return this;
    }

    /// <summary>No-speech probability threshold.</summary>
    public WhisperExtBuilder WithNoSpeechThold(float thold)
    {
        _params.noSpeechThold = thold;
        return this;
    }

    /// <summary>Random seed for reproducibility.</summary>
    public WhisperExtBuilder WithSeed(uint seed)
    {
        _params.seed = seed;
        return this;
    }

    /// <summary>Maximum initial timestamp in seconds.</summary>
    public WhisperExtBuilder WithMaxInitialTimestamp(float seconds)
    {
        _params.maxInitialTimestamp = seconds;
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
            if (_initialPromptPtr != IntPtr.Zero)
                Marshal.FreeCoTaskMem(_initialPromptPtr);
            if (_promptTokensPtr != IntPtr.Zero)
                Marshal.FreeHGlobal(_promptTokensPtr);
            Marshal.FreeHGlobal(_handle);
            _disposed = true;
        }
    }
}
