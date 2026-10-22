using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinVelo.Core.Operations;
using WinVelo.Modules.Cleaner;
using WinVelo.Modules.Config;
using WinVelo.Modules.Memory;
using WinVelo.Modules.Network;
using WinVelo.Modules.Services;
using WinVelo.Modules.Startup;
using WinVelo.App.Localization;

namespace WinVelo.App.ViewModels;

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
    private string _status = LocalizationManager.T("Tools Available");

    [ObservableProperty]
    private string _activeTool = "Cleaner"; // "Cleaner", "Startup", "Services", "Fixes", "Network"

    [ObservableProperty]
    private bool _isScanningCleaner;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelCleanCommand))]
    private bool _isCleaning;

    private CancellationTokenSource? _cleanCts;

    private bool CanCancelClean() => IsCleaning;

    [RelayCommand(CanExecute = nameof(CanCancelClean))]
    private void CancelClean()
    {
        _cleanCts?.Cancel();
        CleanerStatusText = LocalizationManager.T("Stopping cleanup…");
    }

    [ObservableProperty]
    private bool _isNagleDisabled;

    [ObservableProperty]
    private bool _isBenchmarkingDns;

    [ObservableProperty]
    private string _cleanerStatusText = LocalizationManager.T("Ready to scan temporary caches.");

    [ObservableProperty]
    private string _fixStatusText = LocalizationManager.T("Select a repair to diagnose and restore system components.");

    [ObservableProperty]
    private string _dnsStatusText = LocalizationManager.T("Select a verified DNS provider to apply.");

    [ObservableProperty]
    private string _networkStatusText = LocalizationManager.T("Network & Gaming Latency Optimizer ready.");

    public ObservableCollection<CleanerCategoryScan> CleanerCategories { get; } = new();
    public ObservableCollection<StartupAppItem> StartupItems { get; } = new();
    public ObservableCollection<WindowsServiceInfo> ServicesList { get; } = new();
    public ObservableCollection<SystemFixDefinition> FixesList { get; } = new();
    public ObservableCollection<DnsProviderDefinition> DnsProviders { get; } = new();
    public ObservableCollection<DnsAdapter> DnsAdapters { get; } = new();
    [ObservableProperty] private DnsAdapter? _selectedDnsAdapter;
    [ObservableProperty] private bool _useDnsOverHttps;

    partial void OnSelectedDnsAdapterChanged(DnsAdapter? value)
    {
        foreach (var provider in DnsProviders) provider.IsActive = false;
        if (value != null) _ = RefreshActiveDnsAsync(value.Index);
    }
    private async Task RefreshActiveDnsAsync(int index)
    {
        try
        {
            string? id = await _dnsService.GetCurrentProviderIdAsync(index);
            if (SelectedDnsAdapter?.Index == index) foreach (var provider in DnsProviders) provider.IsActive = provider.Id == id;
        }
        catch (Exception ex) { DnsStatusText = ex.Message; }
    }

    public async Task RefreshDnsAdaptersAsync()
    {
        var adapters = await _dnsService.GetAdaptersAsync();
        int? selected = SelectedDnsAdapter?.Index;
        DnsAdapters.Clear();
        foreach (var adapter in adapters) DnsAdapters.Add(adapter);
        SelectedDnsAdapter = DnsAdapters.FirstOrDefault(a => a.Index == selected) ?? DnsAdapters.FirstOrDefault(a => a.IsDefault) ?? DnsAdapters.FirstOrDefault();
    }

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
        CleanerStatusText = LocalizationManager.T("Scanning whitelisted system temporary directories...");
        await Task.Yield();

        try
        {
            var results = await Task.Run(async () => await _cleanerService.ScanCategoriesAsync());
            CleanerCategories.Clear();
            long totalBytes = 0;
            foreach (var r in results)
            {
                CleanerCategories.Add(r);
                totalBytes += r.TotalBytes;
            }
            CleanerStatusText = LocalizationManager.T("Scan complete. {0} junk files found ({1:F1} MB).", CleanerCategories.Sum(c => c.FileCount), totalBytes / (1024.0 * 1024));
        }
        catch (Exception ex)
        {
            CleanerStatusText = LocalizationManager.T("Scan error: {0}", ex.Message);
        }
        finally
        {
            IsScanningCleaner = false;
        }
    }

    [RelayCommand]
    private async Task CleanNowAsync()
    {
        if (!WinVelo.App.Services.AppAdminGuard.EnsureAdmin("clean system temporary files"))
        {
            CleanerStatusText = LocalizationManager.T("Administrator privileges required to clean temporary files.");
            return;
        }

        var selected = CleanerCategories.Where(c => c.IsSelected).Select(c => c.Id).ToList();
        if (selected.Count == 0)
        {
            CleanerStatusText = LocalizationManager.T("No categories selected to clean.");
            return;
        }

        IsCleaning = true;
        CleanerStatusText = LocalizationManager.T("Cleaning selected safe cache locations...");
        await Task.Yield();

        _cleanCts?.Dispose();
        _cleanCts = new CancellationTokenSource();
        var token = _cleanCts.Token;
        try
        {
            var result = await Task.Run(async () => await _cleanerService.CleanCategoriesAsync(selected, token));
            CleanerStatusText = LocalizationManager.T("Cleanup completed! {0}.", result.FormattedSummary);
            await ScanCleanerAsync();
        }
        catch (OperationCanceledException)
        {
            CleanerStatusText = LocalizationManager.T("Cleanup stopped. Files that were already deleted stay deleted.");
            await ScanCleanerAsync();
        }
        catch (Exception ex)
        {
            CleanerStatusText = LocalizationManager.T("Cleanup error: {0}", ex.Message);
        }
        finally
        {
            IsCleaning = false;
        }
    }

    private bool _isRefreshingStates;

    [RelayCommand]
    private async Task ToggleStartupItemAsync(StartupAppItem item)
    {
        if (item == null || _isRefreshingStates) return;
        if (!WinVelo.App.Services.AppAdminGuard.EnsureAdmin("modify startup programs"))
        {
            return;
        }

        bool targetState = !item.IsEnabled;
        await Task.Yield();
        bool ok = await Task.Run(async () => await _startupService.SetEntryStateAsync(item, targetState));
        if (ok)
        {
            item.IsEnabled = targetState;
        }
    }

    [RelayCommand] private Task StartServiceAsync(WindowsServiceInfo service) => ChangeServiceAsync(service, true);
    [RelayCommand] private Task StopServiceAsync(WindowsServiceInfo service) => ChangeServiceAsync(service, false);
    private async Task ChangeServiceAsync(WindowsServiceInfo service, bool start)
    {
        if (!WinVelo.App.Services.AppAdminGuard.EnsureAdmin("change Windows services")) return;
        bool ok = await Task.Run(() => _servicesService.ToggleServiceStateAsync(service.ServiceName, start));
        Status = ok ? "Service updated." : "Service change failed or is protected.";
        var actual = _servicesService.GetServices().FirstOrDefault(s => s.ServiceName == service.ServiceName);
        if (actual != null) { service.Status = actual.Status; service.StartupType = actual.StartupType; }
    }
    [RelayCommand]
    private async Task SetServiceStartupAsync(WindowsServiceInfo service)
    {
        if (!WinVelo.App.Services.AppAdminGuard.EnsureAdmin("change service startup settings")) return;
        bool ok = await Task.Run(() => _servicesService.SetStartupTypeAsync(service.ServiceName, service.StartupType));
        Status = ok ? "Service startup updated." : "Service change failed or is protected.";
        var actual = _servicesService.GetServices().FirstOrDefault(s => s.ServiceName == service.ServiceName);
        if (actual != null) service.StartupType = actual.StartupType;
    }

    [RelayCommand]
    private async Task RunFixAsync(SystemFixDefinition fix)
    {
        if (!WinVelo.App.Services.AppAdminGuard.EnsureAdmin("run system maintenance fixes"))
        {
            FixStatusText = LocalizationManager.T("Administrator privileges required to run fixes.");
            return;
        }

        FixStatusText = LocalizationManager.T("Running repair: {0}...", fix.Title);
        await Task.Yield();
        var report = await Task.Run(async () => await _fixService.RunFixAsync(fix.Id, _runner));
        FixStatusText = report.FailedCount == 0
            ? LocalizationManager.T("Successfully completed: {0}.", fix.Title)
            : LocalizationManager.T("Repair finished with notes: {0}.", report.Summary);
    }

    [RelayCommand]
    private async Task ApplyDnsAsync(DnsProviderDefinition provider)
    {
        if (!WinVelo.App.Services.AppAdminGuard.EnsureAdmin("change DNS settings"))
        {
            DnsStatusText = LocalizationManager.T("Administrator privileges required to change DNS settings.");
            return;
        }

        if (SelectedDnsAdapter == null) await RefreshDnsAdaptersAsync();
        if (SelectedDnsAdapter == null) { DnsStatusText = LocalizationManager.T("Select a network adapter."); return; }
        int adapterIndex = SelectedDnsAdapter.Index;
        DnsStatusText = LocalizationManager.T("Applying {0} ({1})...", provider.Name, provider.PrimaryDns);
        await Task.Yield();
        try
        {
            bool ok = await Task.Run(async () => await _dnsService.ApplyDnsAsync(provider, adapterIndex, UseDnsOverHttps));
            await RefreshActiveDnsAsync(adapterIndex);
            DnsStatusText = ok ? LocalizationManager.T("Applied {0} successfully.", provider.Name) : LocalizationManager.T("Failed to apply {0}.", provider.Name);
        }
        catch (Exception ex) { DnsStatusText = ex.Message; }
    }

    [RelayCommand]
    private async Task TestDnsLatencyAsync(DnsProviderDefinition provider)
    {
        DnsStatusText = LocalizationManager.T("Pinging {0}...", provider.PrimaryDns);
        await Task.Yield();
        int ping = await Task.Run(async () => await _dnsService.PingDnsAsync(provider.PrimaryDns));
        provider.PingMs = ping;
        DnsStatusText = ping >= 0 ? LocalizationManager.T("{0} latency: {1} ms", provider.Name, ping) : LocalizationManager.T("{0} ping timed out or unavailable.", provider.Name);
    }

    [RelayCommand]
    private async Task PurgeRamAsync()
    {
        CleanerStatusText = LocalizationManager.T("Purging standby memory and trimming working sets...");
        await Task.Yield();
        try
        {
            var result = await Task.Run(async () => await _memoryOptimizer.PurgeMemoryAndStandbyListAsync());
            CleanerStatusText = LocalizationManager.T("Memory Purge Complete! Freed {0} of Standby & Process Cache.", result.FormattedFreed);
        }
        catch (Exception ex)
        {
            CleanerStatusText = LocalizationManager.T("RAM Purge Error: {0}", ex.Message);
        }
    }

    [RelayCommand]
    private async Task BenchmarkAllDnsAsync()
    {
        IsBenchmarkingDns = true;
        DnsStatusText = LocalizationManager.T("Benchmarking eligible DNS providers (3 queries each)...");
        await Task.Yield();
        try
        {
            var results = await Task.Run(async () => await _networkOptimizer.BenchmarkDnsProvidersAsync());
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
                DnsStatusText = LocalizationManager.T("Benchmark complete! Fastest DNS: {0} ({1} ms).", fastest.Name, fastest.AveragePingMs);
            }
            else
            {
                DnsStatusText = LocalizationManager.T("DNS Benchmark complete.");
            }
        }
        catch (Exception ex)
        {
            DnsStatusText = LocalizationManager.T("Benchmark error: {0}", ex.Message);
        }
        finally
        {
            IsBenchmarkingDns = false;
        }
    }

    [RelayCommand]
    private async Task ApplyFastestDnsAsync()
    {
        if (!WinVelo.App.Services.AppAdminGuard.EnsureAdmin("apply DNS settings"))
        {
            DnsStatusText = LocalizationManager.T("Administrator privileges required to change DNS.");
            return;
        }

        var responsive = DnsProviders.Where(p => p.PingMs >= 0 && p.BenchmarkEligible).ToList();
        if (responsive.Count == 0)
        {
            await BenchmarkAllDnsAsync();
            responsive = DnsProviders.Where(p => p.PingMs >= 0 && p.BenchmarkEligible).ToList();
        }

        var fastest = responsive.OrderBy(p => p.PingMs).FirstOrDefault();
        if (fastest != null)
        {
            await ApplyDnsAsync(fastest);
        }
        else
        {
            DnsStatusText = LocalizationManager.T("No responsive DNS providers found. Check internet connection.");
        }
    }

    [RelayCommand]
    private async Task ResetDnsToDhcpAsync()
    {
        if (!WinVelo.App.Services.AppAdminGuard.EnsureAdmin("reset DNS settings"))
        {
            DnsStatusText = LocalizationManager.T("Administrator privileges required to change DNS.");
            return;
        }

        var automatic = _dnsService.GetProviders().First(p => p.PrimaryDns == "DHCP");
        await ApplyDnsAsync(automatic);
    }

    [RelayCommand]
    private async Task ToggleNagleAsync(object? parameter)
    {
        if (_isRefreshingStates) return;
        if (!WinVelo.App.Services.AppAdminGuard.EnsureAdmin("modify network packet optimization"))
        {
            return;
        }

        bool target = parameter is bool b ? b : !IsNagleDisabled;
        NetworkStatusText = $"{(target ? "Disabling Nagle's Algorithm (TCPNoDelay)" : "Restoring default TCP packet batching")}...";
        await Task.Yield();

        bool ok = await Task.Run(async () => await _networkOptimizer.SetNagleAlgorithmStateAsync(target));
        if (ok)
        {
            IsNagleDisabled = await Task.Run(async () => await _networkOptimizer.DetectNagleAlgorithmDisabledAsync());
            NetworkStatusText = IsNagleDisabled
                ? LocalizationManager.T("Nagle's Algorithm Disabled: TCPNoDelay active on active network adapter(s).")
                : LocalizationManager.T("Standard TCP packet batching restored.");
        }
        else
        {
            NetworkStatusText = LocalizationManager.T("Failed to update network adapter TCP parameters.");
        }
    }

    public async void LoadToolsData()
    {
        await Task.Yield();
        _isRefreshingStates = true;

        try
        {
            var startupEntries = await Task.Run(async () => await _startupService.GetStartupEntriesAsync());
            StartupItems.Clear();
            foreach (var item in startupEntries)
            {
                StartupItems.Add(item);
            }

            var services = await Task.Run(() => _servicesService.GetServices().ToList());
            ServicesList.Clear();
            foreach (var svc in services)
            {
                ServicesList.Add(svc);
            }

            var fixes = _fixService.GetAvailableFixes().ToList();
            FixesList.Clear();
            foreach (var fix in fixes)
            {
                FixesList.Add(fix);
            }

            var dnsList = _dnsService.GetProviders().ToList();
            DnsProviders.Clear();
            foreach (var dns in dnsList)
            {
                DnsProviders.Add(dns);
            }

            bool nagleDisabled = await Task.Run(async () => await _networkOptimizer.DetectNagleAlgorithmDisabledAsync());
            IsNagleDisabled = nagleDisabled;
        }
        catch { }
        finally
        {
            _isRefreshingStates = false;
        }
    }
}
