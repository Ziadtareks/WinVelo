using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Tweakora.App.Services;
using Tweakora.App.ViewModels;
using Tweakora.App.Views;
using Tweakora.Core.Engine;
using Tweakora.Core.Interfaces;
using Tweakora.Core.Operations;
using Tweakora.Core.Safety;
using Tweakora.Modules.Apps;
using Tweakora.Modules.Cleaner;
using Tweakora.Modules.Config;
using Tweakora.Modules.Network;
using Tweakora.Modules.Services;
using Tweakora.Modules.Startup;
using Tweakora.Modules.Tweaks;
using Tweakora.Modules.Updates;
using Tweakora.Platform;
using Tweakora.Simulation;

namespace Tweakora.App;

public partial class App : Application
{
    private readonly IHost _host;
    private static int _isShowingDispatcherDialog = 0;
    private static int _isShowingAppDomainDialog = 0;
    private static int _unhandledErrorCount = 0;

    public App()
    {
        // 1. Initialize logging first so all crashes are recorded
        LoggingConfiguration.InitializeLogging();

        // 2. Global Crash Handlers with re-entrancy and storm guards
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        // 3. Detect Test Mode from command line arguments or environment variable
        string[] args = Environment.GetCommandLineArgs();
        bool isTestMode = args.Any(a => string.Equals(a, "--test-mode", StringComparison.OrdinalIgnoreCase)) ||
                          string.Equals(Environment.GetEnvironmentVariable("TWEAKORA_TEST_MODE"), "1", StringComparison.OrdinalIgnoreCase);

        if (isTestMode)
        {
            TestModeManager.EnableTestMode();
            Log.Warning("⚠️ Tweakora launched in TEST MODE. All system operations are simulated with in-memory fakes.");
        }
        else
        {
            Log.Information("Tweakora launched in NORMAL PRODUCTION MODE. Using real Windows platform drivers.");
        }

        // 4. Setup Dependency Injection host
        _host = Host.CreateDefaultBuilder()
            .ConfigureServices((context, services) =>
            {
                // Register platform services based on mode
                if (isTestMode)
                {
                    services.AddSingleton<IRegistryService, FakeRegistryService>();
                    services.AddSingleton<ISystemServicesService, FakeServicesService>();
                    services.AddSingleton<ITaskSchedulerService, FakeTaskSchedulerService>();
                    services.AddSingleton<ISystemRestoreService, FakeRestoreService>();
                    services.AddSingleton<IShellService, FakeShellService>();
                }
                else
                {
                    services.AddSingleton<IRegistryService, WindowsRegistryService>();
                    services.AddSingleton<ISystemServicesService, WindowsServicesService>();
                    services.AddSingleton<ITaskSchedulerService, WindowsTaskSchedulerService>();
                    services.AddSingleton<ISystemRestoreService, WindowsRestoreService>();
                    services.AddSingleton<IShellService, WindowsShellService>();
                }

                // Core Engine and Operation Infrastructure
                string journalDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Tweakora", "Journal");
                services.AddSingleton(new RollbackJournalManager(journalDir));
                services.AddSingleton<IOperationRunner, OperationRunner>();

                // Domain Feature Services
                services.AddSingleton<ICleanerService, CleanerService>();
                services.AddSingleton<ITweakService, TweakService>();
                services.AddSingleton<IAppInstallService, AppInstallService>();
                services.AddSingleton<IFeatureService, FeatureService>();
                services.AddSingleton<IFixService, FixService>();
                services.AddSingleton<IUpdateModeService, UpdateModeService>();
                services.AddSingleton<IDnsService, DnsService>();
                services.AddSingleton<IStartupService, StartupService>();
                services.AddSingleton<IServicesManagerService, ServicesManagerService>();

                // ViewModels
                services.AddSingleton<InstallViewModel>();
                services.AddSingleton<TweaksViewModel>();
                services.AddSingleton<ConfigViewModel>();
                services.AddSingleton<UpdatesViewModel>();
                services.AddSingleton<ToolboxViewModel>();
                services.AddSingleton<MainViewModel>();

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
            Log.Information("Starting Tweakora application host (TestMode={IsTestMode})...", TestModeManager.IsTestMode);
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
        int count = Interlocked.Increment(ref _unhandledErrorCount);
        Log.Fatal(e.Exception, "Unhandled Dispatcher Exception #{Count} captured", count);
        e.Handled = true;

        if (Interlocked.CompareExchange(ref _isShowingDispatcherDialog, 1, 0) == 0)
        {
            try
            {
                MessageBox.Show(
                    $"An unexpected error occurred in the UI:\n\n{e.Exception.Message}\n\nCheck logs for more details.",
                    "Tweakora Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Failed to display dispatcher error dialog");
            }
        }
        else
        {
            Log.Warning("Suppressed error popup for exception #{Count} to prevent infinite modal dialog loop.", count);
        }
    }

    private void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            Log.Fatal(ex, "Unhandled AppDomain Exception captured (IsTerminating: {IsTerminating})", e.IsTerminating);
            if (Interlocked.CompareExchange(ref _isShowingAppDomainDialog, 1, 0) == 0)
            {
                try
                {
                    MessageBox.Show(
                        $"A critical system error occurred:\n\n{ex.Message}",
                        "Tweakora Fatal Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error
                    );
                }
                catch { }
            }
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unobserved Task Exception captured");
        e.SetObserved();
    }
}
