using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tweakora.Core.Operations;
using Tweakora.Modules.Config;

namespace Tweakora.App.ViewModels;

public sealed partial class ConfigViewModel : ObservableObject
{
    private readonly IFeatureService _featureService;
    private readonly IFixService _fixService;
    private readonly IOperationRunner _runner;
    private readonly List<FeatureDefinition> _allFeatures = new();

    public ObservableCollection<FeatureDefinition> FilteredFeatures { get; } = new();
    public ObservableCollection<LegacyAppletDefinition> LegacyPanels { get; } = new();
    public ObservableCollection<string> ConfigLogs { get; } = new();

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallSelectedFeaturesCommand))]
    [NotifyCanExecuteChangedFor(nameof(UninstallSelectedFeaturesCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunFixCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = "Select Windows features to install or run system maintenance fixes.";

    [ObservableProperty]
    private int _selectedFeaturesCount;

    [ObservableProperty]
    private bool _isOpenSshConfirmOpen;

    public ConfigViewModel(
        IFeatureService featureService,
        IFixService fixService,
        IOperationRunner runner)
    {
        _featureService = featureService;
        _fixService = fixService;
        _runner = runner;

        _runner.OnLog += msg =>
        {
            System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
            {
                ConfigLogs.Add(msg);
                if (ConfigLogs.Count > 150) ConfigLogs.RemoveAt(0);
            });
        };

        LoadData();
    }

    public void LoadData()
    {
        _allFeatures.Clear();
        var features = _featureService.LoadFeatures();
        foreach (var f in features)
        {
            _allFeatures.Add(f);
        }

        LegacyPanels.Clear();
        foreach (var panel in _fixService.GetLegacyApplets())
        {
            LegacyPanels.Add(panel);
        }

        ApplyFilter();
        UpdateSelectedCount();
    }

    public void SetSearchQuery(string query)
    {
        SearchQuery = query;
    }

    partial void OnSearchQueryChanged(string value) => ApplyFilter();

    public void ApplyFilter()
    {
        FilteredFeatures.Clear();
        string q = SearchQuery?.Trim() ?? string.Empty;

        var query = _allFeatures.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            query = query.Where(f =>
                f.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                f.Description.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                f.Key.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var item in query)
        {
            FilteredFeatures.Add(item);
        }
    }

    private void UpdateSelectedCount()
    {
        SelectedFeaturesCount = _allFeatures.Count(f => f.IsSelected);
    }

    private bool CanExecuteAction() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanExecuteAction))]
    private async Task InstallSelectedFeaturesAsync()
    {
        if (!Tweakora.App.Services.AppAdminGuard.EnsureAdmin("enable Windows features"))
        {
            StatusMessage = "Administrator privileges required to enable features.";
            return;
        }

        var selected = _allFeatures.Where(f => f.IsSelected).ToList();
        if (selected.Count == 0)
        {
            StatusMessage = "No features selected.";
            return;
        }

        IsBusy = true;
        try
        {
            StatusMessage = $"Enabling {selected.Count} Windows feature(s)...";
            var report = await _featureService.EnableFeaturesAsync(selected, _runner);
            StatusMessage = $"Enable features completed: {report.Summary}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error enabling features: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanExecuteAction))]
    private async Task UninstallSelectedFeaturesAsync()
    {
        if (!Tweakora.App.Services.AppAdminGuard.EnsureAdmin("disable Windows features"))
        {
            StatusMessage = "Administrator privileges required to disable features.";
            return;
        }

        var selected = _allFeatures.Where(f => f.IsSelected).ToList();
        if (selected.Count == 0)
        {
            StatusMessage = "No features selected.";
            return;
        }

        IsBusy = true;
        try
        {
            StatusMessage = $"Disabling {selected.Count} Windows feature(s)...";
            var report = await _featureService.DisableFeaturesAsync(selected, _runner);
            StatusMessage = $"Disable features completed: {report.Summary}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error disabling features: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanExecuteAction))]
    private async Task RunFixAsync(string fixKey)
    {
        if (string.Equals(fixKey, "autologon", StringComparison.OrdinalIgnoreCase))
        {
            _fixService.OpenUrl("https://learn.microsoft.com/en-us/sysinternals/downloads/autologon");
            StatusMessage = "Opened Sysinternals AutoLogon page in browser.";
            return;
        }

        if (!Tweakora.App.Services.AppAdminGuard.EnsureAdmin("run system maintenance fixes"))
        {
            StatusMessage = "Administrator privileges required to run fixes.";
            return;
        }

        IsBusy = true;
        try
        {
            StatusMessage = $"Running maintenance fix: {fixKey}...";
            var report = await _fixService.RunFixAsync(fixKey, _runner);
            StatusMessage = $"Fix completed: {report.Summary}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error executing {fixKey}: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void LaunchPanel(LegacyAppletDefinition panel)
    {
        StatusMessage = $"Opening {panel.Name}...";
        _fixService.LaunchApplet(panel.Command);
    }

    [RelayCommand]
    private void PromptOpenSsh()
    {
        IsOpenSshConfirmOpen = true;
    }

    [RelayCommand]
    private async Task ConfirmOpenSshAsync()
    {
        IsOpenSshConfirmOpen = false;
        IsBusy = true;
        try
        {
            StatusMessage = "Configuring OpenSSH Server...";
            bool success = await _featureService.ConfigureOpenSshAsync(true);
            StatusMessage = success ? "OpenSSH Server configured successfully." : "OpenSSH configuration encountered warnings.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error configuring OpenSSH: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void CancelOpenSsh()
    {
        IsOpenSshConfirmOpen = false;
    }

    public List<string> GetSelectedFeatureKeys() => _allFeatures.Where(f => f.IsSelected).Select(f => f.Key).ToList();

    public void SetSelectedFeatureKeys(IEnumerable<string> keys)
    {
        var keySet = new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase);
        foreach (var f in _allFeatures)
        {
            f.IsSelected = keySet.Contains(f.Key);
        }
        UpdateSelectedCount();
    }
}
