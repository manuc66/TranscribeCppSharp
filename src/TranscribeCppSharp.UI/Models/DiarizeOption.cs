using TranscribeCppSharp.Interop;

namespace TranscribeCppSharp.UI.Models;

/// <summary>
/// One choice of the diarization picker: a mode and the words for it.
/// </summary>
/// <remarks>
/// The same shape as <see cref="ModelSizeFilterOption"/>: the picker's items are
/// records and the view model holds the enum, so the two are tied by a
/// <c>SelectedValue</c> binding rather than by matching instances.
/// </remarks>
/// <param name="Value">The mode this entry selects.</param>
/// <param name="Label">Text shown in the picker.</param>
public sealed record DiarizeOption(DiarizeMode Value, string Label);
