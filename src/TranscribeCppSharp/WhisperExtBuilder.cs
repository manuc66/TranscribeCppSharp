namespace TranscribeCppSharp;

using System;
using System.Runtime.InteropServices;
using TranscribeCppSharp.Interop;

/// <summary>
/// Fluent builder for Whisper-specific extension parameters.
/// </summary>
public sealed class WhisperExtBuilder : ExtBuilder<WhisperRunExt>
{
    private IntPtr initialPromptPtr;
    private IntPtr promptTokensPtr;

    /// <summary>Creates a new builder.</summary>
    public WhisperExtBuilder()
        : base(
            NativeMethods.WhisperRunExtInit,
            static p => p.ext.size,
            nameof(WhisperExtBuilder))
    {
    }

    /// <summary>Initial prompt to guide transcription (e.g. "Bonjour, comment allez-vous?").</summary>
    public WhisperExtBuilder WithInitialPrompt(string prompt)
    {
        ThrowIfDisposed();
        if (initialPromptPtr != IntPtr.Zero)
        {
            Marshal.FreeCoTaskMem(initialPromptPtr);
        }

        initialPromptPtr = Marshal.StringToCoTaskMemUTF8(prompt);
        Params.initialPrompt = initialPromptPtr;
        return this;
    }

    /// <summary>Pre-tokenized prompt tokens.</summary>
    public WhisperExtBuilder WithPromptTokens(int[] tokens)
    {
        ThrowIfDisposed();
        if (promptTokensPtr != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(promptTokensPtr);
        }

        promptTokensPtr = Marshal.AllocHGlobal(tokens.Length * sizeof(int));
        Marshal.Copy(tokens, 0, promptTokensPtr, tokens.Length);
        Params.promptTokens = promptTokensPtr;
        Params.nPromptTokens = (nuint)tokens.Length;
        return this;
    }

    /// <summary>How to condition on the prompt.</summary>
    public WhisperExtBuilder WithPromptCondition(WhisperPromptCondition condition)
    {
        ThrowIfDisposed();
        Params.promptCondition = condition;
        return this;
    }

    /// <summary>Whether to condition on previous tokens for context.</summary>
    public WhisperExtBuilder WithConditionOnPrevTokens(bool condition)
    {
        ThrowIfDisposed();
        Params.conditionOnPrevTokens = condition;
        return this;
    }

    /// <summary>Maximum number of previous context tokens to use.</summary>
    public WhisperExtBuilder WithMaxPrevContextTokens(int maxTokens)
    {
        ThrowIfDisposed();
        Params.maxPrevContextTokens = maxTokens;
        return this;
    }

    /// <summary>Sampling temperature (0.0 = greedy, higher = more random).</summary>
    public WhisperExtBuilder WithTemperature(float temperature)
    {
        ThrowIfDisposed();
        Params.temperature = temperature;
        return this;
    }

    /// <summary>Temperature increment for fallback decoding.</summary>
    public WhisperExtBuilder WithTemperatureInc(float temperatureInc)
    {
        ThrowIfDisposed();
        Params.temperatureInc = temperatureInc;
        return this;
    }

    /// <summary>Compression ratio threshold for fallback detection.</summary>
    public WhisperExtBuilder WithCompressionRatioThold(float thold)
    {
        ThrowIfDisposed();
        Params.compressionRatioThold = thold;
        return this;
    }

    /// <summary>Log probability threshold for fallback detection.</summary>
    public WhisperExtBuilder WithLogprobThold(float thold)
    {
        ThrowIfDisposed();
        Params.logprobThold = thold;
        return this;
    }

    /// <summary>No-speech probability threshold.</summary>
    public WhisperExtBuilder WithNoSpeechThold(float thold)
    {
        ThrowIfDisposed();
        Params.noSpeechThold = thold;
        return this;
    }

    /// <summary>Random seed for reproducibility.</summary>
    public WhisperExtBuilder WithSeed(uint seed)
    {
        ThrowIfDisposed();
        Params.seed = seed;
        return this;
    }

    /// <summary>Maximum initial timestamp in seconds.</summary>
    public WhisperExtBuilder WithMaxInitialTimestamp(float seconds)
    {
        ThrowIfDisposed();
        Params.maxInitialTimestamp = seconds;
        return this;
    }

    internal new IntPtr Build() => base.Build();

    /// <inheritdoc/>
    protected override void DisposeNativeResources()
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
    }

    // Dispose is inherited from ExtBuilder<T>.
}
