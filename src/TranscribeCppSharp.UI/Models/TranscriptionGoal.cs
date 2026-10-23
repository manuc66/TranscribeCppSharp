namespace TranscribeCppSharp.UI.Models;

/// <summary>
/// What the reader wants out of the run, in the words of the result rather than
/// the words of the native roles.
/// </summary>
/// <remarks>
/// The app can run four roles, and two of them (transcription, diarization) can be
/// combined into a two-pass result. This enum is the outcome the tab offers; the
/// model picker is filtered to the role the outcome needs, and the speaker step
/// follows <see cref="TranscribeWithSpeakers"/>.
/// </remarks>
public enum TranscriptionGoal
{
    /// <summary>Text only, from an ASR model.</summary>
    Transcribe = 0,

    /// <summary>Text plus speaker labels, native or through a dedicated diarizer.</summary>
    TranscribeWithSpeakers = 1,

    /// <summary>Speaker turns only, from a model whose role is diarization.</summary>
    SpeakersOnly = 2,

    /// <summary>Ranked language candidates, from a model whose role is LANGID.</summary>
    IdentifyLanguage = 3,

    /// <summary>Speech segments, from a model whose role is VAD.</summary>
    DetectSpeech = 4,
}

/// <summary>
/// One choice of the goal picker: the outcome, its words, and the catalog role the
/// model list is filtered to.
/// </summary>
/// <param name="Value">The goal this entry selects.</param>
/// <param name="Label">Text shown in the picker.</param>
/// <param name="Description">One line saying what the run produces.</param>
/// <param name="RoleKey">The catalog role key a compatible model must carry.</param>
/// <param name="RoleLabel">The role in the words used by the empty-state hints.</param>
public sealed record TranscriptionGoalOption(
    TranscriptionGoal Value,
    string Label,
    string Description,
    string RoleKey,
    string RoleLabel);
