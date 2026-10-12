using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using Tweakora.App.Localization;
using Tweakora.App.Services;
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
        SystemSummary = Tweakora.Modules.Dashboard.SystemInfoProvider.GetSystemSummary();
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
    [ObservableProperty]
    private string _status = "Presets Ready";
}

public sealed partial class SettingsViewModel : ViewModelBase
{
    public LocalizationManager Localization => LocalizationManager.Instance;

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

public sealed partial class MainViewModel : ViewModelBase
{
    [ObservableProperty]
    private object _currentPage;

    [ObservableProperty]
    private bool _isLogPanelOpen;

    public LocalizationManager Localization => LocalizationManager.Instance;
    public UiLogSink LogSink => UiLogSink.Instance;

    public DashboardViewModel DashboardVM { get; }
    public TweaksViewModel TweaksVM { get; }
    public ToolboxViewModel ToolboxVM { get; }
    public AppsViewModel AppsVM { get; }
    public PresetsViewModel PresetsVM { get; }
    public SettingsViewModel SettingsVM { get; }

    public MainViewModel(
        DashboardViewModel dashboardVM,
        TweaksViewModel tweaksVM,
        ToolboxViewModel toolboxVM,
        AppsViewModel appsVM,
        PresetsViewModel presetsVM,
        SettingsViewModel settingsVM)
    {
        DashboardVM = dashboardVM;
        TweaksVM = tweaksVM;
        ToolboxVM = toolboxVM;
        AppsVM = appsVM;
        PresetsVM = presetsVM;
        SettingsVM = settingsVM;

        _currentPage = DashboardVM;
    }

    [RelayCommand]
    private void Navigate(string pageKey)
    {
        CurrentPage = pageKey switch
        {
            "Dashboard" => DashboardVM,
            "Tweaks" => TweaksVM,
            "Toolbox" => ToolboxVM,
            "Apps" => AppsVM,
            "Presets" => PresetsVM,
            "Settings" => SettingsVM,
            _ => DashboardVM
        };
        Serilog.Log.Debug("Navigated to page: {Page}", pageKey);
    }

    [RelayCommand]
    private void ToggleLogPanel()
    {
        IsLogPanelOpen = !IsLogPanelOpen;
    }

    [RelayCommand]
    private void ToggleLanguage()
    {
        string newLang = Localization.CurrentLanguage == "en" ? "ar" : "en";
        Localization.SetLanguage(newLang);
        Serilog.Log.Information("Toggled language to: {Language}", newLang);
    }

    [RelayCommand]
    private void ToggleTheme()
    {
        ApplicationTheme current = ApplicationThemeManager.GetAppTheme();
        ApplicationTheme next = current == ApplicationTheme.Dark ? ApplicationTheme.Light : ApplicationTheme.Dark;
        ApplicationThemeManager.Apply(next);
        Serilog.Log.Information("Toggled theme to: {Theme}", next);
    }
}
