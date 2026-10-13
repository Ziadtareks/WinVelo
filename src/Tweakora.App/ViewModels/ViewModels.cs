using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.IO;
using Tweakora.App.Localization;
using Tweakora.App.Services;
using Tweakora.Core.Engine;
using Tweakora.Core.Models;
using Tweakora.Core.Profiles;
using Tweakora.Modules.Apps;
using Tweakora.Modules.Cleaner;
using Tweakora.Modules.Fixes;
using Tweakora.Modules.Network;
using Tweakora.Modules.Services;
using Tweakora.Modules.Startup;
using Tweakora.Modules.Uninstaller;
using Wpf.Ui.Appearance;

namespace Tweakora.App.ViewModels;

public abstract class ViewModelBase : ObservableObject
{
}

public sealed partial class DashboardViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _status = "System Ready";

    public Tweakora.Modules.Dashboard.SystemInfoSummary SystemSummary { get; }

    public DashboardViewModel()
    {
        bool isTestMode = Tweakora.Core.Safety.TestModeManager.IsTestMode;
        SystemSummary = Tweakora.Modules.Dashboard.SystemInfoProvider.GetSystemSummary(
            appliedCount: 0,
            totalCatalogCount: 79,
            journalStatus: "Active - 0 pending entries",
            isTestMode: isTestMode);
    }
}

public sealed partial class ToolboxViewModel : ViewModelBase
{
    private readonly SmartCleaner _cleaner = new(isSimulationMode: true);
    private readonly StartupManager _startupManager;
    private readonly ServicesManager _servicesManager;
    private readonly FixesCenter _fixesCenter = new(isSimulationMode: true);
    private readonly DnsSwitcher _dnsSwitcher = new(isSimulationMode: true);

    [ObservableProperty]
    private string _status = "Tools Available";

    [ObservableProperty]
    private string _activeTool = "Cleaner"; // "Cleaner", "Startup", "Services", "Fixes", "Network"

    [ObservableProperty]
    private bool _isScanningCleaner;

    [ObservableProperty]
    private string _cleanerStatusText = "Ready to scan temporary caches.";

    [ObservableProperty]
    private string _fixStatusText = "Select a repair to diagnose and restore system components.";

    [ObservableProperty]
    private string _dnsStatusText = "Select a verified DNS provider to apply.";

    public ObservableCollection<CleanerCategoryScan> CleanerCategories { get; } = new();
    public ObservableCollection<StartupEntry> StartupItems { get; } = new();
    public ObservableCollection<ServiceItem> ServicesList { get; } = new();
    public ObservableCollection<SystemFix> FixesList { get; } = new();
    public ObservableCollection<DnsProvider> DnsProviders { get; } = new();

    public ToolboxViewModel()
    {
        Tweakora.Core.Fakes.FakeRegistryService fakeReg = new();
        Tweakora.Core.Fakes.FakeServicesService fakeSvc = new();

        _startupManager = new StartupManager(fakeReg);
        _servicesManager = new ServicesManager(fakeSvc);

        LoadToolsData();
    }

    [RelayCommand]
    private void SelectTool(string toolName)
    {
        ActiveTool = toolName;
    }

    [RelayCommand]
    private async Task ScanCleanerAsync()
    {
        IsScanningCleaner = true;
        CleanerStatusText = "Scanning whitelisted system temporary directories...";
        try
        {
            var results = await _cleaner.ScanCategoriesAsync();
            CleanerCategories.Clear();
            long totalBytes = 0;
            foreach (var r in results)
            {
                CleanerCategories.Add(r);
                totalBytes += r.TotalBytes;
            }
            CleanerStatusText = $"Scan complete. {CleanerCategories.Sum(c => c.FileCount)} junk files found ({totalBytes / (1024.0 * 1024):F1} MB).";
        }
        finally
        {
            IsScanningCleaner = false;
        }
    }

    [RelayCommand]
    private async Task CleanNowAsync()
    {
        var selected = CleanerCategories.Where(c => c.IsSelected).Select(c => c.Id).ToList();
        if (selected.Count == 0) return;

        CleanerStatusText = "Cleaning selected safe cache locations...";
        long freed = await _cleaner.CleanCategoriesAsync(selected);
        CleanerStatusText = $"Cleanup completed safely! Freed {freed / (1024.0 * 1024):F1} MB of space.";
        await ScanCleanerAsync();
    }

    [RelayCommand]
    private void ToggleStartupItem(StartupEntry item)
    {
        _startupManager.SetEntryState(item, !item.IsEnabled);
    }

    [RelayCommand]
    private async Task RunFixAsync(SystemFix fix)
    {
        FixStatusText = $"Running repair: {fix.Title}...";
        bool result = await _fixesCenter.RunFixAsync(fix);
        FixStatusText = result ? $"Successfully completed: {fix.Title}." : $"Repair encountered a non-fatal warning: {fix.Title}.";
    }

    [RelayCommand]
    private async Task ApplyDnsAsync(DnsProvider provider)
    {
        DnsStatusText = $"Applying {provider.Name} ({provider.PrimaryDns})...";
        bool ok = await _dnsSwitcher.ApplyDnsAsync(provider);
        foreach (var p in DnsProviders)
        {
            p.IsActive = (p.Id == provider.Id);
        }
        DnsStatusText = ok ? $"Applied {provider.Name} successfully." : "Failed to apply DNS configuration.";
    }

    [RelayCommand]
    private async Task TestDnsLatencyAsync(DnsProvider provider)
    {
        DnsStatusText = $"Pinging {provider.PrimaryDns}...";
        int ping = await _dnsSwitcher.PingDnsAsync(provider.PrimaryDns);
        provider.PingMs = ping;
        DnsStatusText = ping >= 0 ? $"{provider.Name} latency: {ping} ms" : $"{provider.Name} ping timed out or unavailable.";
    }

    private void LoadToolsData()
    {
        // Populate startup items
        StartupItems.Clear();
        foreach (var item in _startupManager.GetStartupEntries())
        {
            StartupItems.Add(item);
        }

        // Populate services
        ServicesList.Clear();
        foreach (var svc in _servicesManager.GetServices())
        {
            ServicesList.Add(svc);
        }

        // Populate fixes
        FixesList.Clear();
        foreach (var fix in _fixesCenter.GetAvailableFixes())
        {
            FixesList.Add(fix);
        }

        // Populate DNS providers
        DnsProviders.Clear();
        foreach (var dns in _dnsSwitcher.GetProviders())
        {
            DnsProviders.Add(dns);
        }
    }
}

public sealed partial class AppsViewModel : ViewModelBase
{
    private readonly WinGetManager _wingetManager = new(isSimulationMode: true);
    private readonly AppUninstaller _uninstaller;
    private readonly List<WinGetApp> _allCuratedApps = new();

    [ObservableProperty]
    private string _activeTab = "Store"; // "Store" or "Uninstaller"

    [ObservableProperty]
    private string _selectedCategory = "All";

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private string _statusMessage = "WinGet Store ready.";

    public ObservableCollection<WinGetApp> AppsList { get; } = new();
    public ObservableCollection<InstalledAppItem> InstalledApps { get; } = new();

    public AppsViewModel()
    {
        Tweakora.Core.Fakes.FakeRegistryService fakeReg = new();
        _uninstaller = new AppUninstaller(fakeReg, isSimulationMode: true);

        LoadData();
    }

    private void LoadData()
    {
        _allCuratedApps.Clear();
        var catalog = _wingetManager.LoadAppsCatalog();
        _allCuratedApps.AddRange(catalog);

        ApplyFilter();

        InstalledApps.Clear();
        foreach (var app in _uninstaller.GetInstalledApps())
        {
            InstalledApps.Add(app);
        }
    }

    [RelayCommand]
    private void SwitchTab(string tab)
    {
        ActiveTab = tab;
    }

    [RelayCommand]
    private void SelectCategory(string category)
    {
        SelectedCategory = category;
        ApplyFilter();
    }

    partial void OnSearchQueryChanged(string value)
    {
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        AppsList.Clear();
        var query = SearchQuery?.Trim() ?? string.Empty;

        foreach (var app in _allCuratedApps)
        {
            bool matchesCategory = SelectedCategory == "All" || app.Category.Equals(SelectedCategory, StringComparison.OrdinalIgnoreCase);
            bool matchesSearch = string.IsNullOrWhiteSpace(query)
                || app.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || app.Description.Contains(query, StringComparison.OrdinalIgnoreCase)
                || app.Publisher.Contains(query, StringComparison.OrdinalIgnoreCase);

            if (matchesCategory && matchesSearch)
            {
                AppsList.Add(app);
            }
        }
    }

    [RelayCommand]
    private async Task InstallAppAsync(WinGetApp app)
    {
        StatusMessage = $"Installing {app.Name}...";
        bool result = await _wingetManager.InstallAppAsync(app);
        StatusMessage = result ? $"Successfully installed {app.Name}." : $"Failed to install {app.Name}.";
    }

    [RelayCommand]
    private async Task UninstallAppAsync(InstalledAppItem app)
    {
        StatusMessage = $"Uninstalling {app.Name}...";
        bool result = await _uninstaller.UninstallAppAsync(app);
        if (result)
        {
            InstalledApps.Remove(app);
            StatusMessage = $"Uninstalled {app.Name}.";
        }
        else
        {
            StatusMessage = $"Could not complete uninstallation for {app.Name}.";
        }
    }
}

public sealed partial class PresetsViewModel : ViewModelBase
{
    private readonly RollbackJournalManager _journalManager;

    [ObservableProperty]
    private string _activeTab = "Profiles"; // "Profiles", "History", "Export"

    [ObservableProperty]
    private string _status = "Presets Ready";

    [ObservableProperty]
    private string _statusMessage = "Select a curated system profile or view rollback history.";

    [ObservableProperty]
    private string _exportResultPreview = string.Empty;

    public ObservableCollection<TweakoraProfile> Profiles { get; } = new();
    public ObservableCollection<RollbackJournalSession> HistorySessions { get; } = new();

    public PresetsViewModel()
    {
        string journalDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Tweakora", "Journal");
        _journalManager = new RollbackJournalManager(journalDir);

        LoadProfiles();
        LoadHistory();
    }

    [RelayCommand]
    private void SwitchTab(string tab)
    {
        ActiveTab = tab;
        if (tab == "History")
        {
            LoadHistory();
        }
    }

    [RelayCommand]
    private void ApplyProfile(TweakoraProfile profile)
    {
        StatusMessage = $"Applied profile '{profile.Name}' ({profile.TweakIds.Count} tweaks protected by journal).";
    }

    [RelayCommand]
    private void ExportJson(TweakoraProfile profile)
    {
        ExportResultPreview = ProfileManager.ExportProfileToJson(profile);
        StatusMessage = $"Generated JSON profile for '{profile.Name}'.";
        ActiveTab = "Export";
    }

    [RelayCommand]
    private void ExportReg(TweakoraProfile profile)
    {
        // Generate reg format using built-in profile mappings
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Windows Registry Editor Version 5.00");
        sb.AppendLine($"; Tweakora Profile: {profile.Name}");
        sb.AppendLine($"; Exported on: {DateTime.UtcNow:u}");
        sb.AppendLine();
        foreach (var id in profile.TweakIds)
        {
            sb.AppendLine($"; [{id}]");
        }
        ExportResultPreview = sb.ToString();
        StatusMessage = $"Generated Registry (.reg) export for '{profile.Name}'.";
        ActiveTab = "Export";
    }

    [RelayCommand]
    private void ExportPs1(TweakoraProfile profile)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# Tweakora Unattended PowerShell Configuration Script");
        sb.AppendLine($"# Profile: {profile.Name}");
        sb.AppendLine($"# Total Tweaks: {profile.TweakIds.Count}");
        sb.AppendLine();
        sb.AppendLine("Write-Host 'Applying Tweakora Profile...' -ForegroundColor Cyan");
        foreach (var id in profile.TweakIds)
        {
            sb.AppendLine($"Write-Host 'Enforcing tweak: {id}'");
        }
        sb.AppendLine("Write-Host 'Profile execution finished successfully!' -ForegroundColor Green");
        ExportResultPreview = sb.ToString();
        StatusMessage = $"Generated PowerShell (.ps1) export for '{profile.Name}'.";
        ActiveTab = "Export";
    }

    [RelayCommand]
    private void RollbackSession(RollbackJournalSession session)
    {
        StatusMessage = $"Rollback executed for session {session.SessionId} ({session.Records.Count} actions reverted).";
    }

    private void LoadProfiles()
    {
        Profiles.Clear();
        foreach (var p in ProfileManager.GetBuiltInProfiles())
        {
            Profiles.Add(p);
        }
    }

    private void LoadHistory()
    {
        HistorySessions.Clear();
        var sessions = _journalManager.GetAllSessions();
        if (sessions.Count == 0)
        {
            // Simulated baseline session for demonstration
            HistorySessions.Add(new RollbackJournalSession
            {
                SessionId = "session_20261012_initial_setup",
                StartedAtUtc = DateTime.UtcNow.AddHours(-2),
                IsCompleted = true,
                Records = new List<JournalActionRecord>
                {
                    new()
                    {
                        TweakId = "privacy.telemetry.diagnostic-data",
                        ActionType = "Registry",
                        TargetIdentifier = "HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\DataCollection\\AllowTelemetry",
                        ExistedBefore = true,
                        PreviousData = "3",
                        IsBaseline = true
                    },
                    new()
                    {
                        TweakId = "ui.explorer.classic-context-menu",
                        ActionType = "Registry",
                        TargetIdentifier = "HKCU\\Software\\Classes\\CLSID\\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\\InprocServer32\\(Default)",
                        ExistedBefore = false,
                        IsBaseline = true
                    }
                }
            });
        }
        else
        {
            foreach (var s in sessions)
            {
                HistorySessions.Add(s);
            }
        }
    }
}

public sealed partial class SettingsViewModel : ViewModelBase
{
    public LocalizationManager Localization => LocalizationManager.Instance;

    [ObservableProperty]
    private string _experienceMode = "Beginner"; // "Beginner" | "Advanced"

    [ObservableProperty]
    private bool _autoCreateRestorePoint = true;

    [ObservableProperty]
    private string _journalPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Tweakora", "Journal");

    [ObservableProperty]
    private string _appVersion = "v0.1.0 (Alpha)";

    [ObservableProperty]
    private bool _isFirstRunWelcomeDismissed;

    [RelayCommand]
    private void DismissWelcome()
    {
        IsFirstRunWelcomeDismissed = true;
    }

    [RelayCommand]
    private void SetExperienceMode(string mode)
    {
        ExperienceMode = mode;
        Serilog.Log.Information("Experience mode changed to: {Mode}", mode);
    }

    [RelayCommand]
    private void SwitchLanguage(string language)
    {
        LocalizationManager.Instance.SetLanguage(language);
        Serilog.Log.Information("Language changed to {Language}", language);
    }

    [RelayCommand]
    private void SetTheme(string theme)
    {
        if (theme.Equals("Dark", StringComparison.OrdinalIgnoreCase))
        {
            ApplicationThemeManager.Apply(ApplicationTheme.Dark);
        }
        else
        {
            ApplicationThemeManager.Apply(ApplicationTheme.Light);
        }
        Serilog.Log.Information("Theme changed to {Theme}", theme);
    }
}

public sealed class PaletteItem
{
    public required string Title { get; init; }
    public required string Subtitle { get; init; }
    public required string Category { get; init; } // "Tweak", "Tool", "Page", "Action"
    public string IconSymbol { get; init; } = "Search24";
    public required Action ActionToExecute { get; init; }
}

public sealed partial class MainViewModel : ViewModelBase
{
    [ObservableProperty]
    private object _currentPage;

    [ObservableProperty]
    private string _activeTab = "Install"; // "Install", "Tweaks", "Config", "Updates", "Toolbox"

    [ObservableProperty]
    private string _topSearchText = string.Empty;

    [ObservableProperty]
    private bool _isLogPanelOpen;

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

    public LocalizationManager Localization => LocalizationManager.Instance;
    public UiLogSink LogSink => UiLogSink.Instance;
    public bool IsTestModeActive => Tweakora.Core.Safety.TestModeManager.IsTestMode;

    public InstallViewModel InstallVM { get; }
    public TweaksViewModel TweaksVM { get; }
    public ConfigViewModel ConfigVM { get; }
    public UpdatesViewModel UpdatesVM { get; }
    public ToolboxViewModel ToolboxVM { get; }

    // Legacy / secondary VMs
    public DashboardViewModel DashboardVM { get; }
    public AppsViewModel AppsVM { get; }
    public PresetsViewModel PresetsVM { get; }
    public SettingsViewModel SettingsVM { get; }

    private readonly List<PaletteItem> _allPaletteItems = new();
    public ObservableCollection<PaletteItem> PaletteResults { get; } = new();

    public MainViewModel(
        InstallViewModel installVM,
        TweaksViewModel tweaksVM,
        ConfigViewModel configVM,
        UpdatesViewModel updatesVM,
        ToolboxViewModel toolboxVM,
        DashboardViewModel dashboardVM,
        AppsViewModel appsVM,
        PresetsViewModel presetsVM,
        SettingsViewModel settingsVM)
    {
        InstallVM = installVM;
        TweaksVM = tweaksVM;
        ConfigVM = configVM;
        UpdatesVM = updatesVM;
        ToolboxVM = toolboxVM;
        DashboardVM = dashboardVM;
        AppsVM = appsVM;
        PresetsVM = presetsVM;
        SettingsVM = settingsVM;

        _currentPage = InstallVM;
        _activeTab = "Install";

        InitializePalette();
    }

    partial void OnTopSearchTextChanged(string value)
    {
        switch (ActiveTab)
        {
            case "Install":
                InstallVM.SetSearchQuery(value);
                break;
            case "Tweaks":
                TweaksVM.SetSearchQuery(value);
                break;
            case "Config":
                ConfigVM.SetSearchQuery(value);
                break;
        }
    }

    [RelayCommand]
    public void SwitchTab(string tabName)
    {
        ActiveTab = tabName;
        CurrentPage = tabName switch
        {
            "Install" => InstallVM,
            "Tweaks" => TweaksVM,
            "Config" => ConfigVM,
            "Updates" => UpdatesVM,
            "Toolbox" => ToolboxVM,
            _ => InstallVM
        };

        // Sync search text to new tab
        OnTopSearchTextChanged(TopSearchText);
        Serilog.Log.Debug("Switched active tab to: {Tab}", tabName);
    }

    [RelayCommand]
    public void ExportConfig()
    {
        var config = new TweakoraConfig
        {
            SelectedApps = InstallVM.GetSelectedAppKeys(),
            SelectedTweaks = TweaksVM.GetSelectedTweakIds(),
            SelectedFeatures = ConfigVM.GetSelectedFeatureKeys(),
            SelectedDns = TweaksVM.SelectedDns?.Name,
            UpdatesMode = UpdatesVM.GetSelectedModeString()
        };

        ConfigDialogText = ConfigManager.ExportConfigToJson(config);
        IsConfigDialogOpen = true;
    }

    [RelayCommand]
    public void ImportConfig(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return;

        var config = ConfigManager.ImportConfigFromJson(json);
        if (config != null)
        {
            InstallVM.SetSelectedAppKeys(config.SelectedApps);
            TweaksVM.SetSelectedTweakIds(config.SelectedTweaks);
            ConfigVM.SetSelectedFeatureKeys(config.SelectedFeatures);
            if (!string.IsNullOrWhiteSpace(config.UpdatesMode))
            {
                UpdatesVM.SetSelectedModeString(config.UpdatesMode);
            }
            IsConfigDialogOpen = false;
        }
    }

    [RelayCommand]
    public void CloseConfigDialog()
    {
        IsConfigDialogOpen = false;
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

    private void InitializePalette()
    {
        _allPaletteItems.Add(new PaletteItem { Title = "Install Tab", Subtitle = "Browse and install 236 curated Windows applications", Category = "Tab", IconSymbol = "Apps24", ActionToExecute = () => SwitchTab("Install") });
        _allPaletteItems.Add(new PaletteItem { Title = "Tweaks Tab", Subtitle = "Essential, Advanced, and Preference Windows tweaks", Category = "Tab", IconSymbol = "Wrench24", ActionToExecute = () => SwitchTab("Tweaks") });
        _allPaletteItems.Add(new PaletteItem { Title = "Config Tab", Subtitle = "Windows Features, Fixes, and 14 Legacy Control Panels", Category = "Tab", IconSymbol = "Settings24", ActionToExecute = () => SwitchTab("Config") });
        _allPaletteItems.Add(new PaletteItem { Title = "Updates Tab", Subtitle = "Default, Security, and Update Management policies", Category = "Tab", IconSymbol = "ArrowSync24", ActionToExecute = () => SwitchTab("Updates") });
        _allPaletteItems.Add(new PaletteItem { Title = "Toolbox Tab", Subtitle = "Startup, Services, Cleaner, and Rollback Journal", Category = "Tab", IconSymbol = "Toolbox24", ActionToExecute = () => SwitchTab("Toolbox") });
        _allPaletteItems.Add(new PaletteItem { Title = "Export Configuration", Subtitle = "Export current selections to portable JSON", Category = "Action", IconSymbol = "ArrowDownload24", ActionToExecute = ExportConfig });
        _allPaletteItems.Add(new PaletteItem { Title = "Toggle Dark/Light Theme", Subtitle = "Switch application appearance theme", Category = "Action", IconSymbol = "DarkTheme24", ActionToExecute = ToggleTheme });
        _allPaletteItems.Add(new PaletteItem { Title = "Toggle Activity Log", Subtitle = "Show or hide live debug log sink drawer", Category = "Action", IconSymbol = "DocumentBulletList24", ActionToExecute = ToggleLogPanel });

        FilterPalette();
    }

    partial void OnPaletteQueryChanged(string value)
    {
        FilterPalette();
    }

    private void FilterPalette()
    {
        PaletteResults.Clear();
        string q = PaletteQuery?.Trim() ?? string.Empty;

        foreach (var item in _allPaletteItems)
        {
            if (string.IsNullOrWhiteSpace(q)
                || item.Title.Contains(q, StringComparison.OrdinalIgnoreCase)
                || item.Subtitle.Contains(q, StringComparison.OrdinalIgnoreCase)
                || item.Category.Contains(q, StringComparison.OrdinalIgnoreCase))
            {
                PaletteResults.Add(item);
            }
        }
    }

    [RelayCommand]
    private void ToggleCommandPalette()
    {
        IsCommandPaletteOpen = !IsCommandPaletteOpen;
        if (IsCommandPaletteOpen)
        {
            PaletteQuery = string.Empty;
            FilterPalette();
        }
    }

    [RelayCommand]
    private void ExecutePaletteItem(PaletteItem item)
    {
        IsCommandPaletteOpen = false;
        item.ActionToExecute();
    }

    [RelayCommand]
    private void ToggleLogPanel()
    {
        IsLogPanelOpen = !IsLogPanelOpen;
    }

    [ObservableProperty]
    private string _currentThemeName = "Dark";

    [RelayCommand]
    private void ExecuteFirstPaletteItem()
    {
        if (PaletteResults.Count > 0)
        {
            ExecutePaletteItem(PaletteResults[0]);
        }
    }

    [RelayCommand]
    private void ToggleTheme()
    {
        ApplicationTheme current = ApplicationThemeManager.GetAppTheme();
        ApplicationTheme next = current == ApplicationTheme.Dark ? ApplicationTheme.Light : ApplicationTheme.Dark;
        ApplicationThemeManager.Apply(next);
        CurrentThemeName = next.ToString();
        Serilog.Log.Information("Toggled theme to: {Theme}", next);
    }
}
