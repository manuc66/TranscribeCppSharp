namespace TranscribeCppSharp.UI.Models;

/// <summary>
/// One choice of the diarizer picker: a model that serves only the DIARIZE role,
/// or <see cref="Alias"/> null for "none".
/// </summary>
/// <param name="Alias">The model alias, or null for no separate diarizer.</param>
/// <param name="Label">Text shown in the picker.</param>
public sealed record DiarizerOption(string? Alias, string Label);
