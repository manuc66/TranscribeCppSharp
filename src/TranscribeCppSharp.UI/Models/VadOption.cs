namespace TranscribeCppSharp.UI.Models;

/// <summary>
/// One choice of the VAD-model picker: a downloaded model that serves the VAD role.
/// </summary>
/// <param name="Alias">The model alias.</param>
/// <param name="Label">Text shown in the picker.</param>
public sealed record VadOption(string Alias, string Label);
