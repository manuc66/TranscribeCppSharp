using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using TranscribeCppSharp.UI.Services;
using TranscribeCppSharp.UI.ViewModels;
using TranscribeCppSharp.UI.Views;

namespace TranscribeCppSharp.UI;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var services = ConfigureServices();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = services.GetRequiredService<MainWindowViewModel>()
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static ServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        services.AddSingleton<ITranscriptionService, TranscriptionService>();
        services.AddSingleton<ISegmentPlayer, FfplaySegmentPlayer>();

        // One cache for the whole window: the transcription tab and the streaming
        // tab both read the default model's capabilities at startup, and this is
        // what makes them share the one load instead of loading it twice.
        services.AddSingleton<ModelCapabilityCache>();

        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<TranscriptionViewModel>();
        services.AddSingleton<StreamingViewModel>();
        services.AddSingleton<BatchViewModel>();
        services.AddSingleton<ModelManagerViewModel>();
        // The app remembers the backend and pinned device between launches; the
        // default constructor does not, so a test constructing the tab neither
        // reads nor writes the developer's settings file.
        services.AddSingleton(_ => new SettingsViewModel(rememberSelection: true));
        services.AddSingleton<HistoryViewModel>();

        return services.BuildServiceProvider();
    }
}
