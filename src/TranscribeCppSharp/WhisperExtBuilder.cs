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
    private readonly ExtBuffer<WhisperRunExt> _buffer;
    private IntPtr _initialPromptPtr;
    private IntPtr _promptTokensPtr;

    public WhisperExtBuilder()
    {
        _buffer = new ExtBuffer<WhisperRunExt>(
            NativeMethods.WhisperRunExtInit,
            static p => p.ext.size,
            nameof(WhisperExtBuilder));
    }

    /// <summary>Initial prompt to guide transcription (e.g. "Bonjour, comment allez-vous?").</summary>
    public WhisperExtBuilder WithInitialPrompt(string prompt)
    {
        if (_initialPromptPtr != IntPtr.Zero)
            Marshal.FreeCoTaskMem(_initialPromptPtr);
        _initialPromptPtr = Marshal.StringToCoTaskMemUTF8(prompt);
        _buffer.Params.initialPrompt = _initialPromptPtr;
        return this;
    }

    /// <summary>Pre-tokenized prompt tokens.</summary>
    public WhisperExtBuilder WithPromptTokens(int[] tokens)
    {
        if (_promptTokensPtr != IntPtr.Zero)
            Marshal.FreeHGlobal(_promptTokensPtr);
        _promptTokensPtr = Marshal.AllocHGlobal(tokens.Length * sizeof(int));
        Marshal.Copy(tokens, 0, _promptTokensPtr, tokens.Length);
        _buffer.Params.promptTokens = _promptTokensPtr;
        _buffer.Params.nPromptTokens = (nuint)tokens.Length;
        return this;
    }

    /// <summary>How to condition on the prompt.</summary>
    public WhisperExtBuilder WithPromptCondition(WhisperPromptCondition condition)
    {
        _buffer.Params.promptCondition = condition;
        return this;
    }

    /// <summary>Whether to condition on previous tokens for context.</summary>
    public WhisperExtBuilder WithConditionOnPrevTokens(bool condition)
    {
        _buffer.Params.conditionOnPrevTokens = condition;
        return this;
    }

    /// <summary>Maximum number of previous context tokens to use.</summary>
    public WhisperExtBuilder WithMaxPrevContextTokens(int maxTokens)
    {
        _buffer.Params.maxPrevContextTokens = maxTokens;
        return this;
    }

    /// <summary>Sampling temperature (0.0 = greedy, higher = more random).</summary>
    public WhisperExtBuilder WithTemperature(float temperature)
    {
        _buffer.Params.temperature = temperature;
        return this;
    }

    /// <summary>Temperature increment for fallback decoding.</summary>
    public WhisperExtBuilder WithTemperatureInc(float temperatureInc)
    {
        _buffer.Params.temperatureInc = temperatureInc;
        return this;
    }

    /// <summary>Compression ratio threshold for fallback detection.</summary>
    public WhisperExtBuilder WithCompressionRatioThold(float thold)
    {
        _buffer.Params.compressionRatioThold = thold;
        return this;
    }

    /// <summary>Log probability threshold for fallback detection.</summary>
    public WhisperExtBuilder WithLogprobThold(float thold)
    {
        _buffer.Params.logprobThold = thold;
        return this;
    }

    /// <summary>No-speech probability threshold.</summary>
    public WhisperExtBuilder WithNoSpeechThold(float thold)
    {
        _buffer.Params.noSpeechThold = thold;
        return this;
    }

    /// <summary>Random seed for reproducibility.</summary>
    public WhisperExtBuilder WithSeed(uint seed)
    {
        _buffer.Params.seed = seed;
        return this;
    }

    /// <summary>Maximum initial timestamp in seconds.</summary>
    public WhisperExtBuilder WithMaxInitialTimestamp(float seconds)
    {
        _buffer.Params.maxInitialTimestamp = seconds;
        return this;
    }

    internal IntPtr Build() => _buffer.Build();

    public void Dispose()
    {
        if (_initialPromptPtr != IntPtr.Zero)
        {
            Marshal.FreeCoTaskMem(_initialPromptPtr);
            _initialPromptPtr = IntPtr.Zero;
        }
        if (_promptTokensPtr != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_promptTokensPtr);
            _promptTokensPtr = IntPtr.Zero;
        }
        _buffer.Dispose();
    }
}
