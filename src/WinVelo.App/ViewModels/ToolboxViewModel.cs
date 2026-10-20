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

    public ToolboxViewModel(
        ICleanerService cleanerService,
        IStartupService startupService,
        IServicesManagerService servicesService,
        IFixService fixService,
        IDnsService dnsService,
        IOperationRunner runner,
        IMemoryOptimizerService memoryOptimizer,
        INetworkOptimizerService networkOptimizer,
        WinVelo.Modules.Power.IPowerPlanService? powerPlan = null,
        WinVelo.Modules.Apps.IBuiltInAppsService? builtInApps = null)
    {
        _powerPlan = powerPlan;
        _builtInApps = builtInApps;
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

    [RelayCommand]
    private async Task RunFixAsync(SystemFixDefinition fix)
    {
        if (_isFixRunning)
        {
            FixStatusText = LocalizationManager.T("Another repair is still running. Please wait for it to finish.");
            return;
        }

        if (!WinVelo.App.Services.AppAdminGuard.EnsureAdmin("run system maintenance fixes"))
        {
            FixStatusText = LocalizationManager.T("Administrator privileges required to run fixes.");
            return;
        }

        _isFixRunning = true;
        try
        {
            FixStatusText = LocalizationManager.T("Running repair: {0}...", fix.Title);
            await Task.Yield();
            var report = await Task.Run(async () => await _fixService.RunFixAsync(fix.Id, _runner));
            FixStatusText = report.FailedCount == 0
                ? LocalizationManager.T("Successfully completed: {0}.", fix.Title)
                : LocalizationManager.T("Repair finished with notes: {0}.", report.Summary);
        }
        catch (Exception ex)
        {
            FixStatusText = LocalizationManager.T("Repair failed: {0}", ex.Message);
        }
        finally
        {
            _isFixRunning = false;
        }
    }

    private bool _isFixRunning;

    // ---------- Power plan (WinUtil "Performance Plans") and O&O ShutUp10++ ----------
    private readonly WinVelo.Modules.Power.IPowerPlanService? _powerPlan;

    [ObservableProperty]
    private string _powerPlanStatusText = LocalizationManager.T("Ultimate Performance keeps the CPU at full speed. It uses more power and is not meant for laptops on battery.");

    [RelayCommand]
    private async Task EnableUltimatePlanAsync() => await RunPowerPlanAsync(enable: true);

    [RelayCommand]
    private async Task DisableUltimatePlanAsync() => await RunPowerPlanAsync(enable: false);

    private async Task RunPowerPlanAsync(bool enable)
    {
        if (_powerPlan == null || _isFixRunning) return;
        if (enable)
        {
            var answer = System.Windows.MessageBox.Show(
                LocalizationManager.T("Ultimate Performance keeps the CPU at full speed. It uses more power and is not meant for laptops on battery. Add and switch to it now?"),
                LocalizationManager.T("Ultimate Performance"), System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning, System.Windows.MessageBoxResult.No);
            if (answer != System.Windows.MessageBoxResult.Yes) return;
        }

        if (!WinVelo.App.Services.AppAdminGuard.EnsureAdmin("change the power plan"))
        {
            PowerPlanStatusText = LocalizationManager.T("Administrator privileges required to run fixes.");
            return;
        }

        _isFixRunning = true;
        try
        {
            var result = await Task.Run(() => enable ? _powerPlan.EnableUltimatePerformanceAsync() : _powerPlan.DisableUltimatePerformanceAsync());
            PowerPlanStatusText = LocalizationManager.TranslateMessage(result.Message);
        }
        catch (Exception ex)
        {
            PowerPlanStatusText = LocalizationManager.T("Repair failed: {0}", ex.Message);
        }
        finally
        {
            _isFixRunning = false;
        }
    }

    /// <summary>Opens O&amp;O's official page. Like WinVelo's other third-party tools, nothing is downloaded or run automatically.</summary>
    [RelayCommand]
    private void OpenOosu()
    {
        _fixService.OpenUrl("https://www.oo-software.com/en/shutup10");
        PowerPlanStatusText = LocalizationManager.T("Opened the O&O ShutUp10++ page in your browser.");
    }

    // ---------- Built-in Windows apps (WinUtil's AppX removal) ----------
    private readonly WinVelo.Modules.Apps.IBuiltInAppsService? _builtInApps;
    private readonly System.Collections.Generic.HashSet<string> _pendingAppxSelection = new(StringComparer.OrdinalIgnoreCase);
    private bool _builtInAppsLoaded;
    private CancellationTokenSource? _appxCts;

    public ObservableCollection<WinVelo.Modules.Apps.BuiltInApp> BuiltInApps { get; } = new();

    [ObservableProperty]
    private string _appxStatusText = LocalizationManager.T("Select built-in apps to remove. Nothing is removed until you confirm.");

    [ObservableProperty]
    private bool _isAppxBusy;

    public System.Collections.Generic.List<string> GetSelectedAppxKeys() => _builtInAppsLoaded
        ? BuiltInApps.Where(a => a.IsSelected).Select(a => a.Key).ToList()
        : _pendingAppxSelection.ToList();

    /// <summary>Selects built-in apps by WinUtil key (from an imported config). Nothing is removed.</summary>
    public void SetSelectedAppxKeys(System.Collections.Generic.IEnumerable<string> keys)
    {
        _pendingAppxSelection.Clear();
        foreach (string key in keys) _pendingAppxSelection.Add(key);
        EnsureBuiltInAppsListed();
        foreach (var app in BuiltInApps) app.IsSelected = _pendingAppxSelection.Contains(app.Key) || _pendingAppxSelection.Contains(app.PackageId);
    }

    private void EnsureBuiltInAppsListed()
    {
        if (_builtInAppsLoaded || _builtInApps == null) return;
        BuiltInApps.Clear();
        foreach (var app in _builtInApps.LoadApps())
        {
            app.IsSelected = _pendingAppxSelection.Contains(app.Key) || _pendingAppxSelection.Contains(app.PackageId);
            BuiltInApps.Add(app);
        }
        _builtInAppsLoaded = true;
    }

    /// <summary>Called when the Built-in apps tool opens: lists the apps and checks which are installed.</summary>
    public async Task EnsureBuiltInAppsLoadedAsync()
    {
        EnsureBuiltInAppsListed();
        await RefreshAppxAsync();
    }

    [RelayCommand]
    private async Task RefreshAppxAsync()
    {
        if (_builtInApps == null || IsAppxBusy) return;
        EnsureBuiltInAppsListed();
        AppxStatusText = LocalizationManager.T("Checking which built-in apps are installed...");
        var installed = await Task.Run(async () => await _builtInApps.GetInstalledPackagesAsync());
        foreach (var app in BuiltInApps)
        {
            app.IsInstalled = installed?.Contains(app.PackageId);
        }
        AppxStatusText = installed == null
            ? LocalizationManager.T("Could not check which apps are installed. You can still select and remove apps.")
            : LocalizationManager.T("{0} of {1} built-in apps are installed.", BuiltInApps.Count(a => a.IsInstalled == true), BuiltInApps.Count);
    }

    [RelayCommand]
    private void SelectRecommendedAppx()
    {
        EnsureBuiltInAppsListed();
        foreach (var app in BuiltInApps) app.IsSelected = app.IsRecommended && app.IsInstalled != false;
        AppxStatusText = LocalizationManager.T("Selected {0} apps that are safe to remove for almost everyone.", BuiltInApps.Count(a => a.IsSelected));
    }

    [RelayCommand]
    private void SelectInstalledAppx()
    {
        EnsureBuiltInAppsListed();
        foreach (var app in BuiltInApps) app.IsSelected = app.IsInstalled == true;
        AppxStatusText = LocalizationManager.T("Selected {0} installed apps. Check the warnings before you remove them.", BuiltInApps.Count(a => a.IsSelected));
    }

    [RelayCommand]
    private void ClearAppxSelection()
    {
        foreach (var app in BuiltInApps) app.IsSelected = false;
        _pendingAppxSelection.Clear();
    }

    [RelayCommand]
    private void CancelAppx() => _appxCts?.Cancel();

    [RelayCommand]
    private async Task RemoveSelectedAppxAsync() => await RunAppxAsync(remove: true);

    [RelayCommand]
    private async Task ReinstallSelectedAppxAsync() => await RunAppxAsync(remove: false);

    private async Task RunAppxAsync(bool remove)
    {
        if (_builtInApps == null || IsAppxBusy) return;
        var selected = BuiltInApps.Where(a => a.IsSelected).ToList();
        if (selected.Count == 0)
        {
            AppxStatusText = LocalizationManager.T("No built-in apps are selected.");
            return;
        }

        if (remove)
        {
            var lines = selected.Select(a => string.IsNullOrEmpty(a.Note)
                ? "• " + a.Name
                : "• " + a.Name + " (" + LocalizationManager.T(a.Risk) + "): " + LocalizationManager.T(a.Note));
            var answer = System.Windows.MessageBox.Show(
                LocalizationManager.T("Remove these {0} built-in apps for all users? They are also removed from the Windows image, so new user accounts do not get them.\n\n{1}\n\nYou can reinstall them later from this page.", selected.Count, string.Join("\n", lines)),
                LocalizationManager.T("Remove built-in apps"), System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning, System.Windows.MessageBoxResult.No);
            if (answer != System.Windows.MessageBoxResult.Yes) return;
        }

        if (!WinVelo.App.Services.AppAdminGuard.EnsureAdmin(remove ? "remove built-in apps" : "reinstall built-in apps"))
        {
            AppxStatusText = LocalizationManager.T("Administrator privileges required to run fixes.");
            return;
        }

        IsAppxBusy = true;
        _appxCts = new CancellationTokenSource();
        try
        {
            AppxStatusText = remove
                ? LocalizationManager.T("Removing {0} built-in apps...", selected.Count)
                : LocalizationManager.T("Reinstalling {0} built-in apps...", selected.Count);
            var token = _appxCts.Token;
            var report = await Task.Run(() => remove
                ? _builtInApps.RemoveAsync(selected, _runner, token)
                : _builtInApps.ReinstallAsync(selected, _runner, token));
            var failures = report.Results.Where(r => r.IsFailure).Select(r => LocalizationManager.TranslateMessage(r.Reason)).ToList();
            AppxStatusText = failures.Count == 0
                ? LocalizationManager.T("Done: {0} succeeded, {1} skipped.", report.SucceededCount, report.SkippedCount)
                : LocalizationManager.T("Done: {0} succeeded, {1} failed.", report.SucceededCount, report.FailedCount) + "\n" + string.Join("\n", failures);
            if (remove)
            {
                foreach (var app in selected.Where(a => a.IsInstalled == false)) app.IsSelected = false;
            }
        }
        catch (Exception ex)
        {
            AppxStatusText = LocalizationManager.T("Repair failed: {0}", ex.Message);
        }
        finally
        {
            IsAppxBusy = false;
            _appxCts.Dispose();
            _appxCts = null;
        }
    }

    [RelayCommand]
    private async Task ApplyDnsAsync(DnsProviderDefinition provider)
    {
        if (!WinVelo.App.Services.AppAdminGuard.EnsureAdmin("change DNS settings"))
        {
            DnsStatusText = LocalizationManager.T("Administrator privileges required to change DNS settings.");
            return;
        }

        DnsStatusText = LocalizationManager.T("Applying {0} ({1})...", provider.Name, provider.PrimaryDns);
        await Task.Yield();
        bool ok = await Task.Run(async () => await _dnsService.ApplyDnsAsync(provider));
        if (ok)
        {
            foreach (var p in DnsProviders)
            {
                p.IsActive = (p.Id == provider.Id);
            }
            DnsStatusText = LocalizationManager.T("Applied {0} successfully.", provider.Name);
        }
        else
        {
            DnsStatusText = LocalizationManager.T("Failed to apply {0}.", provider.Name);
        }
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
        DnsStatusText = LocalizationManager.T("Measuring {0} DNS providers (3 probes each)...", DnsProviders.Count(p => p.Id != DnsService.DhcpId));
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

        // Only plain resolvers are picked automatically; filtering ones (ads, malware, adult) must be chosen by hand.
        var responsive = DnsProviders.Where(p => p.IsNeutral && p.PingMs > 0).ToList();
        if (responsive.Count == 0)
        {
            await BenchmarkAllDnsAsync();
            responsive = DnsProviders.Where(p => p.IsNeutral && p.PingMs > 0).ToList();
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

        DnsStatusText = LocalizationManager.T("Resetting network adapters to Automatic (DHCP) DNS...");
        await Task.Yield();
        bool ok = await Task.Run(async () => await _networkOptimizer.ResetDnsToDhcpAsync());
        if (ok)
        {
            foreach (var p in DnsProviders)
            {
                p.IsActive = (p.PrimaryDns == "DHCP");
            }
            DnsStatusText = LocalizationManager.T("Successfully reset network adapters to Automatic (DHCP) DNS.");
        }
        else
        {
            DnsStatusText = LocalizationManager.T("Failed to reset DNS. Ensure network adapter is active.");
        }
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
            var current = await Task.Run(async () => await _dnsService.GetCurrentServersAsync());
            var active = DnsService.MatchActive(dnsList, current);
            DnsProviders.Clear();
            foreach (var dns in dnsList)
            {
                dns.IsActive = active != null ? ReferenceEquals(dns, active) : (current.Count == 0 && dns.Id == DnsService.DhcpId);
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
