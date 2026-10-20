namespace TranscribeCppSharp.UI.Models;

/// <summary>
/// One choice of the diarizer picker: a model that serves only the DIARIZE role,
/// or <see cref="Alias"/> null for "none".
/// </summary>
/// <param name="Alias">The model alias, or null for no separate diarizer.</param>
/// <param name="Label">Text shown in the picker.</param>
/// <param name="Languages">
/// The languages the model's catalog record declares, or empty when it declares
/// none. Used to warn when the chosen audio language is not among them; it is the
/// pinned catalog's statement, not a measurement made here.
/// </param>
public sealed record DiarizerOption(
    string? Alias,
    string Label,
    IReadOnlyList<string>? Languages = null);
