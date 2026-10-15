using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using Tweakora.App.Localization;
using Tweakora.App.Services;
using Tweakora.Core.Interfaces;
using Tweakora.Core.Models;
using Tweakora.Core.Operations;
using Tweakora.Core.Safety;
using Tweakora.Modules.Tweaks;
using Tweakora.Platform;
using Wpf.Ui.Appearance;

namespace Tweakora.App.ViewModels;

public sealed class PaletteItem
{
    public required string Title { get; init; }
    public required string Subtitle { get; init; }
    public required string Category { get; init; }
    public string IconSymbol { get; init; } = "Search24";
    public required Action ActionToExecute { get; init; }
}

public sealed partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    private object _currentPage;

    [ObservableProperty]
    private string _activeTab = "Tweaks"; // Default to Tweaks view

    [ObservableProperty]
    private string _topSearchText = string.Empty;

    [ObservableProperty]
    private bool _isCommandPaletteOpen;

    [ObservableProperty]
    private bool _isAboutDialogOpen;

    [ObservableProperty]
    private bool _isConfigDialogOpen;

    [ObservableProperty]
    private bool _isRecoveryDialogOpen;

    [ObservableProperty]
    private string _recoveryDialogMessage = string.Empty;

    [ObservableProperty]
    private string _configDialogText = string.Empty;

    [ObservableProperty]
    private string _paletteQuery = string.Empty;

    [ObservableProperty]
    private string _currentThemeName = "Dark";

    // Activity HUD properties
    [ObservableProperty]
    private bool _isActivityVisible;

    [ObservableProperty]
    private bool _isActivityRunning;

    [ObservableProperty]
    private bool _isActivitySuccess;

    [ObservableProperty]
    private string _activityText = string.Empty;

    [ObservableProperty]
    private string _activityDoneText = string.Empty;

    public LocalizationManager Localization => LocalizationManager.Instance;
    public UiLogSink LogSink => UiLogSink.Instance;

    public bool IsTestModeActive => TestModeManager.IsTestMode;
    public bool IsAdministrator { get; } = AdminPrivilegeHelper.IsRunningAsAdministrator();
    public bool IsMissingAdministratorPrivileges => !IsAdministrator;

    public SystemInfoSummary SystemInfo { get; } = SystemInfoHelper.GetSystemInfo();

    public DashboardViewModel DashboardVM { get; }
    public InstallViewModel InstallVM { get; }
    public TweaksViewModel TweaksVM { get; }
    public ConfigViewModel ConfigVM { get; }
    public UpdatesViewModel UpdatesVM { get; }
    public ToolboxViewModel ToolboxVM { get; }
    public StartupViewModel StartupVM { get; }
    public ToolsViewModel ToolsVM { get; }
    public HistoryViewModel HistoryVM { get; }
    public FirstRunWizardViewModel WizardVM { get; }
    public UnifiedExecutionDialogViewModel ExecutionDialogVM { get; }

    [ObservableProperty]
    private bool _isWizardOpen;

    private readonly IAppSettingsService _appSettings;
    private readonly List<PaletteItem> _allPaletteItems = new();
    public ObservableCollection<PaletteItem> PaletteResults { get; } = new();

    private readonly IActivityTracker? _activityTracker;
    private readonly ITweakService? _tweakService;
    private List<RollbackJournalSession> _unfinishedSessions = new();
    private DispatcherTimer? _hudHideTimer;

    public MainViewModel(
        DashboardViewModel dashboardVM,
        InstallViewModel installVM,
        TweaksViewModel tweaksVM,
        ConfigViewModel configVM,
        UpdatesViewModel updatesVM,
        ToolboxViewModel toolboxVM,
        StartupViewModel startupVM,
        ToolsViewModel? toolsVM = null,
        HistoryViewModel? historyVM = null,
        IActivityTracker? activityTracker = null,
        ITweakService? tweakService = null,
        IAppSettingsService? appSettings = null,
        ISystemRestoreService? restoreService = null,
        FirstRunWizardViewModel? wizardVM = null,
        UnifiedExecutionDialogViewModel? executionDialogVM = null)
    {
        DashboardVM = dashboardVM;
        InstallVM = installVM;
        TweaksVM = tweaksVM;
        ConfigVM = configVM;
        UpdatesVM = updatesVM;
        ToolboxVM = toolboxVM;
        StartupVM = startupVM;
        _activityTracker = activityTracker;
        _tweakService = tweakService;

        _appSettings = appSettings ?? new AppSettingsService();
        var resSvc = restoreService ?? new NullRestoreService();
        ExecutionDialogVM = executionDialogVM ?? new UnifiedExecutionDialogViewModel(tweakService, null, activityTracker);
        ToolsVM = toolsVM ?? new ToolsViewModel(startupVM, updatesVM, configVM, toolboxVM, resSvc);
        HistoryVM = historyVM ?? new HistoryViewModel(tweakService, resSvc, null, ExecutionDialogVM);
        WizardVM = wizardVM ?? new FirstRunWizardViewModel(resSvc, _appSettings);
        WizardVM.WizardCompleted += OnWizardCompleted;

        var userSettings = _appSettings.GetSettings();
        if (!userSettings.HasCompletedFirstRunWizard)
        {
            _isWizardOpen = true;
        }

        _currentPage = DashboardVM;
        _activeTab = "Home";

        if (_activityTracker != null)
        {
            _activityTracker.OnActivityChanged += (active, text) =>
            {
                Application.Current?.Dispatcher?.InvokeAsync(() =>
                {
                    if (active)
                    {
                        _hudHideTimer?.Stop();
                        IsActivityVisible = true;
                        IsActivityRunning = true;
                        IsActivitySuccess = false;
                        ActivityText = text;
                    }
                    else
                    {
                        IsActivityRunning = false;
                    }
                });
            };

            _activityTracker.OnActivityCompleted += (msg) =>
            {
                Application.Current?.Dispatcher?.InvokeAsync(() =>
                {
                    IsActivityRunning = false;
                    IsActivitySuccess = true;
                    ActivityDoneText = msg;
                    ActivityText = msg;

                    _hudHideTimer?.Stop();
                    _hudHideTimer = new DispatcherTimer
                    {
                        Interval = TimeSpan.FromMilliseconds(1200)
                    };
                    _hudHideTimer.Tick += (s, e) =>
                    {
                        _hudHideTimer?.Stop();
                        IsActivityVisible = false;
                        IsActivitySuccess = false;
                    };
                    _hudHideTimer.Start();
                });
            };
        }

        InitializePalette();
    }

    partial void OnTopSearchTextChanged(string value)
    {
        if (ActiveTab == "Tweaks")
        {
            TweaksVM.SetSearchQuery(value);
        }
        else if (ActiveTab == "Install")
        {
            InstallVM.SetSearchQuery(value);
        }
        else if (ActiveTab == "Startup")
        {
            StartupVM.SearchText = value;
        }
    }

    [RelayCommand]
    public void NavigateCategory(string category)
    {
        CurrentPage = TweaksVM;
        ActiveTab = "Tweaks";
        TweaksVM.SelectCategory(category);
    }

    [RelayCommand]
    public void SwitchTab(string tabName)
    {
        switch (tabName)
        {
            case "Home" or "Dashboard":
                ActiveTab = "Home";
                CurrentPage = DashboardVM;
                _ = DashboardVM.RefreshAllToggleStatesAsync();
                break;
            case "Tweaks":
                ActiveTab = "Tweaks";
                CurrentPage = TweaksVM;
                _ = TweaksVM.RefreshActiveStatesAsync();
                break;
            case "Apps" or "Install":
                ActiveTab = "Apps";
                CurrentPage = InstallVM;
                break;
            case "Tools" or "Toolbox":
                ActiveTab = "Tools";
                CurrentPage = ToolsVM;
                break;
            case "History" or "Backups":
                ActiveTab = "History";
                CurrentPage = HistoryVM;
                HistoryVM.RefreshHistory();
                break;

            // Direct routes to sub-tools inside Tools:
            case "Startup":
                ActiveTab = "Tools";
                ToolsVM.SelectTool("Startup");
                CurrentPage = ToolsVM;
                _ = StartupVM.RefreshStatesSilentAsync();
                break;
            case "Services":
                ActiveTab = "Tools";
                ToolsVM.SelectTool("Services");
                CurrentPage = ToolsVM;
                break;
            case "Cleaner":
                ActiveTab = "Tools";
                ToolsVM.SelectTool("Cleaner");
                CurrentPage = ToolsVM;
                break;
            case "DNS" or "Network":
                ActiveTab = "Tools";
                ToolsVM.SelectTool("DNS");
                CurrentPage = ToolsVM;
                break;
            case "Config" or "Fixes":
                ActiveTab = "Tools";
                ToolsVM.SelectTool("Fixes");
                CurrentPage = ToolsVM;
                break;
            case "Updates":
                ActiveTab = "Tools";
                ToolsVM.SelectTool("Updates");
                CurrentPage = ToolsVM;
                break;
            case "Features":
                ActiveTab = "Tools";
                ToolsVM.SelectTool("Features");
                CurrentPage = ToolsVM;
                break;
            case "RestorePoints":
                ActiveTab = "Tools";
                ToolsVM.SelectTool("RestorePoints");
                CurrentPage = ToolsVM;
                break;
            default:
                ActiveTab = "Home";
                CurrentPage = DashboardVM;
                break;
        }

        if (!string.IsNullOrEmpty(TopSearchText))
        {
            OnTopSearchTextChanged(TopSearchText);
        }
    }

    private DateTime _lastFocusScan = DateTime.MinValue;

    public async Task RefreshActiveStatesOnFocusAsync()
    {
        if (DateTime.UtcNow - _lastFocusScan < TimeSpan.FromSeconds(1)) return;
        _lastFocusScan = DateTime.UtcNow;

        await Task.Yield();
        await Task.Run(async () =>
        {
            try
            {
                await DashboardVM.RefreshAllToggleStatesAsync();
                if (ActiveTab == "Tweaks")
                {
                    await TweaksVM.RefreshActiveStatesAsync();
                }
                else if (ActiveTab == "Startup")
                {
                    await StartupVM.RefreshStatesSilentAsync();
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to refresh system states on window focus");
            }
        });
    }

    [RelayCommand]
    public void ToggleTheme()
    {
        var current = ApplicationThemeManager.GetAppTheme();
        var next = current == ApplicationTheme.Dark ? ApplicationTheme.Light : ApplicationTheme.Dark;
        ApplicationThemeManager.Apply(next);
        CurrentThemeName = next.ToString();
    }

    [RelayCommand]
    public void ToggleCommandPalette()
    {
        IsCommandPaletteOpen = !IsCommandPaletteOpen;
        if (IsCommandPaletteOpen)
        {
            PaletteQuery = string.Empty;
            FilterPalette();
        }
    }

    [RelayCommand]
    public void OpenAboutDialog()
    {
        IsAboutDialogOpen = true;
    }

    [RelayCommand]
    public void CloseAboutDialog()
    {
        IsAboutDialogOpen = false;
    }

    [RelayCommand]
    public void ExportConfig()
    {
        ConfigDialogText = System.Text.Json.JsonSerializer.Serialize(new
        {
            Version = "0.9.0-beta",
            ExportedAtUtc = DateTime.UtcNow,
            SelectedTweaks = TweaksVM.GetSelectedTweakIds()
        }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        IsConfigDialogOpen = true;
    }

    [RelayCommand]
    public void CloseConfigDialog()
    {
        IsConfigDialogOpen = false;
    }

    [RelayCommand]
    public void RestartAsAdmin()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? "WinVelo.exe",
                UseShellExecute = true,
                Verb = "runas"
            };
            Process.Start(psi);
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to restart as Administrator.");
        }
    }

    private void InitializePalette()
    {
        _allPaletteItems.Clear();

        // 5 Core Navigation Pages
        _allPaletteItems.Add(new PaletteItem { Title = "Home", Subtitle = "System status overview, admin rights, and presets", Category = "Page", IconSymbol = "Home24", ActionToExecute = () => SwitchTab("Home") });
        _allPaletteItems.Add(new PaletteItem { Title = "Tweaks", Subtitle = "View, filter, and apply Windows tweaks", Category = "Page", IconSymbol = "Apps24", ActionToExecute = () => SwitchTab("Tweaks") });
        _allPaletteItems.Add(new PaletteItem { Title = "Apps", Subtitle = "WinGet software installer and package manager", Category = "Page", IconSymbol = "ArrowDownload24", ActionToExecute = () => SwitchTab("Apps") });
        _allPaletteItems.Add(new PaletteItem { Title = "Tools", Subtitle = "System maintenance, diagnostic repairs, and service manager", Category = "Page", IconSymbol = "Toolbox24", ActionToExecute = () => SwitchTab("Tools") });
        _allPaletteItems.Add(new PaletteItem { Title = "History", Subtitle = "Review journal sessions, rollback tweaks, and restore points", Category = "Page", IconSymbol = "Clock24", ActionToExecute = () => SwitchTab("History") });
        _allPaletteItems.Add(new PaletteItem { Title = "First-Run Setup Wizard", Subtitle = "Configure WinVelo catalog mode and verify system restore", Category = "Settings", IconSymbol = "Settings24", ActionToExecute = OpenFirstRunWizard });

        // 8 Integrated Tools
        _allPaletteItems.Add(new PaletteItem { Title = "Startup Apps Manager", Subtitle = "Control Windows boot applications and startup impact", Category = "Tool", IconSymbol = "Rocket24", ActionToExecute = () => SwitchTab("Startup") });
        _allPaletteItems.Add(new PaletteItem { Title = "Windows Services", Subtitle = "Inspect and manage background services safely", Category = "Tool", IconSymbol = "Settings24", ActionToExecute = () => SwitchTab("Services") });
        _allPaletteItems.Add(new PaletteItem { Title = "Safe Cache Cleaner", Subtitle = "Clean safe temporary folders and update caches", Category = "Tool", IconSymbol = "Broom24", ActionToExecute = () => SwitchTab("Cleaner") });
        _allPaletteItems.Add(new PaletteItem { Title = "DNS Switcher", Subtitle = "Benchmark DNS resolvers and apply lowest latency", Category = "Tool", IconSymbol = "Globe24", ActionToExecute = () => SwitchTab("DNS") });
        _allPaletteItems.Add(new PaletteItem { Title = "Fixes & Repairs", Subtitle = "One-click repairs for Windows components and network", Category = "Tool", IconSymbol = "Wrench24", ActionToExecute = () => SwitchTab("Fixes") });
        _allPaletteItems.Add(new PaletteItem { Title = "Windows Updates Modes", Subtitle = "Configure Default, Recommended, or Suspended updates", Category = "Tool", IconSymbol = "ArrowSync24", ActionToExecute = () => SwitchTab("Updates") });
        _allPaletteItems.Add(new PaletteItem { Title = "Optional Features", Subtitle = "Enable or disable optional system components (WSL, Hyper-V)", Category = "Tool", IconSymbol = "AppsListDetail24", ActionToExecute = () => SwitchTab("Features") });
        _allPaletteItems.Add(new PaletteItem { Title = "Restore Points", Subtitle = "Windows System Protection checkpoints", Category = "Tool", IconSymbol = "ShieldCheckmark24", ActionToExecute = () => SwitchTab("RestorePoints") });

        // Tweak Categories
        _allPaletteItems.Add(new PaletteItem { Title = "All Tweaks", Subtitle = "View all system tweaks", Category = "Category", ActionToExecute = () => NavigateCategory("All") });
        _allPaletteItems.Add(new PaletteItem { Title = "Essential Tweaks", Subtitle = "Core safe optimizations", Category = "Category", ActionToExecute = () => NavigateCategory("Essential") });
        _allPaletteItems.Add(new PaletteItem { Title = "Privacy & Telemetry", Subtitle = "Disable telemetry and tracking", Category = "Category", ActionToExecute = () => NavigateCategory("Privacy") });
        _allPaletteItems.Add(new PaletteItem { Title = "Performance Tweaks", Subtitle = "System speed and memory optimizations", Category = "Category", ActionToExecute = () => NavigateCategory("Performance") });
        _allPaletteItems.Add(new PaletteItem { Title = "Customize UI", Subtitle = "Taskbar, Explorer and desktop style", Category = "Category", ActionToExecute = () => NavigateCategory("Customize UI") });
        _allPaletteItems.Add(new PaletteItem { Title = "Advanced / Caution", Subtitle = "Deep optimizations and power user tweaks", Category = "Category", ActionToExecute = () => NavigateCategory("Advanced") });

        // Quick Actions
        _allPaletteItems.Add(new PaletteItem { Title = "Undo All WinVelo Changes", Subtitle = "Revert all active changes recorded in the journal", Category = "Action", ActionToExecute = () => _ = HistoryVM.UndoAllChangesAsync() });
        _allPaletteItems.Add(new PaletteItem { Title = "Standard Preset", Subtitle = "Select recommended desktop tweaks", Category = "Action", ActionToExecute = () => TweaksVM.ApplyPreset("Standard") });
        _allPaletteItems.Add(new PaletteItem { Title = "Minimal Preset", Subtitle = "Select essential telemetry reductions", Category = "Action", ActionToExecute = () => TweaksVM.ApplyPreset("Minimal") });
        _allPaletteItems.Add(new PaletteItem { Title = "Clear Selection", Subtitle = "Deselect all tweaks", Category = "Action", ActionToExecute = () => TweaksVM.ClearSelection() });
        _allPaletteItems.Add(new PaletteItem { Title = "Create Restore Point", Subtitle = "Create a Windows System Restore point", Category = "Action", ActionToExecute = () => _ = TweaksVM.CreateRestorePointAsync() });
        _allPaletteItems.Add(new PaletteItem { Title = "Re-Scan System", Subtitle = "Detect currently active tweaks on this PC", Category = "Action", ActionToExecute = () => _ = TweaksVM.RescanSystemAsync() });

        FilterPalette();
    }

    partial void OnPaletteQueryChanged(string value) => FilterPalette();

    private void FilterPalette()
    {
        PaletteResults.Clear();
        string q = PaletteQuery?.Trim() ?? string.Empty;

        foreach (var item in _allPaletteItems)
        {
            if (string.IsNullOrWhiteSpace(q) ||
                item.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                item.Subtitle.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                item.Category.Contains(q, StringComparison.OrdinalIgnoreCase))
            {
                PaletteResults.Add(item);
            }
        }
    }

    [RelayCommand]
    public void ExecutePaletteItem(PaletteItem item)
    {
        IsCommandPaletteOpen = false;
        item.ActionToExecute?.Invoke();
    }

    [RelayCommand]
    public void ExecuteFirstPaletteItem()
    {
        if (PaletteResults.Count > 0)
        {
            ExecutePaletteItem(PaletteResults[0]);
        }
    }

    public async Task CheckUnfinishedSessionsOnStartupAsync()
    {
        if (_tweakService == null) return;

        var unfinished = _tweakService.GetUnfinishedSessions();
        if (unfinished.Count > 0)
        {
            _unfinishedSessions = unfinished.ToList();
            var touchedTweaks = _unfinishedSessions
                .SelectMany(s => s.Records)
                .Select(r => r.TweakId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct()
                .ToList();
            int actionCount = _unfinishedSessions.Sum(s => s.Records.Count);
            string tweakNames = string.Join(", ", touchedTweaks);

            RecoveryDialogMessage = $"A previous session did not finish. {actionCount} actions were applied to [{tweakNames}].";
            IsRecoveryDialogOpen = true;
        }
    }

    [RelayCommand]
    public async Task RollbackRecoveryAsync()
    {
        if (_tweakService != null)
        {
            await _tweakService.RollbackAllUnfinishedSessionsAsync();
            if (TweaksVM != null)
            {
                await TweaksVM.RescanSystemAsync();
            }
        }
        IsRecoveryDialogOpen = false;
        _unfinishedSessions.Clear();
    }

    [RelayCommand]
    public void KeepRecovery()
    {
        if (_tweakService != null)
        {
            _tweakService.KeepAndCompleteAllUnfinishedSessions();
        }
        IsRecoveryDialogOpen = false;
        _unfinishedSessions.Clear();
    }

    private void OnWizardCompleted(string mode)
    {
        IsWizardOpen = false;
        if (string.Equals(mode, "Simple", StringComparison.OrdinalIgnoreCase))
        {
            TweaksVM.IsRecommendedOnly = true;
            TweaksVM.ShowExperimentalTweaks = false;
        }
        else
        {
            TweaksVM.IsRecommendedOnly = false;
        }
        _ = TweaksVM.RefreshActiveStatesAsync();
    }

    [RelayCommand]
    public void OpenFirstRunWizard()
    {
        WizardVM.Reset();
        IsWizardOpen = true;
    }
}

file sealed class NullRestoreService : ISystemRestoreService
{
    public bool IsSystemRestoreEnabled() => false;
    public bool EnableSystemRestore(string drive = "C:\\") => false;
    public int GetRestorePointCount() => 0;
    public DateTime? GetLatestRestorePointDate() => null;
    public RestorePointResult CreateRestorePoint(string description) => new() { Success = true };
    public IReadOnlyList<RestorePointItem> GetRestorePoints() => Array.Empty<RestorePointItem>();
}

