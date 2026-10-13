using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tweakora.Core.Fakes;
using Tweakora.Core.Safety;
using Tweakora.Modules.Config;

namespace Tweakora.App.ViewModels;

public sealed partial class ConfigViewModel : ViewModelBase
{
    private readonly WindowsConfigManager _configManager;
    private readonly List<FeatureItemViewModel> _allFeatures = new();

    public ObservableCollection<FeatureItemViewModel> FilteredFeatures { get; } = new();
    public ObservableCollection<LegacyPanelItem> LegacyPanels { get; } = new();
    public ObservableCollection<string> ConfigLogs { get; } = new();

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = "Select Windows features to install or run system maintenance fixes.";

    [ObservableProperty]
    private int _selectedFeaturesCount;

    [ObservableProperty]
    private bool _isOpenSshConfirmOpen;

    public ConfigViewModel()
    {
        var fakeShell = new FakeShellService();
        _configManager = new WindowsConfigManager(fakeShell, isSimulationMode: true);
        _configManager.IsSimulationMode = TestModeManager.IsTestMode;

        _configManager.OnLog += msg =>
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
        var features = _configManager.LoadFeatures();
        foreach (var f in features)
        {
            f.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(FeatureItemViewModel.IsSelected))
                {
                    UpdateSelectedCount();
                }
            };
            _allFeatures.Add(f);
        }

        LegacyPanels.Clear();
        foreach (var panel in _configManager.GetLegacyPanels())
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
    private async Task InstallSelectedFeaturesAsync()
    {
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
            await _configManager.InstallFeaturesAsync(selected);
            StatusMessage = $"Successfully enabled {selected.Count} Windows feature(s).";
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

    [RelayCommand]
    private async Task UninstallSelectedFeaturesAsync()
    {
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
            await _configManager.UninstallFeaturesAsync(selected);
            StatusMessage = $"Successfully disabled {selected.Count} Windows feature(s).";
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

    [RelayCommand]
    private async Task RunFixAsync(string fixKey)
    {
        IsBusy = true;
        try
        {
            StatusMessage = $"Running maintenance action: {fixKey}...";
            bool success = await _configManager.RunFixAsync(fixKey);
            StatusMessage = success
                ? $"Action {fixKey} completed successfully."
                : $"Action {fixKey} reported warnings. Check log.";
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
    private void LaunchPanel(LegacyPanelItem panel)
    {
        StatusMessage = $"Opening {panel.Name}...";
        _configManager.LaunchPanel(panel);
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
            await _configManager.ConfigureOpenSshAsync(true);
            StatusMessage = "OpenSSH Server configured.";
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
