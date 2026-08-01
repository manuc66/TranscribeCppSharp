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
    private readonly ExtBuffer<WhisperRunExt> buffer;
    private IntPtr initialPromptPtr;
    private IntPtr promptTokensPtr;

    /// <inheritdoc/>
    public WhisperExtBuilder()
    {
        buffer = new ExtBuffer<WhisperRunExt>(
            NativeMethods.WhisperRunExtInit,
            static p => p.ext.size,
            nameof(WhisperExtBuilder));
    }

    /// <summary>Initial prompt to guide transcription (e.g. "Bonjour, comment allez-vous?").</summary>
    public WhisperExtBuilder WithInitialPrompt(string prompt)
    {
        if (initialPromptPtr != IntPtr.Zero)
        {
            Marshal.FreeCoTaskMem(initialPromptPtr);
        }

        initialPromptPtr = Marshal.StringToCoTaskMemUTF8(prompt);
        buffer.Params.initialPrompt = initialPromptPtr;
        return this;
    }

    /// <summary>Pre-tokenized prompt tokens.</summary>
    public WhisperExtBuilder WithPromptTokens(int[] tokens)
    {
        if (promptTokensPtr != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(promptTokensPtr);
        }

        promptTokensPtr = Marshal.AllocHGlobal(tokens.Length * sizeof(int));
        Marshal.Copy(tokens, 0, promptTokensPtr, tokens.Length);
        buffer.Params.promptTokens = promptTokensPtr;
        buffer.Params.nPromptTokens = (nuint)tokens.Length;
        return this;
    }

    /// <summary>How to condition on the prompt.</summary>
    public WhisperExtBuilder WithPromptCondition(WhisperPromptCondition condition)
    {
        buffer.Params.promptCondition = condition;
        return this;
    }

    /// <summary>Whether to condition on previous tokens for context.</summary>
    public WhisperExtBuilder WithConditionOnPrevTokens(bool condition)
    {
        buffer.Params.conditionOnPrevTokens = condition;
        return this;
    }

    /// <summary>Maximum number of previous context tokens to use.</summary>
    public WhisperExtBuilder WithMaxPrevContextTokens(int maxTokens)
    {
        buffer.Params.maxPrevContextTokens = maxTokens;
        return this;
    }

    /// <summary>Sampling temperature (0.0 = greedy, higher = more random).</summary>
    public WhisperExtBuilder WithTemperature(float temperature)
    {
        buffer.Params.temperature = temperature;
        return this;
    }

    /// <summary>Temperature increment for fallback decoding.</summary>
    public WhisperExtBuilder WithTemperatureInc(float temperatureInc)
    {
        buffer.Params.temperatureInc = temperatureInc;
        return this;
    }

    /// <summary>Compression ratio threshold for fallback detection.</summary>
    public WhisperExtBuilder WithCompressionRatioThold(float thold)
    {
        buffer.Params.compressionRatioThold = thold;
        return this;
    }

    /// <summary>Log probability threshold for fallback detection.</summary>
    public WhisperExtBuilder WithLogprobThold(float thold)
    {
        buffer.Params.logprobThold = thold;
        return this;
    }

    /// <summary>No-speech probability threshold.</summary>
    public WhisperExtBuilder WithNoSpeechThold(float thold)
    {
        buffer.Params.noSpeechThold = thold;
        return this;
    }

    /// <summary>Random seed for reproducibility.</summary>
    public WhisperExtBuilder WithSeed(uint seed)
    {
        buffer.Params.seed = seed;
        return this;
    }

    /// <summary>Maximum initial timestamp in seconds.</summary>
    public WhisperExtBuilder WithMaxInitialTimestamp(float seconds)
    {
        buffer.Params.maxInitialTimestamp = seconds;
        return this;
    }

    internal IntPtr Build() => buffer.Build();

    /// <inheritdoc/>
    public void Dispose()
    {
        if (initialPromptPtr != IntPtr.Zero)
        {
            Marshal.FreeCoTaskMem(initialPromptPtr);
            initialPromptPtr = IntPtr.Zero;
        }

        if (promptTokensPtr != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(promptTokensPtr);
            promptTokensPtr = IntPtr.Zero;
        }

        buffer.Dispose();
    }
}
