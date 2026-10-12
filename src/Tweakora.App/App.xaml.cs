using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Tweakora.App.Services;
using Tweakora.App.ViewModels;
using Tweakora.App.Views;

namespace Tweakora.App;

public partial class App : Application
{
    private readonly IHost _host;

    public App()
    {
        // 1. Initialize logging first so all crashes are recorded
        LoggingConfiguration.InitializeLogging();

        // 2. Global Crash Handlers
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        // 3. Setup Dependency Injection host
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

        try
        {
            Log.Information("Starting Tweakora application host...");
            await _host.StartAsync();

            MainWindow window = _host.Services.GetRequiredService<MainWindow>();
            MainWindow = window;
            window.Show();

            Log.Information("Tweakora main window shown successfully.");
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Critical failure during application startup");
            MessageBox.Show(
                $"Tweakora failed to start:\n\n{ex.Message}\n\nStack Trace:\n{ex.StackTrace}\n\nPlease check the logs in %LOCALAPPDATA%\\Tweakora\\Logs for details.",
                "Tweakora Startup Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );

            Shutdown(1);
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        try
        {
            Log.Information("Tweakora is shutting down.");
            await _host.StopAsync();
            _host.Dispose();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error while stopping application host during shutdown");
        }
        finally
        {
            Log.CloseAndFlush();
            base.OnExit(e);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Fatal(e.Exception, "Unhandled Dispatcher Exception captured");
        MessageBox.Show(
            $"An unexpected error occurred in the UI:\n\n{e.Exception.Message}\n\nCheck logs for more details.",
            "Tweakora Error",
            MessageBoxButton.OK,
            MessageBoxImage.Error
        );
        e.Handled = true;
    }

    private void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            Log.Fatal(ex, "Unhandled AppDomain Exception captured (IsTerminating: {IsTerminating})", e.IsTerminating);
            MessageBox.Show(
                $"A critical system error occurred:\n\n{ex.Message}",
                "Tweakora Fatal Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unobserved Task Exception captured");
        e.SetObserved();
    }
}
