using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tweakora.App.Services;
using Tweakora.App.ViewModels;
using Tweakora.App.Views;

namespace Tweakora.App;

public partial class App : Application
{
    private readonly IHost _host;

    public App()
    {
        LoggingConfiguration.InitializeLogging();

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices((context, services) =>
            {
                // ViewModels
                services.AddSingleton<MainViewModel>();
                services.AddSingleton<DashboardViewModel>();
                services.AddSingleton<TweaksViewModel>();
                services.AddSingleton<ToolboxViewModel>();
                services.AddSingleton<AppsViewModel>();
                services.AddSingleton<PresetsViewModel>();
                services.AddSingleton<SettingsViewModel>();

                // Views
                services.AddSingleton<MainWindow>();
            })
            .Build();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        await _host.StartAsync();

        MainWindow window = _host.Services.GetRequiredService<MainWindow>();
        window.Show();

        Serilog.Log.Information("Tweakora started successfully.");
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        Serilog.Log.Information("Tweakora is shutting down.");
        await _host.StopAsync();
        _host.Dispose();
        base.OnExit(e);
    }
}
