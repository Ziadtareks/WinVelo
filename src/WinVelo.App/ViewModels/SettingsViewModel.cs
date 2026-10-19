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
using WinVelo.Core.Updates;
using WinVelo.Modules.Tweaks;
using Wpf.Ui.Appearance;

namespace WinVelo.App.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly IAppSettingsService _appSettings;
    private readonly TweaksViewModel _tweaksVM;
    private readonly IUpdateChecker _updateChecker;
    private readonly IShellService? _shell;
    private string? _releaseUrl;
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

    [ObservableProperty]
    private string _updateStatus = string.Empty;

    [ObservableProperty]
    private bool _updateAvailable;

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
        TweaksViewModel tweaksVM,
        IUpdateChecker updateChecker,
        IShellService? shell = null)
    {
        _appSettings = appSettings;
        _tweaksVM = tweaksVM;
        _updateChecker = updateChecker;
        _shell = shell;

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

    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        UpdateAvailable = false;
        _releaseUrl = null;
        UpdateStatus = LocalizationManager.T("Checking for updates...");

        try
        {
            UpdateCheckResult result = await _updateChecker.CheckAsync(WinVelo.Core.AppInfo.Version);
            switch (result.Status)
            {
                case UpdateCheckStatus.UpdateAvailable:
                    _releaseUrl = result.ReleaseUrl;
                    UpdateAvailable = true;
                    UpdateStatus = LocalizationManager.T("{0} is available.", result.LatestVersion ?? string.Empty);
                    break;

                case UpdateCheckStatus.UpToDate:
                    UpdateStatus = LocalizationManager.T("You are running the latest version ({0}).", WinVelo.Core.AppInfo.DisplayVersion);
                    break;

                default:
                    UpdateStatus = LocalizationManager.T("Could not check for updates. Check your connection and try again.");
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Update check failed");
            UpdateStatus = LocalizationManager.T("Could not check for updates. Check your connection and try again.");
        }
    }

    [RelayCommand]
    private void OpenReleasePage()
    {
        if (_shell != null && !string.IsNullOrWhiteSpace(_releaseUrl))
        {
            _shell.OpenUrl(_releaseUrl);
        }
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
