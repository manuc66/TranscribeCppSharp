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

        // Wait for the startup capability probes before the first frame. Without
        // this the picture is taken mid-load: on a machine where the default model
        // is already downloaded, the language picker is still empty because the
        // list only arrives once the model has answered. Settled is the honest
        // state to show, and a transient blank field would not be.
        if (!SettleProbes(window, models))
        {
            Console.Error.WriteLine(
                "warning: capability probes still running after " + ProbeTimeout +
                "; capturing the window as it is.");
        }

        foreach (string tab in Tabs)
        {
            models.SelectedTabIndex = Array.IndexOf(Tabs, tab);
            Layout(window);

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

    /// <summary>How long to wait for the model probes before giving up.</summary>
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Pumps the dispatcher until both tabs have finished reading what their model
    /// can do.
    /// </summary>
    /// <returns>True when they finished, false when <see cref="ProbeTimeout"/> ran out.</returns>
    /// <remarks>
    /// The probes load the model on a worker thread and post back to the UI thread,
    /// so this has to sleep between pumps: an empty loop would starve the load. The
    /// dispatcher is never left to run on its own, because nothing here drives a
    /// message loop.
    /// </remarks>
    private static bool SettleProbes(Window window, MainWindowViewModel models)
    {
        var deadline = DateTime.UtcNow + ProbeTimeout;
        do
        {
            Layout(window);
            if (!IsProbing(models))
            {
                return true;
            }

            Thread.Sleep(50);
        }
        while (DateTime.UtcNow < deadline);

        Layout(window);
        return !IsProbing(models);
    }

    private static bool IsProbing(MainWindowViewModel models)
        => models.Transcription.IsCheckingCapabilities
            || models.Streaming.IsCheckingCapabilities;

    /// <summary>
    /// Lets a change be laid out before the frame is taken.
    /// </summary>
    /// <remarks>
    /// Setting SelectedTabIndex and capturing in one pass photographs the previous
    /// tab: the binding has not been through a layout yet. Draining the dispatcher
    /// and then measuring is what makes the new tab the thing in the picture.
    /// </remarks>
    private static void Layout(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
}