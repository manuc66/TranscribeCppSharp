using Avalonia;
using Avalonia.Headless;
using TranscribeCppSharp.Ui;

[assembly: AvaloniaTestApplication(typeof(TranscribeCppSharp.Ui.Tests.TestAppBuilder))]

namespace TranscribeCppSharp.Ui.Tests;

/// <summary>
/// Builds the real <see cref="App"/> on Avalonia's headless backend, so the
/// views load with the same styles (FluentTheme, the DataGrid theme) they get
/// on screen, without needing a display.
/// </summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
