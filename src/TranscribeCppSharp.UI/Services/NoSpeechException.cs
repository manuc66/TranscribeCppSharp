namespace TranscribeCppSharp.UI.Services;

/// <summary>
/// A VAD pre-pass found no usable speech, so there was nothing to transcribe.
/// </summary>
/// <remarks>
/// This is an outcome of a valid input, not a failure of the run: the file may be
/// silence, noise, or too short after the planner's minimum. The tab reports it
/// as a status and does not keep it as a copyable error.
/// </remarks>
public sealed class NoSpeechException(string vadModel)
    : Exception($"The VAD model {vadModel} found no usable speech in this audio; nothing was transcribed.")
{
    /// <summary>The VAD model that found no speech.</summary>
    public string VadModel { get; } = vadModel;
}
