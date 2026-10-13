using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
                services.AddSingleton<BackupsViewModel>();
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

            if (e.Args.Any(a => string.Equals(a, "--capture-screenshots", StringComparison.OrdinalIgnoreCase)))
            {
                int idx = Array.FindIndex(e.Args, a => string.Equals(a, "--capture-screenshots", StringComparison.OrdinalIgnoreCase));
                string targetDir = (idx >= 0 && idx + 1 < e.Args.Length && !e.Args[idx + 1].StartsWith("--")) ? e.Args[idx + 1] : "docs/screenshots";
                window.Loaded += (_, _) => CaptureAllScreenshots(window, targetDir);
                return;
            }
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

    private void CaptureAllScreenshots(MainWindow window, string targetDir)
    {
        try
        {
            Directory.CreateDirectory(targetDir);
            window.Width = 1440;
            window.Height = 900;
            FlushUi(window);

            var vm = (MainViewModel)window.DataContext;

            // 1. Tweaks Tab
            vm.SwitchTabCommand.Execute("Tweaks");
            FlushUi(window);
            SaveVisualToPng(window, 1440, 900, Path.Combine(targetDir, "tweaks.png"));

            // 2. Install Tab
            vm.SwitchTabCommand.Execute("Install");
            FlushUi(window);
            SaveVisualToPng(window, 1440, 900, Path.Combine(targetDir, "install.png"));

            // 3. Config Tab
            vm.SwitchTabCommand.Execute("Config");
            FlushUi(window);
            SaveVisualToPng(window, 1440, 900, Path.Combine(targetDir, "config.png"));

            // 4. Updates Tab
            vm.SwitchTabCommand.Execute("Updates");
            FlushUi(window);
            SaveVisualToPng(window, 1440, 900, Path.Combine(targetDir, "updates.png"));

            // 5. Toolbox Tab
            vm.SwitchTabCommand.Execute("Toolbox");
            FlushUi(window);
            SaveVisualToPng(window, 1440, 900, Path.Combine(targetDir, "toolbox.png"));

            // 6. Backups Tab
            vm.SwitchTabCommand.Execute("Backups");
            FlushUi(window);
            SaveVisualToPng(window, 1440, 900, Path.Combine(targetDir, "backups.png"));

            // 7. Diff Preview Overlay
            vm.SwitchTabCommand.Execute("Tweaks");
            vm.TweaksVM.ApplyPresetCommand.Execute("Minimal");
            FlushUi(window);
            vm.TweaksVM.OpenDiffPreviewCommand.Execute(null);
            FlushUi(window);
            SaveVisualToPng(window, 1440, 900, Path.Combine(targetDir, "diff-preview.png"));
            vm.TweaksVM.CloseDiffPreviewCommand.Execute(null);
            vm.TweaksVM.ClearSelectionCommand.Execute(null);
            FlushUi(window);

            // 8. Command Palette Overlay
            vm.ToggleCommandPaletteCommand.Execute(null);
            FlushUi(window);
            SaveVisualToPng(window, 1440, 900, Path.Combine(targetDir, "command-palette.png"));
            vm.ToggleCommandPaletteCommand.Execute(null);

            Log.Information("All UI screenshots successfully captured to {TargetDir}", targetDir);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to capture automated UI screenshots");
        }
        finally
        {
            Shutdown(0);
        }
    }

    private static void FlushUi(Window window)
    {
        window.UpdateLayout();
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new DispatcherOperationCallback(f =>
        {
            ((DispatcherFrame)f).Continue = false;
            return null;
        }), frame);
        Dispatcher.PushFrame(frame);
        window.UpdateLayout();
    }

    private static void SaveVisualToPng(Visual visual, int width, int height, string filePath)
    {
        var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var stream = File.Create(filePath);
        encoder.Save(stream);
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
