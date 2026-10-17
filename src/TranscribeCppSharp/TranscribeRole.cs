namespace TranscribeCppSharp;

using System;
using TranscribeCppSharp.Interop;

/// <summary>
/// A kind of work a loaded model does. A model serves one or more roles, fixed
/// at load and never changed for the model's lifetime.
/// </summary>
/// <remarks>
/// Introduced by upstream transcribe.cpp 0.4. A model's roles come from
/// <see cref="Model.Roles"/> (<c>transcribe_model_roles</c>). Entry points that
/// need a role the model lacks return <see cref="Status.ErrUnsupportedRole"/>,
/// which surfaces as a <see cref="TranscribeException"/>.
///
/// In particular, models whose product is speaker turns (Sortformer) serve
/// <see cref="Diarize"/>, not <see cref="Asr"/>: transcribe.cpp 0.4 refuses
/// <see cref="Model.CreateSession"/> on them, and
/// <see cref="Model.GetCapabilities"/> throws instead of returning a zeroed
/// struct. Use <see cref="Model.CreateDiarizeSession"/> instead.
/// </remarks>
[Flags]
public enum TranscribeRole
{
    /// <summary>No role (an unloaded or unknown model).</summary>
    None = 0,

    /// <summary>Speech-to-text: <see cref="Model.CreateSession"/>.</summary>
    Asr = Role.RoleAsr,

    /// <summary>Speaker diarization: <see cref="Model.CreateDiarizeSession"/>.</summary>
    Diarize = Role.RoleDiarize,

    /// <summary>Language identification: <see cref="Model.CreateLangIdSession"/>.</summary>
    Langid = Role.RoleLangid,

    /// <summary>Voice activity detection: <see cref="Model.CreateVadSession"/>.</summary>
    Vad = Role.RoleVad,
}
