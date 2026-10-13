using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tweakora.Core.Operations;
using Tweakora.Modules.Cleaner;
using Tweakora.Modules.Config;
using Tweakora.Modules.Network;
using Tweakora.Modules.Services;
using Tweakora.Modules.Startup;

namespace Tweakora.App.ViewModels;

public sealed partial class ToolboxViewModel : ObservableObject
{
    private readonly ICleanerService _cleanerService;
    private readonly IStartupService _startupService;
    private readonly IServicesManagerService _servicesService;
    private readonly IFixService _fixService;
    private readonly IDnsService _dnsService;
    private readonly IOperationRunner _runner;

    [ObservableProperty]
    private string _status = "Tools Available";

    [ObservableProperty]
    private string _activeTool = "Cleaner"; // "Cleaner", "Startup", "Services", "Fixes", "Network"

    [ObservableProperty]
    private bool _isScanningCleaner;

    [ObservableProperty]
    private bool _isCleaning;

    [ObservableProperty]
    private string _cleanerStatusText = "Ready to scan temporary caches.";

    [ObservableProperty]
    private string _fixStatusText = "Select a repair to diagnose and restore system components.";

    [ObservableProperty]
    private string _dnsStatusText = "Select a verified DNS provider to apply.";

    public ObservableCollection<CleanerCategoryScan> CleanerCategories { get; } = new();
    public ObservableCollection<StartupProgramItem> StartupItems { get; } = new();
    public ObservableCollection<WindowsServiceInfo> ServicesList { get; } = new();
    public ObservableCollection<SystemFixDefinition> FixesList { get; } = new();
    public ObservableCollection<DnsProviderDefinition> DnsProviders { get; } = new();

    public ToolboxViewModel(
        ICleanerService cleanerService,
        IStartupService startupService,
        IServicesManagerService servicesService,
        IFixService fixService,
        IDnsService dnsService,
        IOperationRunner runner)
    {
        _cleanerService = cleanerService;
        _startupService = startupService;
        _servicesService = servicesService;
        _fixService = fixService;
        _dnsService = dnsService;
        _runner = runner;

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
            var results = await _cleanerService.ScanCategoriesAsync();
            CleanerCategories.Clear();
            long totalBytes = 0;
            foreach (var r in results)
            {
                CleanerCategories.Add(r);
                totalBytes += r.TotalBytes;
            }
            CleanerStatusText = $"Scan complete. {CleanerCategories.Sum(c => c.FileCount)} junk files found ({totalBytes / (1024.0 * 1024):F1} MB).";
        }
        catch (Exception ex)
        {
            CleanerStatusText = $"Scan error: {ex.Message}";
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
        if (selected.Count == 0)
        {
            CleanerStatusText = "No categories selected to clean.";
            return;
        }

        IsCleaning = true;
        CleanerStatusText = "Cleaning selected safe cache locations...";
        try
        {
            var result = await _cleanerService.CleanCategoriesAsync(selected);
            CleanerStatusText = $"Cleanup completed! {result.FormattedSummary}.";
            await ScanCleanerAsync();
        }
        catch (Exception ex)
        {
            CleanerStatusText = $"Cleanup error: {ex.Message}";
        }
        finally
        {
            IsCleaning = false;
        }
    }

    [RelayCommand]
    private async Task ToggleStartupItemAsync(StartupProgramItem item)
    {
        bool targetState = item.IsEnabled;
        bool ok = await _startupService.SetEntryStateAsync(item, targetState);
        if (!ok)
        {
            // Revert UI toggle if state change failed
            item.IsEnabled = !targetState;
        }
    }

    [RelayCommand]
    private async Task RunFixAsync(SystemFixDefinition fix)
    {
        FixStatusText = $"Running repair: {fix.Title}...";
        var report = await _fixService.RunFixAsync(fix.Id, _runner);
        FixStatusText = report.FailedCount == 0
            ? $"Successfully completed: {fix.Title}."
            : $"Repair finished with notes: {report.Summary}.";
    }

    [RelayCommand]
    private async Task ApplyDnsAsync(DnsProviderDefinition provider)
    {
        DnsStatusText = $"Applying {provider.Name} ({provider.PrimaryDns})...";
        bool ok = await _dnsService.ApplyDnsAsync(provider);
        if (ok)
        {
            foreach (var p in DnsProviders)
            {
                p.IsActive = (p.Id == provider.Id);
            }
            DnsStatusText = $"Applied {provider.Name} successfully.";
        }
        else
        {
            DnsStatusText = $"Failed to apply {provider.Name}.";
        }
    }

    [RelayCommand]
    private async Task TestDnsLatencyAsync(DnsProviderDefinition provider)
    {
        DnsStatusText = $"Pinging {provider.PrimaryDns}...";
        int ping = await _dnsService.PingDnsAsync(provider.PrimaryDns);
        provider.PingMs = ping;
        DnsStatusText = ping >= 0 ? $"{provider.Name} latency: {ping} ms" : $"{provider.Name} ping timed out or unavailable.";
    }

    public async void LoadToolsData()
    {
        // Populate startup items
        StartupItems.Clear();
        var startupEntries = await _startupService.GetStartupEntriesAsync();
        foreach (var item in startupEntries)
        {
            StartupItems.Add(item);
        }

        // Populate services
        ServicesList.Clear();
        foreach (var svc in _servicesService.GetServices())
        {
            ServicesList.Add(svc);
        }

        // Populate fixes
        FixesList.Clear();
        foreach (var fix in _fixService.GetAvailableFixes())
        {
            FixesList.Add(fix);
        }

        // Populate DNS providers
        DnsProviders.Clear();
        foreach (var dns in _dnsService.GetProviders())
        {
            DnsProviders.Add(dns);
        }
    }
}
