using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinVelo.Core.Operations;
using WinVelo.Modules.Config;
using WinVelo.App.Localization;

namespace WinVelo.App.ViewModels;

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
    private string _statusMessage = LocalizationManager.T("Select Windows features to install or run system maintenance fixes.");

    [ObservableProperty]
    private bool _hasFailedDetails;

    [ObservableProperty]
    private bool _isDetailsDialogOpen;

    [ObservableProperty]
    private string _detailsDialogTitle = LocalizationManager.T("Execution Details");

    public ObservableCollection<OperationItemResult> ExecutionDetailsItems { get; } = new();

    [RelayCommand]
    public void ShowDetailsDialog()
    {
        if (ExecutionDetailsItems.Count > 0)
        {
            IsDetailsDialogOpen = true;
        }
    }

    [RelayCommand]
    public void CloseDetailsDialog()
    {
        IsDetailsDialogOpen = false;
    }

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
            f.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(FeatureDefinition.IsSelected)) UpdateSelectedCount(); };
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

    [RelayCommand]
    public async Task RefreshFeatureStatesAsync() => await _featureService.RefreshStatusAsync(_allFeatures);

    private bool CanExecuteAction() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanExecuteAction))]
    private async Task InstallSelectedFeaturesAsync()
    {
        if (!WinVelo.App.Services.AppAdminGuard.EnsureAdmin("enable Windows features"))
        {
            StatusMessage = LocalizationManager.T("Administrator privileges required to enable features.");
            return;
        }

        var selected = _allFeatures.Where(f => f.IsSelected).ToList();
        if (selected.Count == 0)
        {
            StatusMessage = LocalizationManager.T("No features selected.");
            return;
        }

        IsBusy = true;
        await Task.Yield();

        try
        {
            StatusMessage = LocalizationManager.T("Enabling {0} Windows feature(s)...", selected.Count);
            var report = await Task.Run(async () => await _featureService.EnableFeaturesAsync(selected, _runner));
            if (report.FailedCount > 0)
            {
                StatusMessage = LocalizationManager.T("Applied {0} of {1} — {2} failed (details)", report.SucceededCount, report.TotalRequested, report.FailedCount);
                HasFailedDetails = true;
                DetailsDialogTitle = LocalizationManager.T("Execution Details — Windows Features");
                ExecutionDetailsItems.Clear();
                foreach (var f in report.Results.Where(r => r.IsFailure)) ExecutionDetailsItems.Add(f);
            }
            else
            {
                StatusMessage = LocalizationManager.T("Enabled all {0} feature(s) successfully.", report.SucceededCount);
                HasFailedDetails = false;
                ExecutionDetailsItems.Clear();
            }
        }
        catch (Exception ex)
        {
            StatusMessage = LocalizationManager.T("Error enabling features: {0}", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanExecuteAction))]
    private async Task UninstallSelectedFeaturesAsync()
    {
        if (!WinVelo.App.Services.AppAdminGuard.EnsureAdmin("disable Windows features"))
        {
            StatusMessage = LocalizationManager.T("Administrator privileges required to disable features.");
            return;
        }

        var selected = _allFeatures.Where(f => f.IsSelected).ToList();
        if (selected.Count == 0)
        {
            StatusMessage = LocalizationManager.T("No features selected.");
            return;
        }

        IsBusy = true;
        await Task.Yield();

        try
        {
            StatusMessage = LocalizationManager.T("Disabling {0} Windows feature(s)...", selected.Count);
            var report = await Task.Run(async () => await _featureService.DisableFeaturesAsync(selected, _runner));
            if (report.FailedCount > 0)
            {
                StatusMessage = LocalizationManager.T("Restored {0} of {1} — {2} failed (details)", report.SucceededCount, report.TotalRequested, report.FailedCount);
                HasFailedDetails = true;
                DetailsDialogTitle = LocalizationManager.T("Execution Details — Windows Features");
                ExecutionDetailsItems.Clear();
                foreach (var f in report.Results.Where(r => r.IsFailure)) ExecutionDetailsItems.Add(f);
            }
            else
            {
                StatusMessage = LocalizationManager.T("Disabled all {0} feature(s) successfully.", report.SucceededCount);
                HasFailedDetails = false;
                ExecutionDetailsItems.Clear();
            }
        }
        catch (Exception ex)
        {
            StatusMessage = LocalizationManager.T("Error disabling features: {0}", ex.Message);
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
            StatusMessage = LocalizationManager.T("Opened Sysinternals AutoLogon page in browser.");
            return;
        }

        if (!WinVelo.App.Services.AppAdminGuard.EnsureAdmin("run system maintenance fixes"))
        {
            StatusMessage = LocalizationManager.T("Administrator privileges required to run fixes.");
            return;
        }

        IsBusy = true;
        await Task.Yield();

        try
        {
            StatusMessage = LocalizationManager.T("Running maintenance fix: {0}...", fixKey);
            var report = await Task.Run(async () => await _fixService.RunFixAsync(fixKey, _runner));
            if (report.FailedCount > 0)
            {
                StatusMessage = LocalizationManager.T("Fix {0} failed (details)", fixKey);
                HasFailedDetails = true;
                DetailsDialogTitle = LocalizationManager.T("Execution Details — Fix: {0}", fixKey);
                ExecutionDetailsItems.Clear();
                foreach (var f in report.Results.Where(r => r.IsFailure)) ExecutionDetailsItems.Add(f);
            }
            else
            {
                StatusMessage = LocalizationManager.T("Fix {0} completed successfully.", fixKey);
                HasFailedDetails = false;
                ExecutionDetailsItems.Clear();
            }
        }
        catch (Exception ex)
        {
            StatusMessage = LocalizationManager.T("Error executing {0}: {1}", fixKey, ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void LaunchPanel(LegacyAppletDefinition panel)
    {
        StatusMessage = LocalizationManager.T("Opening {0}...", panel.Name);
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
        if (!WinVelo.App.Services.AppAdminGuard.EnsureAdmin("configure OpenSSH Server")) return;
        IsBusy = true;
        try
        {
            StatusMessage = LocalizationManager.T("Configuring OpenSSH Server...");
            bool success = await _featureService.ConfigureOpenSshAsync(true);
            StatusMessage = success ? LocalizationManager.T("OpenSSH Server configured successfully.") : LocalizationManager.T("OpenSSH configuration encountered warnings.");
        }
        catch (Exception ex)
        {
            StatusMessage = LocalizationManager.T("Error configuring OpenSSH: {0}", ex.Message);
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
