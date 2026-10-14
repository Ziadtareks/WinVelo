using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tweakora.Core.Operations;
using Tweakora.Modules.Cleaner;
using Tweakora.Modules.Config;
using Tweakora.Modules.Memory;
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
    private readonly IMemoryOptimizerService _memoryOptimizer;
    private readonly INetworkOptimizerService _networkOptimizer;

    [ObservableProperty]
    private string _status = "Tools Available";

    [ObservableProperty]
    private string _activeTool = "Cleaner"; // "Cleaner", "Startup", "Services", "Fixes", "Network"

    [ObservableProperty]
    private bool _isScanningCleaner;

    [ObservableProperty]
    private bool _isCleaning;

    [ObservableProperty]
    private bool _isNagleDisabled;

    [ObservableProperty]
    private bool _isBenchmarkingDns;

    [ObservableProperty]
    private string _cleanerStatusText = "Ready to scan temporary caches.";

    [ObservableProperty]
    private string _fixStatusText = "Select a repair to diagnose and restore system components.";

    [ObservableProperty]
    private string _dnsStatusText = "Select a verified DNS provider to apply.";

    [ObservableProperty]
    private string _networkStatusText = "Network & Gaming Latency Optimizer ready.";

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
        IOperationRunner runner,
        IMemoryOptimizerService memoryOptimizer,
        INetworkOptimizerService networkOptimizer)
    {
        _cleanerService = cleanerService;
        _startupService = startupService;
        _servicesService = servicesService;
        _fixService = fixService;
        _dnsService = dnsService;
        _runner = runner;
        _memoryOptimizer = memoryOptimizer;
        _networkOptimizer = networkOptimizer;

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
        if (!Tweakora.App.Services.AppAdminGuard.EnsureAdmin("clean system temporary files"))
        {
            CleanerStatusText = "Administrator privileges required to clean temporary files.";
            return;
        }

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
        if (!Tweakora.App.Services.AppAdminGuard.EnsureAdmin("modify startup programs"))
        {
            item.IsEnabled = !item.IsEnabled;
            return;
        }

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
        if (!Tweakora.App.Services.AppAdminGuard.EnsureAdmin("run system maintenance fixes"))
        {
            FixStatusText = "Administrator privileges required to run fixes.";
            return;
        }

        FixStatusText = $"Running repair: {fix.Title}...";
        var report = await _fixService.RunFixAsync(fix.Id, _runner);
        FixStatusText = report.FailedCount == 0
            ? $"Successfully completed: {fix.Title}."
            : $"Repair finished with notes: {report.Summary}.";
    }

    [RelayCommand]
    private async Task ApplyDnsAsync(DnsProviderDefinition provider)
    {
        if (!Tweakora.App.Services.AppAdminGuard.EnsureAdmin("change DNS settings"))
        {
            DnsStatusText = "Administrator privileges required to change DNS settings.";
            return;
        }

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

    [RelayCommand]
    private async Task PurgeRamAsync()
    {
        CleanerStatusText = "Purging standby memory and trimming working sets...";
        try
        {
            var result = await _memoryOptimizer.PurgeMemoryAndStandbyListAsync();
            CleanerStatusText = $"Memory Purge Complete! Freed {result.FormattedFreed} of Standby & Process Cache.";
        }
        catch (Exception ex)
        {
            CleanerStatusText = $"RAM Purge Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task BenchmarkAllDnsAsync()
    {
        IsBenchmarkingDns = true;
        DnsStatusText = "Benchmarking top 5 DNS providers (3 pings each)...";
        try
        {
            var results = await _networkOptimizer.BenchmarkDnsProvidersAsync();
            foreach (var res in results)
            {
                var existing = DnsProviders.FirstOrDefault(p => p.PrimaryDns == res.PrimaryDns);
                if (existing != null)
                {
                    existing.PingMs = res.AveragePingMs;
                }
            }

            var fastest = results.FirstOrDefault(r => r.IsFastest);
            if (fastest != null)
            {
                DnsStatusText = $"Benchmark complete! Fastest DNS: {fastest.Name} ({fastest.AveragePingMs} ms).";
            }
            else
            {
                DnsStatusText = "DNS Benchmark complete.";
            }
        }
        catch (Exception ex)
        {
            DnsStatusText = $"Benchmark error: {ex.Message}";
        }
        finally
        {
            IsBenchmarkingDns = false;
        }
    }

    [RelayCommand]
    private async Task ApplyFastestDnsAsync()
    {
        if (!Tweakora.App.Services.AppAdminGuard.EnsureAdmin("apply DNS settings"))
        {
            DnsStatusText = "Administrator privileges required to change DNS.";
            return;
        }

        var responsive = DnsProviders.Where(p => p.PingMs > 0).ToList();
        if (responsive.Count == 0)
        {
            await BenchmarkAllDnsAsync();
            responsive = DnsProviders.Where(p => p.PingMs > 0).ToList();
        }

        var fastest = responsive.OrderBy(p => p.PingMs).FirstOrDefault();
        if (fastest != null)
        {
            await ApplyDnsAsync(fastest);
        }
        else
        {
            DnsStatusText = "No responsive DNS providers found. Check internet connection.";
        }
    }

    [RelayCommand]
    private async Task ResetDnsToDhcpAsync()
    {
        if (!Tweakora.App.Services.AppAdminGuard.EnsureAdmin("reset DNS settings"))
        {
            DnsStatusText = "Administrator privileges required to change DNS.";
            return;
        }

        DnsStatusText = "Resetting network adapters to Automatic (DHCP) DNS...";
        bool ok = await _networkOptimizer.ResetDnsToDhcpAsync();
        if (ok)
        {
            foreach (var p in DnsProviders)
            {
                p.IsActive = (p.PrimaryDns == "DHCP");
            }
            DnsStatusText = "Successfully reset network adapters to Automatic (DHCP) DNS.";
        }
        else
        {
            DnsStatusText = "Failed to reset DNS. Ensure network adapter is active.";
        }
    }

    [RelayCommand]
    private async Task ToggleNagleAsync(object? parameter)
    {
        if (!Tweakora.App.Services.AppAdminGuard.EnsureAdmin("modify network packet optimization"))
        {
            return;
        }

        bool target = parameter is bool b ? b : !IsNagleDisabled;
        NetworkStatusText = $"{(target ? "Disabling Nagle's Algorithm (Low Latency Gaming)" : "Enabling default TCP packet batching")}...";

        bool ok = await _networkOptimizer.SetNagleAlgorithmStateAsync(target);
        if (ok)
        {
            IsNagleDisabled = await _networkOptimizer.DetectNagleAlgorithmDisabledAsync();
            NetworkStatusText = IsNagleDisabled
                ? "Nagle's Algorithm Disabled: Instant gaming packet transmission active on active NIC(s)."
                : "Standard TCP packet batching restored.";
        }
        else
        {
            NetworkStatusText = "Failed to update network adapter TCP parameters.";
        }
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

        _ = Task.Run(async () =>
        {
            try
            {
                bool nagleDisabled = await _networkOptimizer.DetectNagleAlgorithmDisabledAsync();
                System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                {
                    IsNagleDisabled = nagleDisabled;
                });
            }
            catch { }
        });
    }
}
