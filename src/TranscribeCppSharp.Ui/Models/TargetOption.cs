namespace TranscribeCppSharp.Ui.Models;

/// <summary>
/// One choice in the target-language picker.
/// </summary>
/// <param name="Display">What the reader sees. Never empty.</param>
/// <param name="Code">
/// The code to hand to the native library, or null to hand over nothing at all —
/// which is what makes the model use its own default. An empty string would be
/// passed through as a language rather than as "unset", so null is deliberate.
/// </param>
/// <remarks>
/// A record rather than a bare string so the picker can show a sentence where
/// there is no code. Binding a ComboBox's SelectedItem straight to a nullable
/// string is what leaves the box rendering nothing while a valid value sits in
/// the view model: there is no label for "unset" to select.
/// </remarks>
public sealed record TargetOption(string Display, string? Code);