using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using TranscribeCppSharp.Ui.Services;
using TranscribeCppSharp.Ui.ViewModels;
using TranscribeCppSharp.Ui.Views;

namespace TranscribeCppSharp.Ui;

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

        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<TranscriptionViewModel>();
        services.AddSingleton<StreamingViewModel>();
        services.AddSingleton<BatchViewModel>();
        services.AddSingleton<ModelManagerViewModel>();
        services.AddSingleton<SettingsViewModel>();

        return services.BuildServiceProvider();
    }
}
