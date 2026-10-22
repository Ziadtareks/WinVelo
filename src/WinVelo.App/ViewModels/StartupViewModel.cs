using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using WinVelo.App.Services;
using WinVelo.Core.Operations;
using WinVelo.Modules.Startup;
using WinVelo.App.Localization;

namespace WinVelo.App.ViewModels;

public sealed partial class StartupViewModel : ObservableObject
{
    private readonly IStartupService _startupService;
    private readonly IActivityTracker? _activityTracker;
    private readonly System.Threading.SemaphoreSlim _loadLock = new(1, 1);

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private int _enabledCount;

    [ObservableProperty]
    private int _highImpactCount;

    [ObservableProperty]
    private double _bootDelaySeconds;

    [ObservableProperty]
    private string _bootDelayText = LocalizationManager.T("Estimated (heuristic)");

    [ObservableProperty]
    private string _bootTimeSavedText = LocalizationManager.T("Relative weight");

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _selectedFilter = "All"; // "All", "Enabled", "Disabled", "HighImpact"

    public bool IsFilterAll => SelectedFilter == "All";
    public bool IsFilterEnabled => SelectedFilter == "Enabled";
    public bool IsFilterDisabled => SelectedFilter == "Disabled";
    public bool IsFilterHighImpact => SelectedFilter == "HighImpact";

    partial void OnSelectedFilterChanged(string value)
    {
        OnPropertyChanged(nameof(IsFilterAll));
        OnPropertyChanged(nameof(IsFilterEnabled));
        OnPropertyChanged(nameof(IsFilterDisabled));
        OnPropertyChanged(nameof(IsFilterHighImpact));
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReady))]
    private bool _isLoading;

    public bool IsReady => !IsLoading;

    [ObservableProperty]
    private bool _isOptimizing;

    [ObservableProperty]
    private string _statusMessage = LocalizationManager.T("Ready. Toggle apps to control Windows boot startup.");

    public ObservableCollection<StartupAppItem> AllApps { get; } = new();
    public ObservableCollection<StartupAppItem> FilteredApps { get; } = new();

    public StartupViewModel(IStartupService startupService, IActivityTracker? activityTracker = null)
    {
        _startupService = startupService;
        _activityTracker = activityTracker;
        _ = RefreshAsync();
    }

    partial void OnSearchTextChanged(string value)
    {
        ApplyFilter();
    }

    [RelayCommand]
    public void SetFilter(string filter)
    {
        SelectedFilter = filter;
        ApplyFilter();
    }

    private bool _isRefreshingStates;

    private Task? _startupLoadTask;
    [RelayCommand]
    public Task RefreshAsync() => GetOrStartLoad(false);

    private Task GetOrStartLoad(bool silent) => _startupLoadTask is { IsCompleted: false }
        ? _startupLoadTask : _startupLoadTask = LoadStartupAppsAsync(silent);

    /// <summary>
    /// Backward-compatibility method alias for tests and direct callers.
    /// </summary>
    public Task RefreshCommandAsync() => RefreshAsync();

    [RelayCommand]
    public async Task ToggleAppAsync(StartupAppItem item)
    {
        if (item == null || _isRefreshingStates || item.IsToggling) return;

        if (!AppAdminGuard.EnsureAdmin("modify startup applications"))
        {
            return;
        }

        item.IsToggling = true;
        using var activity = _activityTracker?.BeginActivity($"Updating {item.Name} startup state...");
        await Task.Yield();

        bool targetState = !item.IsEnabled;
        bool success = false;

        try
        {
            success = await Task.Run(async () => await _startupService.SetEntryStateAsync(item, targetState));
            if (!success)
            {
                StatusMessage = LocalizationManager.T("Failed to change startup state for {0}.", item.Name);
            }
            else
            {
                StatusMessage = LocalizationManager.T("{0} is now {1} on Windows startup.", item.Name, (targetState ? "enabled" : "disabled"));
            }
        }
        catch (Exception ex)
        {
            item.IsEnabled = !targetState;
            StatusMessage = LocalizationManager.T("Error: {0}", ex.Message);
            Log.Error(ex, "Failed to toggle startup item {Name}", item.Name);
        }
        finally
        {
            item.IsToggling = false;
            RecalculateMetrics();
            ApplyFilter();
            _activityTracker?.CompleteActivity(success
                ? LocalizationManager.T("{0} startup {1}", item.Name, (targetState ? "enabled" : "disabled"))
                : LocalizationManager.T("Failed to update {0}", item.Name));
        }
    }

    [RelayCommand]
    public async Task OptimizeStartupAsync()
    {
        if (!AppAdminGuard.EnsureAdmin("optimize Windows startup applications"))
        {
            StatusMessage = LocalizationManager.T("Administrator privileges required to optimize startup apps.");
            return;
        }

        var heavyNonEssential = AllApps.Where(a => a.IsEnabled && a.Impact == "High" && !a.IsEssentialSystemItem).ToList();
        if (heavyNonEssential.Count == 0)
        {
            StatusMessage = LocalizationManager.T("Startup is already optimal! No heavy non-essential apps are enabled.");
            return;
        }

        IsOptimizing = true;
        using var optActivity = _activityTracker?.BeginActivity($"Optimizing startup: disabling {heavyNonEssential.Count} heavy apps...");
        StatusMessage = LocalizationManager.T("Disabling {0} heavy startup applications...", heavyNonEssential.Count);
        await Task.Yield();

        try
        {
            int disabledCount = await Task.Run(async () => await _startupService.OptimizeStartupAsync());
            await LoadStartupAppsAsync(silent: true);
            StatusMessage = LocalizationManager.T("Disabled {0} heavy non-essential startup applications.", disabledCount);
            _activityTracker?.CompleteActivity(LocalizationManager.T("Startup optimized: Disabled {0} heavy non-essential apps", disabledCount));
        }
        catch (Exception ex)
        {
            StatusMessage = LocalizationManager.T("Optimization error: {0}", ex.Message);
            Log.Error(ex, "Failed to optimize startup applications");
        }
        finally
        {
            IsOptimizing = false;
        }
    }

    [RelayCommand]
    public void OpenFolder(StartupAppItem item)
    {
        if (item == null) return;

        string target = !string.IsNullOrEmpty(item.CleanExePath) && File.Exists(item.CleanExePath)
            ? item.CleanExePath
            : (!string.IsNullOrEmpty(item.Command) && File.Exists(item.Command) ? item.Command : string.Empty);

        if (string.IsNullOrEmpty(target))
        {
            StatusMessage = LocalizationManager.T("File location for {0} not found on disk.", item.Name);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{target}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            StatusMessage = LocalizationManager.T("Could not open folder: {0}", ex.Message);
        }
    }

    public async Task RefreshStatesSilentAsync()
    {
        await GetOrStartLoad(true);
    }

    private async Task LoadStartupAppsAsync(bool silent = false)
    {
        await _loadLock.WaitAsync();
        _isRefreshingStates = true;
        try
        {
            IsLoading = true;
            if (!silent)
            {
                StatusMessage = LocalizationManager.T("Scanning startup programs across 5 Windows locations...");
            }

            await Task.Yield();
            var entries = await Task.Run(async () =>
            {
                var list = await _startupService.GetStartupEntriesAsync();
                foreach (var entry in list)
                {
                    try
                    {
                        string targetPath = !string.IsNullOrEmpty(entry.CleanExePath) ? entry.CleanExePath : entry.Command;
                        var icon = AppIconExtractor.ExtractIcon(targetPath);
                        if (icon != null)
                        {
                            entry.IconImage = icon;
                        }
                    }
                    catch { }
                }
                return list;
            });

            Action updateAction = () =>
            {
                AllApps.Clear();
                foreach (var entry in entries.OrderByDescending(e => e.Impact == "High")
                                            .ThenByDescending(e => e.IsEnabled)
                                            .ThenBy(e => e.Name))
                {
                    AllApps.Add(entry);
                }

                RecalculateMetrics();
                ApplyFilter();

                if (!silent)
                {
                    StatusMessage = LocalizationManager.T("Found {0} startup apps ({1} enabled, {2} high impact).", TotalCount, EnabledCount, HighImpactCount);
                }
            };

            if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
            {
                Application.Current.Dispatcher.Invoke(updateAction);
            }
            else
            {
                updateAction();
            }
        }
        catch (Exception ex)
        {
            StatusMessage = LocalizationManager.T("Failed to scan startup apps: {0}", ex.Message);
            Log.Error(ex, "Error scanning startup apps");
        }
        finally
        {
            _isRefreshingStates = false;
            IsLoading = false;
            _loadLock.Release();
        }
    }

    private void RecalculateMetrics()
    {
        TotalCount = AllApps.Count;
        EnabledCount = AllApps.Count(a => a.IsEnabled);
        HighImpactCount = AllApps.Count(a => a.IsEnabled && a.Impact == "High");

        double totalDelay = AllApps.Where(a => a.IsEnabled).Sum(a => a.BootDelaySeconds);
        BootDelaySeconds = Math.Round(totalDelay, 1);
        BootDelayText = LocalizationManager.T("Estimated (heuristic)");
        BootTimeSavedText = LocalizationManager.T("{0} high-impact active", HighImpactCount);
    }

    private void ApplyFilter()
    {
        FilteredApps.Clear();
        string q = (SearchText ?? string.Empty).Trim().ToLowerInvariant();

        foreach (var app in AllApps)
        {
            // Category / State filter
            bool stateMatch = SelectedFilter switch
            {
                "Enabled" => app.IsEnabled,
                "Disabled" => !app.IsEnabled,
                "HighImpact" => app.Impact == "High",
                _ => true
            };

            if (!stateMatch) continue;

            // Search filter
            if (!string.IsNullOrEmpty(q))
            {
                bool searchMatch = app.Name.ToLowerInvariant().Contains(q) ||
                                  app.Publisher.ToLowerInvariant().Contains(q) ||
                                  app.CleanExePath.ToLowerInvariant().Contains(q) ||
                                  app.Command.ToLowerInvariant().Contains(q) ||
                                  app.Source.ToLowerInvariant().Contains(q) ||
                                  app.Impact.ToLowerInvariant().Contains(q);

                if (!searchMatch) continue;
            }

            FilteredApps.Add(app);
        }
    }
}
