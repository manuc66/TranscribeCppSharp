#nullable enable

using System;

namespace TranscribeCppSharp;

/// <summary>
/// Convenience API: load model + transcribe in one call.
/// For repeated transcription, use Model.CreateSession() instead.
/// </summary>
public static class Transcription
{
    /// <summary>
    /// Load a model, transcribe a PCM buffer, and return the result.
    /// The model is freed after transcription completes.
    /// </summary>
    public static Transcript Run(
        string modelPath,
        ReadOnlySpan<float> pcm,
        Action<ModelLoadParamsBuilder>? modelConfig = null,
        Action<SessionParamsBuilder>? sessionConfig = null,
        Action<RunParamsBuilder>? runConfig = null)
    {
        using var model = Model.Load(modelPath, modelConfig);
        using var session = model.CreateSession(sessionConfig);
        return session.Run(pcm, runConfig);
    }
}
