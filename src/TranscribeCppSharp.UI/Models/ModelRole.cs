using TranscribeCppSharp.SharedSource;

namespace TranscribeCppSharp.UI.Models;

/// <summary>
/// The role a model serves, as transcribe.cpp's catalog spells it.
/// </summary>
/// <remarks>
/// Their catalog records <c>role</c> for the non-ASR models and leaves it out for
/// the 73 speech-to-text entries. Absent therefore means ASR, not unknown; a model
/// with no record at all is treated the same way here, which matches the wrapper's
/// fallback label rather than inventing a role.
/// <para>
/// The loaded model is the authority — <c>Model.Roles</c> is what the run follows.
/// This helper is for the catalogue, the goal filter and the Models tab, where the
/// answer is needed before a download.
/// </para>
/// </remarks>
public static class ModelRole
{
    public const string Asr = "asr";
    public const string Diarize = "diarize";
    public const string Langid = "langid";
    public const string Vad = "vad";

    /// <summary>The catalog role key for an alias.</summary>
    public static string KeyOf(string alias)
    {
        string? role = UpstreamCatalog.Read(alias)?.Role;
        return string.IsNullOrEmpty(role) ? Asr : role;
    }

    /// <summary>The role in the words the UI shows.</summary>
    public static string LabelOf(string key) => key switch
    {
        Asr => "speech to text",
        Diarize => "diarization",
        Langid => "language identification",
        Vad => "speech detection",
        _ => key,
    };
}
