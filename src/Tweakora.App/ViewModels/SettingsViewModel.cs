using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tweakora.App.Services;
using Tweakora.Core.Interfaces;
using Tweakora.Core.Models;
using Tweakora.Modules.Tweaks;
using Wpf.Ui.Appearance;

namespace Tweakora.App.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly IAppSettingsService _appSettings;
    private readonly TweaksViewModel _tweaksVM;
    public Action? RequestOpenWizard { get; set; }

    [ObservableProperty]
    private string _appMode = "Simple";

    [ObservableProperty]
    private bool _isDryRun;

    [ObservableProperty]
    private bool _recommendedOnly = true;

    [ObservableProperty]
    private bool _showExperimental;

    public string VersionDisplay => Tweakora.Core.AppInfo.DisplayVersion;
    public string OsDescription => Environment.OSVersion.VersionString;

    public SettingsViewModel(
        IAppSettingsService appSettings,
        TweaksViewModel tweaksVM)
    {
        _appSettings = appSettings;
        _tweaksVM = tweaksVM;

        var s = _appSettings.GetSettings();
        _appMode = s.AppMode;
        _recommendedOnly = s.RecommendedOnly;
        _showExperimental = s.ShowExperimental;
        _isDryRun = _tweaksVM.IsDryRun;
    }

    [RelayCommand]
    public void SetSimpleMode()
    {
        AppMode = "Simple";
        RecommendedOnly = true;
        ShowExperimental = false;
        Save();
    }

    [RelayCommand]
    public void SetAdvancedMode()
    {
        AppMode = "Advanced";
        Save();
    }

    partial void OnIsDryRunChanged(bool value)
    {
        _tweaksVM.IsDryRun = value;
    }

    partial void OnRecommendedOnlyChanged(bool value)
    {
        _tweaksVM.IsRecommendedOnly = value;
        Save();
    }

    partial void OnShowExperimentalChanged(bool value)
    {
        _tweaksVM.ShowExperimentalTweaks = value;
        Save();
    }

    [RelayCommand]
    public void ToggleTheme()
    {
        var current = ApplicationThemeManager.GetAppTheme();
        var next = current == ApplicationTheme.Dark ? ApplicationTheme.Light : ApplicationTheme.Dark;
        ApplicationThemeManager.Apply(next);
        DesignThemeManager.ApplyTheme(next);
    }

    [RelayCommand]
    public void OpenWizard()
    {
        RequestOpenWizard?.Invoke();
    }

    private void Save()
    {
        var s = _appSettings.GetSettings();
        s.AppMode = AppMode;
        s.RecommendedOnly = RecommendedOnly;
        s.ShowExperimental = ShowExperimental;
        _appSettings.SaveSettings(s);
    }
}
