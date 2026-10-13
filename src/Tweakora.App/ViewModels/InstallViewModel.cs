using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tweakora.Core.Operations;
using Tweakora.Modules.Apps;

namespace Tweakora.App.ViewModels;

public sealed partial class InstallViewModel : ObservableObject
{
    private readonly IAppInstallService _installService;
    private readonly IOperationRunner _runner;
    private readonly List<ApplicationItem> _allApps = new();

    public ObservableCollection<ApplicationItem> FilteredApps { get; } = new();
    public ObservableCollection<string> Categories { get; } = new();
    public ObservableCollection<string> OperationLogs { get; } = new();

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private string _selectedCategory = "All";

    [ObservableProperty]
    private string _packageManager = "WinGet"; // "WinGet" or "Chocolatey"

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallSelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(UninstallSelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(UpgradeAllCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private int _progressPercent;

    [ObservableProperty]
    private string _statusMessage = "Select applications to install or manage.";

    [ObservableProperty]
    private int _selectedCount;

    public InstallViewModel(IAppInstallService installService, IOperationRunner runner)
    {
        _installService = installService;
        _runner = runner;

        _runner.OnLog += msg =>
        {
            System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
            {
                OperationLogs.Add(msg);
                if (OperationLogs.Count > 200) OperationLogs.RemoveAt(0);
            });
        };

        _runner.OnProgress += (status, percent) =>
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

        var loaded = _installService.LoadApplications();
        foreach (var app in loaded)
        {
            app.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(ApplicationItem.IsSelected))
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

    private bool CanExecuteAction() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanExecuteAction))]
    private async Task InstallSelectedAsync()
    {
        if (!Tweakora.App.Services.AppAdminGuard.EnsureAdmin("install applications"))
        {
            StatusMessage = "Administrator privileges required to install applications.";
            return;
        }

        var selected = _allApps.Where(a => a.IsSelected).ToList();
        if (selected.Count == 0)
        {
            StatusMessage = "No applications selected to install.";
            return;
        }

        IsBusy = true;
        try
        {
            StatusMessage = $"Installing {selected.Count} application(s) via {PackageManager}...";
            var report = await _installService.InstallAppsAsync(selected, PackageManager, _runner);
            StatusMessage = $"Installation completed: {report.Summary}.";
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

    [RelayCommand(CanExecute = nameof(CanExecuteAction))]
    private async Task UninstallSelectedAsync()
    {
        if (!Tweakora.App.Services.AppAdminGuard.EnsureAdmin("uninstall applications"))
        {
            StatusMessage = "Administrator privileges required to uninstall applications.";
            return;
        }

        var selected = _allApps.Where(a => a.IsSelected).ToList();
        if (selected.Count == 0)
        {
            StatusMessage = "No applications selected to uninstall.";
            return;
        }

        IsBusy = true;
        try
        {
            StatusMessage = $"Uninstalling {selected.Count} application(s) via {PackageManager}...";
            var report = await _installService.UninstallAppsAsync(selected, PackageManager, _runner);
            StatusMessage = $"Uninstallation completed: {report.Summary}.";
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

    [RelayCommand(CanExecute = nameof(CanExecuteAction))]
    private async Task UpgradeAllAsync()
    {
        if (!Tweakora.App.Services.AppAdminGuard.EnsureAdmin("upgrade applications"))
        {
            StatusMessage = "Administrator privileges required to upgrade applications.";
            return;
        }

        IsBusy = true;
        try
        {
            StatusMessage = $"Checking and upgrading all applications via {PackageManager}...";
            string output = await _installService.UpgradeAllAsync(PackageManager);
            StatusMessage = "Upgrade check completed.";
            OperationLogs.Add(output);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Upgrade error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void OpenAppWebsite(ApplicationItem app)
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
