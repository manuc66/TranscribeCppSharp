using TranscribeCppSharp.Interop;

namespace TranscribeCppSharp.UI.Models;

/// <summary>
/// One choice of the backend picker: the request to make and the words for it.
/// </summary>
/// <remarks>
/// The enum members carry the "Backend" prefix, which is noise in a ComboBox
/// ("BackendAuto" rather than "Auto"), so the picker shows this label and keeps
/// the enum value for the run. Same shape as <see cref="DeviceOption"/>.
/// </remarks>
/// <param name="Value">The backend to request.</param>
/// <param name="Label">Text shown in the picker. Never empty.</param>
public sealed record BackendOption(BackendRequest Value, string Label);
