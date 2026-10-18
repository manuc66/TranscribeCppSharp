using TranscribeCppSharp.UI.Services;

namespace TranscribeCppSharp.UI.Tests;

/// <summary>
/// Points <see cref="UiSettings"/> at a temporary file for the duration of a test.
/// </summary>
/// <remarks>
/// A run records its model in the settings file. Without this the test would write
/// to the developer's real one, and a file left over from a real run could steer
/// the selection the test asserts on. The path is unique per scope, so two scopes
/// can never share a file even if they overlap.
/// </remarks>
internal sealed class UiSettingsScope : IDisposable
{
    private readonly string? _previous = UiSettings.FilePathOverride;

    public UiSettingsScope()
        => UiSettings.FilePathOverride = Path.Combine(
            Path.GetTempPath(),
            "tcsharp-ui-settings-" + Guid.NewGuid().ToString("N"),
            "ui-settings.json");

    public void Dispose() => UiSettings.FilePathOverride = _previous;
}
