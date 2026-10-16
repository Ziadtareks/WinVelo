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
using Tweakora.Modules.Advisor;
using Tweakora.Modules.Apps;
using Tweakora.Modules.Cleaner;
using Tweakora.Modules.Config;
using Tweakora.Modules.Memory;
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
    private static readonly System.Diagnostics.Stopwatch _startupStopwatch;
    private static int _isShowingDispatcherDialog = 0;
    private static int _isShowingAppDomainDialog = 0;
    private static int _unhandledErrorCount = 0;

    static App()
    {
        _startupStopwatch = System.Diagnostics.Stopwatch.StartNew();
    }

    public App()
    {
        // 1. Detect Test Mode from command line arguments or environment variable
        string[] args = Environment.GetCommandLineArgs();
        bool isTestMode = args.Any(a => string.Equals(a, "--test-mode", StringComparison.OrdinalIgnoreCase)) ||
                          string.Equals(Environment.GetEnvironmentVariable("WINVELO_TEST_MODE"), "1", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(Environment.GetEnvironmentVariable("TWEAKORA_TEST_MODE"), "1", StringComparison.OrdinalIgnoreCase);

        if (isTestMode)
        {
            TestModeManager.EnableTestMode();
            WinVeloStoragePaths.InitializeStorage(true);
        }
        else
        {
            TestModeManager.DisableTestMode();
            WinVeloStoragePaths.InitializeStorage(false);
        }

        // 2. Initialize logging so all crashes are recorded in the isolated log directory
        LoggingConfiguration.InitializeLogging(WinVeloStoragePaths.GetLogsDirectory(isTestMode));

        // 3. Global Crash Handlers with re-entrancy and storm guards
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        if (isTestMode)
        {
            Log.Warning("⚠️ WinVelo launched in TEST MODE. All system operations are simulated with in-memory fakes. Storage isolated at {StorageRoot}", WinVeloStoragePaths.GetStorageRoot(true));
        }
        else
        {
            Log.Information("WinVelo launched in NORMAL PRODUCTION MODE. Using real Windows platform drivers.");
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
                    services.AddSingleton<IAppSettingsService>(new AppSettingsService(WinVeloStoragePaths.GetSettingsPath(true)));
                }
                else
                {
                    services.AddSingleton<IRegistryService, WindowsRegistryService>();
                    services.AddSingleton<ISystemServicesService, WindowsServicesService>();
                    services.AddSingleton<ITaskSchedulerService, WindowsTaskSchedulerService>();
                    services.AddSingleton<ISystemRestoreService, WindowsRestoreService>();
                    services.AddSingleton<IShellService, WindowsShellService>();
                    services.AddSingleton<IAppSettingsService>(new AppSettingsService(WinVeloStoragePaths.GetSettingsPath(false)));
                }

                // Core Engine and Operation Infrastructure
                if (!isTestMode)
                {
                    AppDataMigrator.Migrate(logCallback: (msg, ex) =>
                    {
                        if (ex != null) Log.Warning(ex, "{Message}", msg);
                        else Log.Information("{Message}", msg);
                    });
                }

                string journalDir = WinVeloStoragePaths.GetJournalDirectory(isTestMode);
                services.AddSingleton(new RollbackJournalManager(journalDir));
                services.AddSingleton<IActivityTracker, ActivityTracker>();
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
                services.AddSingleton<IMemoryOptimizerService, MemoryOptimizerService>();
                services.AddSingleton<INetworkOptimizerService, NetworkOptimizerService>();
                services.AddSingleton<ISmartAdvisorService, SmartAdvisorService>();

                // ViewModels
                services.AddSingleton<DashboardViewModel>();
                services.AddSingleton<InstallViewModel>();
                services.AddSingleton<TweaksViewModel>();
                services.AddSingleton<ConfigViewModel>();
                services.AddSingleton<UpdatesViewModel>();
                services.AddSingleton<ToolboxViewModel>();
                services.AddSingleton<StartupViewModel>();
                services.AddSingleton<ToolsViewModel>();
                services.AddSingleton<HistoryViewModel>();
                services.AddSingleton<FirstRunWizardViewModel>();
                services.AddSingleton<SettingsViewModel>();
                services.AddSingleton<UnifiedExecutionDialogViewModel>();
                services.AddSingleton<MainViewModel>();

                // Views
                services.AddSingleton<MainWindow>();
            })
            .Build();
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int dwProcessId);

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            Log.Information("Starting WinVelo application host (TestMode={IsTestMode})...", TestModeManager.IsTestMode);
            await _host.StartAsync();

            MainWindow window = _host.Services.GetRequiredService<MainWindow>();
            MainWindow = window;
            window.Show();

            Log.Information("WinVelo main window shown successfully.");

            // Item 0b Startup instrumentation: measure time to first Home render
            await Dispatcher.InvokeAsync(() =>
            {
                if (_startupStopwatch.IsRunning)
                {
                    _startupStopwatch.Stop();
                }
                long elapsedMs = _startupStopwatch.ElapsedMilliseconds;
                Log.Information("Startup completed to first Home render: {ElapsedMs} ms", elapsedMs);

                if (e.Args.Any(a => string.Equals(a, "--measure-startup", StringComparison.OrdinalIgnoreCase)))
                {
                    if (TestModeManager.IsTestMode)
                    {
                        AttachConsole(-1);
                        using var writer = new System.IO.StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
                        Console.SetOut(writer);
                        Console.WriteLine($"{elapsedMs} ms");
                        Console.Out.Flush();
                        Shutdown(0);
                    }
                    else
                    {
                        AttachConsole(-1);
                        using var errWriter = new System.IO.StreamWriter(Console.OpenStandardError()) { AutoFlush = true };
                        Console.SetError(errWriter);
                        Console.Error.WriteLine("Error: --measure-startup is only supported in --test-mode.");
                        Console.Error.Flush();
                        Shutdown(1);
                    }
                }
            }, DispatcherPriority.Loaded);

            if (e.Args.Any(a => string.Equals(a, "--measure-startup", StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            if (e.Args.Any(a => string.Equals(a, "--capture-screenshots", StringComparison.OrdinalIgnoreCase)))
            {
                int idx = Array.FindIndex(e.Args, a => string.Equals(a, "--capture-screenshots", StringComparison.OrdinalIgnoreCase));
                string targetDir = (idx >= 0 && idx + 1 < e.Args.Length && !e.Args[idx + 1].StartsWith("--")) ? e.Args[idx + 1] : "docs/screenshots";
                
                await Dispatcher.InvokeAsync(() =>
                {
                    CaptureAllScreenshots(window, targetDir);
                }, DispatcherPriority.ApplicationIdle);
                return;
            }
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Critical failure during application startup");
            MessageBox.Show(
                $"WinVelo failed to start:\n\n{ex.Message}\n\nStack Trace:\n{ex.StackTrace}\n\nPlease check the logs in %LOCALAPPDATA%\\WinVelo\\Logs for details.",
                "WinVelo Startup Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );

            Shutdown(1);
        }
    }

    private static Visual GetWindowVisual(MainWindow window) => (Visual?)window.Content ?? window;

    private void CaptureAllScreenshots(MainWindow window, string targetDir)
    {
        try
        {
            Log.Information("Starting automated UI screenshot capture to {TargetDir}...", targetDir);
            Directory.CreateDirectory(targetDir);
            string darkDir = Path.Combine(targetDir, "dark");
            string lightDir = Path.Combine(targetDir, "light");
            Directory.CreateDirectory(darkDir);
            Directory.CreateDirectory(lightDir);

            window.Width = 1440;
            window.Height = 900;
            FlushUi(window);

            var vm = (MainViewModel)window.DataContext;
            // Dismiss wizard initially so all page captures are clean
            vm.IsWizardOpen = false;
            FlushUi(window);

            // ==================== 1. DARK THEME CAPTURES ====================
            Wpf.Ui.Appearance.ApplicationThemeManager.Apply(Wpf.Ui.Appearance.ApplicationTheme.Dark);
            DesignThemeManager.ApplyTheme(Wpf.Ui.Appearance.ApplicationTheme.Dark);
            FlushUi(window);

            // Home
            vm.SwitchTabCommand.Execute("Home");
            FlushUi(window);
            SaveVisualToPng(window, 1440, 900, Path.Combine(darkDir, "home.png"));
            SaveVisualToPng(window, 1440, 900, Path.Combine(targetDir, "home.png"));
            SaveVisualToPng(window, 1440, 900, Path.Combine(targetDir, "dashboard.png"));

            // Tweaks
            vm.SwitchTabCommand.Execute("Tweaks");
            FlushUi(window);
            SaveVisualToPng(window, 1440, 900, Path.Combine(darkDir, "tweaks.png"));
            SaveVisualToPng(window, 1440, 900, Path.Combine(targetDir, "tweaks.png"));

            // Apps
            vm.SwitchTabCommand.Execute("Apps");
            FlushUi(window);
            SaveVisualToPng(window, 1440, 900, Path.Combine(darkDir, "apps.png"));
            SaveVisualToPng(window, 1440, 900, Path.Combine(targetDir, "apps.png"));
            SaveVisualToPng(window, 1440, 900, Path.Combine(targetDir, "install.png"));

            // Tools
            vm.SwitchTabCommand.Execute("Tools");
            FlushUi(window);
            SaveVisualToPng(window, 1440, 900, Path.Combine(darkDir, "tools.png"));
            SaveVisualToPng(window, 1440, 900, Path.Combine(targetDir, "tools.png"));

            // History
            vm.SwitchTabCommand.Execute("History");
            FlushUi(window);
            SaveVisualToPng(window, 1440, 900, Path.Combine(darkDir, "history.png"));
            SaveVisualToPng(window, 1440, 900, Path.Combine(targetDir, "history.png"));

            // Settings
            vm.SwitchTabCommand.Execute("Settings");
            FlushUi(window);
            SaveVisualToPng(window, 1440, 900, Path.Combine(darkDir, "settings.png"));
            SaveVisualToPng(window, 1440, 900, Path.Combine(targetDir, "settings.png"));

            // Unified Execution Preview Dialog (Home -> Review on a Preset: clean-ui Full)
            vm.SwitchTabCommand.Execute("Home");
            FlushUi(window);
            var bigPreset = vm.DashboardVM.Profiles.FirstOrDefault(p => p.Id == "preset.clean-ui") ?? vm.DashboardVM.Profiles.FirstOrDefault();
            if (bigPreset != null)
            {
                bigPreset.IsFullTier = true;
                FlushUi(window);
                bigPreset.ApplyCommand.Execute(null);
                FlushUi(window);
                System.Threading.Thread.Sleep(500);
                FlushUi(window);
                SaveVisualToPng(window, 1440, 900, Path.Combine(darkDir, "preview.png"));
                SaveVisualToPng(window, 1440, 900, Path.Combine(targetDir, "preview.png"));
                SaveVisualToPng(window, 1440, 900, Path.Combine(targetDir, "diff-preview.png"));
                vm.ExecutionDialogVM.CancelPreview();
                FlushUi(window);
            }

            // Command Palette Overlay
            vm.ToggleCommandPaletteCommand.Execute(null);
            FlushUi(window);
            SaveVisualToPng(window, 1440, 900, Path.Combine(darkDir, "command-palette.png"));
            SaveVisualToPng(window, 1440, 900, Path.Combine(targetDir, "command-palette.png"));
            vm.ToggleCommandPaletteCommand.Execute(null);
            FlushUi(window);

            // First-Run Wizard Overlay (Opaque modal surface, 60% dim backdrop)
            vm.OpenFirstRunWizard();
            FlushUi(window);
            SaveVisualToPng(window, 1440, 900, Path.Combine(darkDir, "wizard.png"));
            SaveVisualToPng(window, 1440, 900, Path.Combine(targetDir, "wizard.png"));
            vm.WizardVM.FinishCommand.Execute(null);
            vm.IsWizardOpen = false;
            FlushUi(window);

            // ==================== 2. LIGHT THEME CAPTURES ====================
            Wpf.Ui.Appearance.ApplicationThemeManager.Apply(Wpf.Ui.Appearance.ApplicationTheme.Light);
            DesignThemeManager.ApplyTheme(Wpf.Ui.Appearance.ApplicationTheme.Light);
            FlushUi(window);

            // Home (Light)
            vm.SwitchTabCommand.Execute("Home");
            FlushUi(window);
            SaveVisualToPng(window, 1440, 900, Path.Combine(lightDir, "home.png"));

            // Tweaks (Light)
            vm.SwitchTabCommand.Execute("Tweaks");
            FlushUi(window);
            SaveVisualToPng(window, 1440, 900, Path.Combine(lightDir, "tweaks.png"));

            // Apps (Light)
            vm.SwitchTabCommand.Execute("Apps");
            FlushUi(window);
            SaveVisualToPng(window, 1440, 900, Path.Combine(lightDir, "apps.png"));

            // Tools (Light)
            vm.SwitchTabCommand.Execute("Tools");
            FlushUi(window);
            SaveVisualToPng(window, 1440, 900, Path.Combine(lightDir, "tools.png"));

            // History (Light)
            vm.SwitchTabCommand.Execute("History");
            FlushUi(window);
            SaveVisualToPng(window, 1440, 900, Path.Combine(lightDir, "history.png"));

            // Settings (Light)
            vm.SwitchTabCommand.Execute("Settings");
            FlushUi(window);
            SaveVisualToPng(window, 1440, 900, Path.Combine(lightDir, "settings.png"));

            // Preview (Light - Home -> Review on a Preset: clean-ui Full)
            vm.SwitchTabCommand.Execute("Home");
            FlushUi(window);
            var lightBigPreset = vm.DashboardVM.Profiles.FirstOrDefault(p => p.Id == "preset.clean-ui") ?? vm.DashboardVM.Profiles.FirstOrDefault();
            if (lightBigPreset != null)
            {
                lightBigPreset.IsFullTier = true;
                FlushUi(window);
                lightBigPreset.ApplyCommand.Execute(null);
                FlushUi(window);
                System.Threading.Thread.Sleep(500);
                FlushUi(window);
                SaveVisualToPng(window, 1440, 900, Path.Combine(lightDir, "preview.png"));
                vm.ExecutionDialogVM.CancelPreview();
                FlushUi(window);
            }

            // First-Run Wizard (Light)
            vm.OpenFirstRunWizard();
            FlushUi(window);
            SaveVisualToPng(window, 1440, 900, Path.Combine(lightDir, "wizard.png"));
            vm.WizardVM.FinishCommand.Execute(null);
            vm.IsWizardOpen = false;
            FlushUi(window);

            // Restore Dark Theme default
            Wpf.Ui.Appearance.ApplicationThemeManager.Apply(Wpf.Ui.Appearance.ApplicationTheme.Dark);
            DesignThemeManager.ApplyTheme(Wpf.Ui.Appearance.ApplicationTheme.Dark);
            FlushUi(window);

            // ==================== 3. GENERATE OVERVIEW CONTACT SHEET ====================
            BuildContactSheet(targetDir, Path.Combine(targetDir, "overview.png"));

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

    private static void BuildContactSheet(string targetDir, string overviewPath)
    {
        try
        {
            targetDir = Path.GetFullPath(targetDir);
            overviewPath = Path.GetFullPath(overviewPath);
            string[] files = { "home.png", "tweaks.png", "apps.png", "tools.png", "history.png", "settings.png", "wizard.png", "preview.png" };
            string[] titles = { "Home", "Tweaks (ShutUp10++ List)", "Apps (Responsive Grid)", "Tools (Tile Grid)", "History & Activity Log", "Settings", "Setup Wizard (Modal)", "Preview Dialog" };

            int cols = 4;
            int rows = 2;
            int thumbW = 440;
            int thumbH = 275;
            int pad = 24;
            int topPad = 60;

            int totalW = pad + cols * (thumbW + pad);
            int totalH = topPad + rows * (thumbH + pad + 24);

            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                // Background
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x0F, 0x11, 0x17)), null, new Rect(0, 0, totalW, totalH));

                // Title
                var titleText = new FormattedText(
                    $"WINVELO {Tweakora.Core.AppInfo.DisplayVersion.ToUpperInvariant()} — VISUAL CONTACT SHEET",
                    System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    new Typeface("Segoe UI Variable Display, Segoe UI, sans-serif"),
                    20,
                    new SolidColorBrush(Color.FromRgb(0xF9, 0xFA, 0xFB)),
                    1.0);
                dc.DrawText(titleText, new Point(pad, 18));

                for (int i = 0; i < files.Length; i++)
                {
                    int c = i % cols;
                    int r = i / cols;
                    int x = pad + c * (thumbW + pad);
                    int y = topPad + r * (thumbH + pad + 24);

                    string src = Path.GetFullPath(Path.Combine(targetDir, files[i]));
                    if (File.Exists(src))
                    {
                        var bi = new BitmapImage();
                        bi.BeginInit();
                        bi.UriSource = new Uri(src, UriKind.Absolute);
                        bi.CacheOption = BitmapCacheOption.OnLoad;
                        bi.EndInit();

                        // Border card
                        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(0x16, 0x19, 0x22)),
                            new Pen(new SolidColorBrush(Color.FromRgb(0x2A, 0x30, 0x3C)), 1),
                            new Rect(x - 2, y - 2, thumbW + 4, thumbH + 4), 6, 6);

                        dc.DrawImage(bi, new Rect(x, y, thumbW, thumbH));

                        // Label
                        var caption = new FormattedText(
                            titles[i],
                            System.Globalization.CultureInfo.InvariantCulture,
                            FlowDirection.LeftToRight,
                            new Typeface("Segoe UI, sans-serif"),
                            13,
                            new SolidColorBrush(Color.FromRgb(0xD1, 0xD5, 0xDB)),
                            1.0);
                        dc.DrawText(caption, new Point(x, y + thumbH + 6));
                    }
                }
            }

            var rtb = new RenderTargetBitmap(totalW, totalH, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            using var stream = File.Create(overviewPath);
            encoder.Save(stream);
            Log.Information("Generated contact sheet at {OverviewPath}", overviewPath);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to generate contact sheet overview");
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
        if (visual is UIElement ui)
        {
            ui.UpdateLayout();
        }

        var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        string? dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        using var stream = File.Create(filePath);
        encoder.Save(stream);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        try
        {
            Log.Information("WinVelo is shutting down.");
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
                    "WinVelo Error",
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
                        "WinVelo Fatal Error",
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

