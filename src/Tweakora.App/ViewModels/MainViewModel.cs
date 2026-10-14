using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using Tweakora.App.Localization;
using Tweakora.App.Services;
using Tweakora.Core.Operations;
using Tweakora.Core.Safety;
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
    public BackupsViewModel BackupsVM { get; }

    private readonly List<PaletteItem> _allPaletteItems = new();
    public ObservableCollection<PaletteItem> PaletteResults { get; } = new();

    private readonly IActivityTracker? _activityTracker;
    private DispatcherTimer? _hudHideTimer;

    public MainViewModel(
        DashboardViewModel dashboardVM,
        InstallViewModel installVM,
        TweaksViewModel tweaksVM,
        ConfigViewModel configVM,
        UpdatesViewModel updatesVM,
        ToolboxViewModel toolboxVM,
        BackupsViewModel backupsVM,
        IActivityTracker? activityTracker = null)
    {
        DashboardVM = dashboardVM;
        InstallVM = installVM;
        TweaksVM = tweaksVM;
        ConfigVM = configVM;
        UpdatesVM = updatesVM;
        ToolboxVM = toolboxVM;
        BackupsVM = backupsVM;
        _activityTracker = activityTracker;

        _currentPage = DashboardVM;
        _activeTab = "Dashboard";

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
        ActiveTab = tabName;
        CurrentPage = tabName switch
        {
            "Dashboard" or "Home" => DashboardVM,
            "Install" => InstallVM,
            "Tweaks" => TweaksVM,
            "Config" => ConfigVM,
            "Updates" => UpdatesVM,
            "Toolbox" => ToolboxVM,
            "Backups" => BackupsVM,
            _ => DashboardVM
        };

        if (!string.IsNullOrEmpty(TopSearchText))
        {
            OnTopSearchTextChanged(TopSearchText);
        }

        if (tabName is "Dashboard" or "Home")
        {
            _ = DashboardVM.RefreshAllToggleStatesAsync();
        }
        else if (tabName == "Tweaks")
        {
            _ = TweaksVM.RefreshActiveStatesAsync();
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
            Version = "1.0.0",
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

        // Tabs
        _allPaletteItems.Add(new PaletteItem { Title = "All Tweaks", Subtitle = "View and apply all system tweaks", Category = "Category", ActionToExecute = () => NavigateCategory("All") });
        _allPaletteItems.Add(new PaletteItem { Title = "Essential Tweaks", Subtitle = "Core safe optimizations", Category = "Category", ActionToExecute = () => NavigateCategory("Essential") });
        _allPaletteItems.Add(new PaletteItem { Title = "Privacy & Telemetry", Subtitle = "Disable telemetry and tracking", Category = "Category", ActionToExecute = () => NavigateCategory("Privacy") });
        _allPaletteItems.Add(new PaletteItem { Title = "Performance Tweaks", Subtitle = "System speed and memory optimizations", Category = "Category", ActionToExecute = () => NavigateCategory("Performance") });
        _allPaletteItems.Add(new PaletteItem { Title = "Customize UI", Subtitle = "Taskbar, Explorer and desktop style", Category = "Category", ActionToExecute = () => NavigateCategory("Customize UI") });
        _allPaletteItems.Add(new PaletteItem { Title = "Advanced / Caution", Subtitle = "Deep optimizations and power user tweaks", Category = "Category", ActionToExecute = () => NavigateCategory("Advanced") });

        // Tools
        _allPaletteItems.Add(new PaletteItem { Title = "Install Apps", Subtitle = "WinGet software package manager", Category = "Tool", ActionToExecute = () => SwitchTab("Install") });
        _allPaletteItems.Add(new PaletteItem { Title = "System Config", Subtitle = "Windows features and system repairs", Category = "Tool", ActionToExecute = () => SwitchTab("Config") });
        _allPaletteItems.Add(new PaletteItem { Title = "Windows Updates", Subtitle = "Manage update modes and policies", Category = "Tool", ActionToExecute = () => SwitchTab("Updates") });
        _allPaletteItems.Add(new PaletteItem { Title = "Toolbox & Cleaner", Subtitle = "Disk cleanup and maintenance tools", Category = "Tool", ActionToExecute = () => SwitchTab("Toolbox") });
        _allPaletteItems.Add(new PaletteItem { Title = "Backups & Restore", Subtitle = "Inspect and restore system snapshots", Category = "Tool", ActionToExecute = () => SwitchTab("Backups") });

        // Actions
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
}
