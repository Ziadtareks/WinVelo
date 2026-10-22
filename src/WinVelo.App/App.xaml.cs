using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using WinVelo.App.Localization;
using WinVelo.App.Services;
using WinVelo.App.ViewModels;
using WinVelo.App.Views;
using WinVelo.Core.Engine;
using WinVelo.Core.Interfaces;
using WinVelo.Core.Operations;
using WinVelo.Core.Safety;
using WinVelo.Core.Updates;
using WinVelo.Modules.Advisor;
using WinVelo.Modules.Apps;
using WinVelo.Modules.Cleaner;
using WinVelo.Modules.Config;
using WinVelo.Modules.Memory;
using WinVelo.Modules.Network;
using WinVelo.Modules.Services;
using WinVelo.Modules.Startup;
using WinVelo.Modules.Tweaks;
using WinVelo.Modules.Updates;
using WinVelo.Platform;
using WinVelo.Simulation;

namespace WinVelo.App;

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
                          string.Equals(Environment.GetEnvironmentVariable("WINVELO_TEST_MODE"), "1", StringComparison.OrdinalIgnoreCase);

        if (isTestMode)
        {
            TestModeManager.EnableTestMode();
            WinVeloStoragePaths.InitializeStorage(true);
        }
        else
        {
            TestModeManager.DisableTestMode();
            if (AppAdminGuard.TryRelaunchElevated(args))
            {
                // The elevated copy takes over; nothing has been opened or written yet.
                Environment.Exit(0);
            }
            WinVeloStoragePaths.InitializeStorage(false);
            var (winBuild, winEdition) = WinVelo.Platform.WindowsVersionInfo.Read();
            WinVelo.Core.WindowsCompatibility.Configure(winBuild, winEdition);
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
                services.AddSingleton<IOperationRunner>(provider =>
                {
                    var runner = new OperationRunner(provider.GetRequiredService<IActivityTracker>());
                    runner.OnLog += message => Log.Information("{OperationMessage}", message);
                    return runner;
                });

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
                services.AddSingleton(new HttpClient { Timeout = TimeSpan.FromSeconds(15) });
                services.AddSingleton<IUpdateChecker, GitHubUpdateChecker>();

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

    /// <summary>
    /// Picks the UI language before any window or view model is created:
    /// "--lang ar" on the command line wins, otherwise the saved setting (default English).
    /// </summary>
    private void ApplyLanguage(string[] args)
    {
        string? lang = null;
        int idx = Array.FindIndex(args, a => string.Equals(a, "--lang", StringComparison.OrdinalIgnoreCase));
        if (idx >= 0 && idx + 1 < args.Length)
        {
            lang = args[idx + 1];
        }

        if (lang == null)
        {
            try
            {
                lang = _host.Services.GetRequiredService<IAppSettingsService>().GetSettings().Language;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Could not read the language setting; using English.");
            }
        }

        string code = LocalizationManager.NormalizeCode(lang);
        LocalizationManager.Instance.SetLanguage(code);
        WinVelo.Core.CatalogTranslations.Language = code;
        Log.Information("UI language: {Language}", code);
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            Log.Information("Starting WinVelo application host (TestMode={IsTestMode})...", TestModeManager.IsTestMode);
            await _host.StartAsync();

            ApplyLanguage(e.Args);
            ConfirmCustomCatalog(e.Args);

            MainWindow window = _host.Services.GetRequiredService<MainWindow>();
            MainWindow = window;
            window.Show();

            Log.Information("WinVelo main window shown successfully.");

            _ = Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => ReportCatalogProblems(window)));

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
                LocalizationManager.T("WinVelo could not start: {0}\n\nNothing on your PC was changed. Details are in the log folder:\n{1}", ex.Message, WinVeloStoragePaths.GetLogsDirectory(TestModeManager.IsTestMode)),
                LocalizationManager.T("WinVelo Startup Error"),
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );

            Shutdown(1);
        }
    }

    /// <summary>
    /// A custom catalog folder (--catalog <folder>) can contain PowerShell that runs as administrator. It is used
    /// only when the user confirms it here; otherwise WinVelo keeps its built-in catalog.
    /// </summary>
    private static void ConfirmCustomCatalog(string[] args)
    {
        string? folder = WinVelo.Core.Catalog.CatalogSource.RequestedFolder(args);
        if (string.IsNullOrWhiteSpace(folder)) return;

        string full;
        try
        {
            full = Path.GetFullPath(folder);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            Log.Warning(ex, "Ignoring invalid --catalog path {Folder}", folder);
            return;
        }

        var answer = MessageBox.Show(
            LocalizationManager.T("WinVelo was started with a custom tweak catalog:\n{0}\n\nWARNING: files in this folder replace the built-in tweaks and can run PowerShell scripts with administrator rights. Use it only if you wrote these files yourself or fully trust where they came from.\n\nUse this custom catalog?\nYes: use it.  No: use the built-in catalog.", full),
            LocalizationManager.T("Custom catalog: administrator warning"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (answer == MessageBoxResult.Yes)
        {
            WinVelo.Core.Catalog.CatalogSource.SetDefault(WinVelo.Core.Catalog.CatalogSource.FromOverrideFolder(full));
            Log.Warning("The user confirmed the custom catalog at {Path}.", full);
        }
        else
        {
            Log.Information("The user declined the custom catalog at {Path}; using the built-in catalog.", full);
        }
    }

    /// <summary>
    /// Tells the user, once, when parts of the catalog could not be read. Broken files or tweaks are skipped,
    /// so the app keeps working with everything else.
    /// </summary>
    private void ReportCatalogProblems(Window owner)
    {
        try
        {
            var tweakService = _host.Services.GetRequiredService<ITweakService>();
            int count = tweakService.LoadCatalog().Count;
            var issues = tweakService.CatalogIssues;
            var source = WinVelo.Core.Catalog.CatalogSource.Default;
            if (source.UsesExternalFolder)
            {
                Log.Warning("Using the confirmed custom catalog at {Path}; its files replace the built-in catalog.", source.ExternalPath);
            }
            foreach (var issue in issues)
            {
                Log.Warning("Catalog problem: {Issue}", issue.ToString());
            }

            if (count == 0)
            {
                MessageBox.Show(owner,
                    LocalizationManager.T("WinVelo could not load any tweaks.\n\nIf you started it with --catalog, that folder may contain broken files: fix it, or start WinVelo without --catalog to use its built-in tweak list.\n\nDetails are in the log folder:\n{0}", WinVeloStoragePaths.GetLogsDirectory(TestModeManager.IsTestMode)),
                    LocalizationManager.T("Tweak list not available"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            else if (issues.Count > 0)
            {
                string list = string.Join("\n", issues.Take(8).Select(i => "• " + i.ToString()));
                if (issues.Count > 8) list += "\n…";
                MessageBox.Show(owner,
                    LocalizationManager.T("Some catalog entries could not be read and were skipped. Everything else works normally.\n\n{0}\n\nDetails are in the log folder:\n{1}", list, WinVeloStoragePaths.GetLogsDirectory(TestModeManager.IsTestMode)),
                    LocalizationManager.T("Some tweaks were skipped"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not check the catalog for problems.");
        }

        try
        {
            var journal = _host.Services.GetRequiredService<RollbackJournalManager>();
            journal.GetAllSessions();
            var damaged = journal.DamagedFiles;
            if (damaged.Count > 0)
            {
                foreach (string file in damaged)
                {
                    Log.Error("Rollback journal file could not be read and was left untouched: {File}", file);
                }
                MessageBox.Show(owner,
                    LocalizationManager.T("{0} rollback journal file(s) could not be read. WinVelo did not delete them, but it cannot undo the changes they describe.\n\nThe files are in:\n{1}\n\nPlease keep them and report this on GitHub.", damaged.Count, WinVeloStoragePaths.GetJournalDirectory(TestModeManager.IsTestMode)),
                    LocalizationManager.T("Rollback journal problem"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not check the rollback journal for damaged files.");
        }
    }

    private void CaptureAllScreenshots(MainWindow window, string targetDir)
    {
        try
        {
            Log.Information("Starting automated UI screenshot capture to {TargetDir}...", targetDir);
            Directory.CreateDirectory(targetDir);

            window.Width = 1440;
            window.Height = 900;
            FlushUi(window);

            var vm = (MainViewModel)window.DataContext;
            // Dismiss wizard so every page capture is clean
            vm.IsWizardOpen = false;
            FlushUi(window);

            Wpf.Ui.Appearance.ApplicationThemeManager.Apply(Wpf.Ui.Appearance.ApplicationTheme.Dark);
            DesignThemeManager.ApplyTheme(Wpf.Ui.Appearance.ApplicationTheme.Dark);
            FlushUi(window);

            vm.SwitchTabCommand.Execute("Home");
            FlushUi(window);
            SaveVisualToPng(window, 1440, 900, Path.Combine(targetDir, "home.png"));

            vm.SwitchTabCommand.Execute("Tweaks");
            FlushUi(window);
            SaveVisualToPng(window, 1440, 900, Path.Combine(targetDir, "tweaks.png"));

            vm.SwitchTabCommand.Execute("Apps");
            FlushUi(window);
            SaveVisualToPng(window, 1440, 900, Path.Combine(targetDir, "apps.png"));

            vm.SwitchTabCommand.Execute("Tools");
            FlushUi(window);
            SaveVisualToPng(window, 1440, 900, Path.Combine(targetDir, "tools.png"));

            vm.SwitchTabCommand.Execute("History");
            FlushUi(window);
            SaveVisualToPng(window, 1440, 900, Path.Combine(targetDir, "history.png"));

            vm.SwitchTabCommand.Execute("Settings");
            FlushUi(window);
            SaveVisualToPng(window, 1440, 900, Path.Combine(targetDir, "settings.png"));

            // Unified Execution Preview Dialog (Home -> Review on a Preset: clean-ui Full)
            vm.SwitchTabCommand.Execute("Home");
            FlushUi(window);
            var preset = vm.DashboardVM.Profiles.FirstOrDefault(p => p.Id == "preset.clean-ui") ?? vm.DashboardVM.Profiles.FirstOrDefault();
            if (preset != null)
            {
                preset.IsFullTier = true;
                FlushUi(window);
                preset.ApplyCommand.Execute(null);
                FlushUi(window);
                Thread.Sleep(500);
                FlushUi(window);
                SaveVisualToPng(window, 1440, 900, Path.Combine(targetDir, "preview.png"));
                vm.ExecutionDialogVM.CancelPreview();
                FlushUi(window);
            }

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
                    LocalizationManager.T("Something went wrong: {0}\n\nWinVelo is still running. Details are in the log folder:\n{1}", e.Exception.Message, WinVeloStoragePaths.GetLogsDirectory(TestModeManager.IsTestMode)),
                    LocalizationManager.T("WinVelo Error"),
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
                        LocalizationManager.T("WinVelo has to close because of an unexpected error: {0}\n\nDetails are in the log folder:\n{1}", ex.Message, WinVeloStoragePaths.GetLogsDirectory(TestModeManager.IsTestMode)),
                        LocalizationManager.T("WinVelo Fatal Error"),
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

