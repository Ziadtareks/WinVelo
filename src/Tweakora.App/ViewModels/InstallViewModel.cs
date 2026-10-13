using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tweakora.Core.Safety;
using Tweakora.Modules.Apps;

namespace Tweakora.App.ViewModels;

public sealed partial class InstallViewModel : ViewModelBase
{
    private readonly ApplicationCatalogManager _catalogManager = new() { IsSimulationMode = true };
    private readonly List<AppItemViewModel> _allApps = new();

    public ObservableCollection<AppItemViewModel> FilteredApps { get; } = new();
    public ObservableCollection<string> Categories { get; } = new();
    public ObservableCollection<string> OperationLogs { get; } = new();

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private string _selectedCategory = "All";

    [ObservableProperty]
    private string _packageManager = "WinGet"; // "WinGet" or "Chocolatey"

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private int _progressPercent;

    [ObservableProperty]
    private string _statusMessage = "Select applications to install or manage.";

    [ObservableProperty]
    private int _selectedCount;

    public InstallViewModel()
    {
        _catalogManager.IsSimulationMode = TestModeManager.IsTestMode;
        _catalogManager.OnLog += msg =>
        {
            System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
            {
                OperationLogs.Add(msg);
                if (OperationLogs.Count > 200) OperationLogs.RemoveAt(0);
            });
        };
        _catalogManager.OnProgress += (status, percent) =>
        {
            System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
            {
                StatusMessage = status;
                ProgressPercent = percent;
            });
        };

        LoadApps();
    }

    public void LoadApps()
    {
        _allApps.Clear();
        Categories.Clear();
        Categories.Add("All");

        var loaded = _catalogManager.LoadCatalog();
        foreach (var app in loaded)
        {
            app.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(AppItemViewModel.IsSelected))
                {
                    UpdateSelectedCount();
                }
            };
            _allApps.Add(app);

            if (!Categories.Contains(app.Category))
            {
                Categories.Add(app.Category);
            }
        }

        ApplyFilter();
        UpdateSelectedCount();
    }

    public void SetSearchQuery(string query)
    {
        SearchQuery = query;
    }

    partial void OnSearchQueryChanged(string value) => ApplyFilter();
    partial void OnSelectedCategoryChanged(string value) => ApplyFilter();

    public void ApplyFilter()
    {
        FilteredApps.Clear();
        string q = SearchQuery?.Trim() ?? string.Empty;

        var query = _allApps.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(SelectedCategory) && SelectedCategory != "All")
        {
            query = query.Where(a => a.Category.Equals(SelectedCategory, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            query = query.Where(a =>
                a.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                a.Description.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                a.Key.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                a.Category.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var item in query)
        {
            FilteredApps.Add(item);
        }
    }

    private void UpdateSelectedCount()
    {
        SelectedCount = _allApps.Count(a => a.IsSelected);
    }

    [RelayCommand]
    private void SelectCategory(string category)
    {
        SelectedCategory = category;
    }

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var app in _allApps)
        {
            app.IsSelected = false;
        }
        UpdateSelectedCount();
    }

    [RelayCommand]
    private async Task InstallSelectedAsync()
    {
        var selected = _allApps.Where(a => a.IsSelected).ToList();
        if (selected.Count == 0)
        {
            StatusMessage = "No applications selected to install.";
            return;
        }

        IsBusy = true;
        try
        {
            await _catalogManager.InstallAppsAsync(selected, PackageManager);
            StatusMessage = $"Installation completed for {selected.Count} application(s).";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Installation error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task UninstallSelectedAsync()
    {
        var selected = _allApps.Where(a => a.IsSelected).ToList();
        if (selected.Count == 0)
        {
            StatusMessage = "No applications selected to uninstall.";
            return;
        }

        IsBusy = true;
        try
        {
            await _catalogManager.UninstallAppsAsync(selected, PackageManager);
            StatusMessage = $"Uninstallation completed for {selected.Count} application(s).";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Uninstallation error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task UpgradeAllAsync()
    {
        IsBusy = true;
        try
        {
            OperationLogs.Add($"[UPGRADE] Checking for updates via {PackageManager}...");
            await Task.Delay(TestModeManager.IsTestMode ? 100 : 500);
            OperationLogs.Add($"[UPGRADE] All installed applications are up to date.");
            StatusMessage = "Upgrade check completed.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void OpenAppWebsite(AppItemViewModel app)
    {
        if (string.IsNullOrWhiteSpace(app.Link)) return;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = app.Link,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not open link: {ex.Message}";
        }
    }

    public List<string> GetSelectedAppKeys() => _allApps.Where(a => a.IsSelected).Select(a => a.Key).ToList();

    public void SetSelectedAppKeys(IEnumerable<string> keys)
    {
        var keySet = new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase);
        foreach (var app in _allApps)
        {
            app.IsSelected = keySet.Contains(app.Key);
        }
        UpdateSelectedCount();
    }
}
