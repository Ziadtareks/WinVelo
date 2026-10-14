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
using Tweakora.App.Services;
using Tweakora.Core.Operations;
using Tweakora.Modules.Startup;

namespace Tweakora.App.ViewModels;

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
    private string _bootDelayText = "~0.0s boot impact";

    [ObservableProperty]
    private string _bootTimeSavedText = "~0.0s saved";

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
    private bool _isLoading;

    [ObservableProperty]
    private bool _isOptimizing;

    [ObservableProperty]
    private string _statusMessage = "Ready. Toggle apps to control Windows boot startup.";

    public ObservableCollection<StartupAppItem> AllApps { get; } = new();
    public ObservableCollection<StartupAppItem> FilteredApps { get; } = new();

    public StartupViewModel(IStartupService startupService, IActivityTracker? activityTracker = null)
    {
        _startupService = startupService;
        _activityTracker = activityTracker;
        _ = LoadStartupAppsAsync();
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

    [RelayCommand]
    public async Task RefreshAsync()
    {
        await LoadStartupAppsAsync();
    }

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
                StatusMessage = $"Failed to change startup state for {item.Name}.";
            }
            else
            {
                StatusMessage = $"{item.Name} is now {(targetState ? "enabled" : "disabled")} on Windows startup.";
            }
        }
        catch (Exception ex)
        {
            item.IsEnabled = !targetState;
            StatusMessage = $"Error: {ex.Message}";
            Log.Error(ex, "Failed to toggle startup item {Name}", item.Name);
        }
        finally
        {
            item.IsToggling = false;
            RecalculateMetrics();
            ApplyFilter();
            _activityTracker?.CompleteActivity(success
                ? $"{item.Name} startup {(targetState ? "enabled" : "disabled")}"
                : $"Failed to update {item.Name}");
        }
    }

    [RelayCommand]
    public async Task OptimizeStartupAsync()
    {
        if (!AppAdminGuard.EnsureAdmin("optimize Windows startup applications"))
        {
            StatusMessage = "Administrator privileges required to optimize startup apps.";
            return;
        }

        var heavyNonEssential = AllApps.Where(a => a.IsEnabled && a.Impact == "High" && !a.IsEssentialSystemItem).ToList();
        if (heavyNonEssential.Count == 0)
        {
            StatusMessage = "Startup is already optimal! No heavy non-essential apps are enabled.";
            return;
        }

        IsOptimizing = true;
        using var optActivity = _activityTracker?.BeginActivity($"Optimizing startup: disabling {heavyNonEssential.Count} heavy apps...");
        StatusMessage = $"Disabling {heavyNonEssential.Count} heavy startup applications...";
        await Task.Yield();

        try
        {
            int disabledCount = await Task.Run(async () => await _startupService.OptimizeStartupAsync());
            await LoadStartupAppsAsync(silent: true);
            double timeSaved = disabledCount * 1.2;
            StatusMessage = $"Optimized! Disabled {disabledCount} heavy apps, saving ~{timeSaved:F1}s on boot.";
            _activityTracker?.CompleteActivity($"Startup optimized! Disabled {disabledCount} heavy apps (saved ~{timeSaved:F1}s boot time)");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Optimization error: {ex.Message}";
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
            StatusMessage = $"File location for {item.Name} not found on disk.";
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
            StatusMessage = $"Could not open folder: {ex.Message}";
        }
    }

    public async Task RefreshStatesSilentAsync()
    {
        await LoadStartupAppsAsync(silent: true);
    }

    private async Task LoadStartupAppsAsync(bool silent = false)
    {
        await _loadLock.WaitAsync();
        _isRefreshingStates = true;
        try
        {
            if (!silent)
            {
                IsLoading = true;
                StatusMessage = "Scanning startup programs across 5 Windows locations...";
            }

            await Task.Yield();
            var entries = await Task.Run(async () => await _startupService.GetStartupEntriesAsync());

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
                    StatusMessage = $"Found {TotalCount} startup apps ({EnabledCount} enabled, ~{BootDelaySeconds:F1}s boot delay).";
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
            StatusMessage = $"Failed to scan startup apps: {ex.Message}";
            Log.Error(ex, "Error scanning startup apps");
        }
        finally
        {
            _isRefreshingStates = false;
            if (!silent)
            {
                IsLoading = false;
            }
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
        BootDelayText = $"~{BootDelaySeconds:F1}s boot impact";

        double savedDelay = AllApps.Where(a => !a.IsEnabled).Sum(a => a.BootDelaySeconds);
        BootTimeSavedText = $"~{savedDelay:F1}s boot time saved";
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
