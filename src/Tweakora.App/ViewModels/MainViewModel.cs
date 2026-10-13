using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tweakora.App.Localization;
using Tweakora.App.Services;
using Tweakora.Core.Engine;
using Tweakora.Core.Profiles;
using Tweakora.Core.Safety;
using Wpf.Ui.Appearance;

namespace Tweakora.App.ViewModels;

public sealed class PaletteItem
{
    public required string Title { get; init; }
    public required string Subtitle { get; init; }
    public required string Category { get; init; } // "Tab", "Action", "Tool"
    public string IconSymbol { get; init; } = "Search24";
    public required Action ActionToExecute { get; init; }
}

public sealed partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    private object _currentPage;

    [ObservableProperty]
    private string _activeTab = "Install"; // "Install", "Tweaks", "Config", "Updates", "Toolbox"

    [ObservableProperty]
    private string _topSearchText = string.Empty;

    [ObservableProperty]
    private bool _isLogPanelOpen;

    [ObservableProperty]
    private bool _isCommandPaletteOpen;

    [ObservableProperty]
    private bool _isAboutDialogOpen;

    [ObservableProperty]
    private bool _isConfigDialogOpen;

    [ObservableProperty]
    private string _configDialogText = string.Empty;

    [ObservableProperty]
    private string _paletteQuery = string.Empty;

    [ObservableProperty]
    private string _currentThemeName = "Dark";

    public LocalizationManager Localization => LocalizationManager.Instance;
    public UiLogSink LogSink => UiLogSink.Instance;

    public bool IsTestModeActive => TestModeManager.IsTestMode;
    public bool IsAdministrator { get; } = AdminPrivilegeHelper.IsRunningAsAdministrator();
    public bool IsMissingAdministratorPrivileges => !IsAdministrator;

    public InstallViewModel InstallVM { get; }
    public TweaksViewModel TweaksVM { get; }
    public ConfigViewModel ConfigVM { get; }
    public UpdatesViewModel UpdatesVM { get; }
    public ToolboxViewModel ToolboxVM { get; }

    private readonly List<PaletteItem> _allPaletteItems = new();
    public ObservableCollection<PaletteItem> PaletteResults { get; } = new();

    public MainViewModel(
        InstallViewModel installVM,
        TweaksViewModel tweaksVM,
        ConfigViewModel configVM,
        UpdatesViewModel updatesVM,
        ToolboxViewModel toolboxVM)
    {
        InstallVM = installVM;
        TweaksVM = tweaksVM;
        ConfigVM = configVM;
        UpdatesVM = updatesVM;
        ToolboxVM = toolboxVM;

        _currentPage = InstallVM;
        _activeTab = "Install";

        InitializePalette();
    }

    partial void OnTopSearchTextChanged(string value)
    {
        switch (ActiveTab)
        {
            case "Install":
                InstallVM.SetSearchQuery(value);
                break;
            case "Tweaks":
                TweaksVM.SetSearchQuery(value);
                break;
            case "Config":
                ConfigVM.SetSearchQuery(value);
                break;
        }
    }

    [RelayCommand]
    public void SwitchTab(string tabName)
    {
        ActiveTab = tabName;
        CurrentPage = tabName switch
        {
            "Install" => InstallVM,
            "Tweaks" => TweaksVM,
            "Config" => ConfigVM,
            "Updates" => UpdatesVM,
            "Toolbox" => ToolboxVM,
            _ => InstallVM
        };

        OnTopSearchTextChanged(TopSearchText);
        Serilog.Log.Debug("Switched active tab to: {Tab}", tabName);
    }

    [RelayCommand]
    public void ExportConfig()
    {
        var config = new TweakoraConfig
        {
            SelectedApps = InstallVM.GetSelectedAppKeys(),
            SelectedTweaks = TweaksVM.GetSelectedTweakIds(),
            SelectedFeatures = ConfigVM.GetSelectedFeatureKeys(),
            SelectedDns = TweaksVM.SelectedDns?.Name,
            UpdatesMode = UpdatesVM.GetSelectedModeString()
        };

        ConfigDialogText = ConfigManager.ExportConfigToJson(config);
        IsConfigDialogOpen = true;
    }

    [RelayCommand]
    public void ImportConfig(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return;

        var config = ConfigManager.ImportConfigFromJson(json);
        if (config != null)
        {
            InstallVM.SetSelectedAppKeys(config.SelectedApps);
            TweaksVM.SetSelectedTweakIds(config.SelectedTweaks);
            ConfigVM.SetSelectedFeatureKeys(config.SelectedFeatures);
            if (!string.IsNullOrWhiteSpace(config.UpdatesMode))
            {
                UpdatesVM.SetSelectedModeString(config.UpdatesMode);
            }
            IsConfigDialogOpen = false;
        }
    }

    [RelayCommand]
    public void CloseConfigDialog()
    {
        IsConfigDialogOpen = false;
    }

    [RelayCommand]
    public void OpenAboutDialog()
    {
        IsAboutDialogOpen = true;
    }

    [RelayCommand]
    public void CloseAboutDialog()
    {
        IsAboutDialogOpen = false;
    }

    [RelayCommand]
    public void RestartAsAdmin()
    {
        AdminPrivilegeHelper.RestartAsAdministrator();
    }

    private void InitializePalette()
    {
        _allPaletteItems.Add(new PaletteItem { Title = "Install Tab", Subtitle = "Browse and install curated Windows applications", Category = "Tab", IconSymbol = "Apps24", ActionToExecute = () => SwitchTab("Install") });
        _allPaletteItems.Add(new PaletteItem { Title = "Tweaks Tab", Subtitle = "Essential, Advanced, and Preference Windows tweaks", Category = "Tab", IconSymbol = "Wrench24", ActionToExecute = () => SwitchTab("Tweaks") });
        _allPaletteItems.Add(new PaletteItem { Title = "Config Tab", Subtitle = "Windows Features, Fixes, and 14 Legacy Control Panels", Category = "Tab", IconSymbol = "Settings24", ActionToExecute = () => SwitchTab("Config") });
        _allPaletteItems.Add(new PaletteItem { Title = "Updates Tab", Subtitle = "Default, Security, and Update Management policies", Category = "Tab", IconSymbol = "ArrowSync24", ActionToExecute = () => SwitchTab("Updates") });
        _allPaletteItems.Add(new PaletteItem { Title = "Toolbox Tab", Subtitle = "Startup, Services, Cleaner, Fixes, and DNS", Category = "Tab", IconSymbol = "Toolbox24", ActionToExecute = () => SwitchTab("Toolbox") });
        _allPaletteItems.Add(new PaletteItem { Title = "Export Configuration", Subtitle = "Export current selections to portable JSON", Category = "Action", IconSymbol = "ArrowDownload24", ActionToExecute = ExportConfig });
        _allPaletteItems.Add(new PaletteItem { Title = "Toggle Dark/Light Theme", Subtitle = "Switch application appearance theme", Category = "Action", IconSymbol = "DarkTheme24", ActionToExecute = ToggleTheme });
        _allPaletteItems.Add(new PaletteItem { Title = "Toggle Activity Log", Subtitle = "Show or hide live debug log sink drawer", Category = "Action", IconSymbol = "DocumentBulletList24", ActionToExecute = ToggleLogPanel });

        FilterPalette();
    }

    partial void OnPaletteQueryChanged(string value)
    {
        FilterPalette();
    }

    private void FilterPalette()
    {
        PaletteResults.Clear();
        string q = PaletteQuery?.Trim() ?? string.Empty;

        foreach (var item in _allPaletteItems)
        {
            if (string.IsNullOrWhiteSpace(q)
                || item.Title.Contains(q, StringComparison.OrdinalIgnoreCase)
                || item.Subtitle.Contains(q, StringComparison.OrdinalIgnoreCase)
                || item.Category.Contains(q, StringComparison.OrdinalIgnoreCase))
            {
                PaletteResults.Add(item);
            }
        }
    }

    [RelayCommand]
    private void ToggleCommandPalette()
    {
        IsCommandPaletteOpen = !IsCommandPaletteOpen;
        if (IsCommandPaletteOpen)
        {
            PaletteQuery = string.Empty;
            FilterPalette();
        }
    }

    [RelayCommand]
    private void ExecutePaletteItem(PaletteItem item)
    {
        IsCommandPaletteOpen = false;
        item.ActionToExecute();
    }

    [RelayCommand]
    private void ToggleLogPanel()
    {
        IsLogPanelOpen = !IsLogPanelOpen;
    }

    [RelayCommand]
    private void ExecuteFirstPaletteItem()
    {
        if (PaletteResults.Count > 0)
        {
            ExecutePaletteItem(PaletteResults[0]);
        }
    }

    [RelayCommand]
    private void ToggleTheme()
    {
        ApplicationTheme current = ApplicationThemeManager.GetAppTheme();
        ApplicationTheme next = current == ApplicationTheme.Dark ? ApplicationTheme.Light : ApplicationTheme.Dark;
        ApplicationThemeManager.Apply(next);
        CurrentThemeName = next.ToString();
        Serilog.Log.Information("Toggled theme to: {Theme}", next);
    }
}
