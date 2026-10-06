using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using TranscribeCppSharp.Ui;
using TranscribeCppSharp.Ui.ViewModels;

namespace TranscribeCppSharp.UiScreenshot;

/// <summary>
/// Renders the real window to one PNG per tab, so the images in the documentation
/// are something that can be regenerated rather than something to trust.
/// </summary>
/// <remarks>
/// It runs the real <see cref="App"/> and its real <c>MainWindow</c> with the real
/// view models behind them. Nothing is stubbed or re-wired to suit the picture: the
/// only difference from running the app is that there is no display and no
/// dispatcher loop, which is why the lifetime is constructed by hand and the
/// dispatcher is pumped explicitly between steps.
/// <para>
/// Two things about the output are worth knowing before using it. The machine decides
/// what is in the picture: the Settings tab lists this machine's compute devices, and
/// the Models tab counts the models this machine has downloaded. And what a tab shows
/// on a fresh machine is what it shows here — no audio picked, nothing transcribed,
/// nothing downloaded. This generator does not fabricate a result to fill the frame.
/// </para>
/// </remarks>
internal static class Program
{
    /// <summary>
    /// Tab order as MainWindow.axaml declares it; the name is the file suffix.
    /// </summary>
    private static readonly string[] Tabs =
        ["transcription", "streaming", "batch", "models", "settings"];

    [STAThread]
    public static void Main(string[] args)
    {
        string outputDirectory = args.Length > 0
            ? args[0]
            : Path.Combine("docs", "assets", "images");
        Directory.CreateDirectory(outputDirectory);

        // A lifetime handed to SetupWithLifetime instead of
        // StartWithClassicDesktopLifetime, which would block forever here: the
        // headless platform has no timer source to drive a message loop. Constructing
        // it directly still goes through App.OnFrameworkInitializationCompleted, so
        // the window and its dependency injection are the app's own.
        var lifetime = new ClassicDesktopStyleApplicationLifetime();

        AppBuilder.Configure<App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            // UseSkia, not UsePlatformDetect: the headless platform supplies the
            // windows, but with UseHeadlessDrawing = false it draws nothing unless a
            // rendering system is named, and the frame this tool exists to capture
            // comes back empty.
            .UseSkia()
            .WithInterFont()
            .SetupWithLifetime(lifetime);

        var window = lifetime.MainWindow
            ?? throw new InvalidOperationException("App did not create a main window.");

        if (window.DataContext is not MainWindowViewModel models)
        {
            throw new InvalidOperationException(
                $"MainWindow.DataContext is {window.DataContext?.GetType().Name ?? "null"}, " +
                $"not a {nameof(MainWindowViewModel)}.");
        }

        window.Show();

        foreach (string tab in Tabs)
        {
            models.SelectedTabIndex = Array.IndexOf(Tabs, tab);
            Settle(window);

            string path = Path.Combine(outputDirectory, $"ui-{tab}.png");
            WriteableBitmap? frame = window.CaptureRenderedFrame();
            if (frame is null)
            {
                throw new InvalidOperationException($"No frame captured for the {tab} tab.");
            }

            frame.Save(path, new PngBitmapEncoderOptions());
            Console.WriteLine($"wrote {path} ({frame.PixelSize.Width}x{frame.PixelSize.Height})");
        }

        lifetime.Shutdown();
    }

    /// <summary>
    /// Lets the tab change be laid out before the frame is taken.
    /// </summary>
    /// <remarks>
    /// Setting SelectedTabIndex and capturing in one pass photographs the previous
    /// tab: the binding has not been through a layout yet. Draining the dispatcher
    /// and then measuring is what makes the new tab the thing in the picture.
    /// </remarks>
    private static void Settle(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
}