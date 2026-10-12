using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tweakora.App.Localization;
using Tweakora.App.Services;
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
    private readonly Tweakora.Modules.Cleaner.SmartCleaner _cleaner = new(isSimulationMode: true);
    private readonly Tweakora.Modules.Startup.StartupManager _startupManager;
    private readonly Tweakora.Modules.Services.ServicesManager _servicesManager;

    [ObservableProperty]
    private string _status = "Tools Available";

    [ObservableProperty]
    private string _activeTool = "Cleaner"; // "Cleaner", "Startup", "Services"

    [ObservableProperty]
    private bool _isScanningCleaner;

    [ObservableProperty]
    private string _cleanerStatusText = "Ready to scan temporary caches.";

    public System.Collections.ObjectModel.ObservableCollection<Tweakora.Modules.Cleaner.CleanerCategoryScan> CleanerCategories { get; } = new();
    public System.Collections.ObjectModel.ObservableCollection<Tweakora.Modules.Startup.StartupEntry> StartupItems { get; } = new();
    public System.Collections.ObjectModel.ObservableCollection<Tweakora.Modules.Services.ServiceItem> ServicesList { get; } = new();

    public ToolboxViewModel()
    {
        Tweakora.Core.Fakes.FakeRegistryService fakeReg = new();
        Tweakora.Core.Fakes.FakeServicesService fakeSvc = new();

        _startupManager = new Tweakora.Modules.Startup.StartupManager(fakeReg);
        _servicesManager = new Tweakora.Modules.Services.ServicesManager(fakeSvc);

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
    private void ToggleStartupItem(Tweakora.Modules.Startup.StartupEntry item)
    {
        _startupManager.SetEntryState(item, !item.IsEnabled);
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
    }
}

public sealed partial class AppsViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _status = "WinGet Ready";
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
