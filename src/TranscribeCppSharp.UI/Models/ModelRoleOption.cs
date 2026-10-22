namespace TranscribeCppSharp.UI.Models;

/// <summary>
/// One choice of the role filter: upstream's role key and the words for it.
/// </summary>
/// <remarks>
/// The key is theirs ("asr", "diarize", "langid", "vad"), so the filter and
/// <see cref="ModelCatalogItem.RoleKey"/> are compared without a translation
/// table that could drift. The label is only what the picker shows.
/// </remarks>
/// <param name="Key">Upstream's role key.</param>
/// <param name="Label">Text shown in the picker.</param>
public sealed record ModelRoleOption(string Key, string Label);
