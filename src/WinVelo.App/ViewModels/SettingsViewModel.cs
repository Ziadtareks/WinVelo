using System;
using System.Diagnostics;
using System.Windows;
using Serilog;
using WinVelo.App.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinVelo.App.Services;
using WinVelo.Core.Interfaces;
using WinVelo.Core.Models;
using WinVelo.Modules.Tweaks;
using Wpf.Ui.Appearance;

namespace WinVelo.App.ViewModels;

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

    public string VersionDisplay => WinVelo.Core.AppInfo.DisplayVersion;

    public IReadOnlyList<LanguageOption> Languages => LocalizationManager.SupportedLanguages;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLanguageRestartNeeded))]
    private LanguageOption? _selectedLanguage;

    /// <summary>True when the chosen language differs from the one the app started with.</summary>
    public bool IsLanguageRestartNeeded =>
        SelectedLanguage != null && SelectedLanguage.Code != LocalizationManager.Instance.CurrentLanguage;

    public bool IsSimpleMode => !string.Equals(AppMode, "Advanced", StringComparison.OrdinalIgnoreCase);
    public bool IsAdvancedMode => string.Equals(AppMode, "Advanced", StringComparison.OrdinalIgnoreCase);
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

        // Show the language the app is running in right now (it can differ from the saved one with --lang).
        string running = LocalizationManager.Instance.CurrentLanguage;
        _selectedLanguage = Languages.FirstOrDefault(l => l.Code == running) ?? Languages[0];
    }

    partial void OnAppModeChanged(string value)
    {
        OnPropertyChanged(nameof(IsSimpleMode));
        OnPropertyChanged(nameof(IsAdvancedMode));
    }

    partial void OnSelectedLanguageChanged(LanguageOption? value)
    {
        if (value == null) return;
        var s = _appSettings.GetSettings();
        s.Language = value.Code;
        _appSettings.SaveSettings(s);
    }

    /// <summary>Restarts WinVelo so the new language is applied everywhere.</summary>
    [RelayCommand]
    public void RestartToApplyLanguage() => LanguageSwitcher.Restart(SelectedLanguage?.Code ?? "en");

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
