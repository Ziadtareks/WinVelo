using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tweakora.App.Localization;
using Tweakora.App.Services;
using Wpf.Ui.Appearance;

namespace Tweakora.App.ViewModels;

public abstract class ViewModelBase : ObservableObject
{
}

public sealed partial class DashboardViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _status = "System Ready";
}


public sealed partial class ToolboxViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _status = "Tools Available";
}

public sealed partial class AppsViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _status = "WinGet Ready";
}

public sealed partial class PresetsViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _status = "Presets Ready";
}

public sealed partial class SettingsViewModel : ViewModelBase
{
    public LocalizationManager Localization => LocalizationManager.Instance;

    [RelayCommand]
    private void SwitchLanguage(string language)
    {
        LocalizationManager.Instance.SetLanguage(language);
        Serilog.Log.Information("Language changed to {Language}", language);
    }

    [RelayCommand]
    private void SetTheme(string theme)
    {
        if (theme.Equals("Dark", StringComparison.OrdinalIgnoreCase))
        {
            ApplicationThemeManager.Apply(ApplicationTheme.Dark);
        }
        else
        {
            ApplicationThemeManager.Apply(ApplicationTheme.Light);
        }
        Serilog.Log.Information("Theme changed to {Theme}", theme);
    }
}

public sealed partial class MainViewModel : ViewModelBase
{
    [ObservableProperty]
    private object _currentPage;

    [ObservableProperty]
    private bool _isLogPanelOpen;

    public LocalizationManager Localization => LocalizationManager.Instance;
    public UiLogSink LogSink => UiLogSink.Instance;

    public DashboardViewModel DashboardVM { get; }
    public TweaksViewModel TweaksVM { get; }
    public ToolboxViewModel ToolboxVM { get; }
    public AppsViewModel AppsVM { get; }
    public PresetsViewModel PresetsVM { get; }
    public SettingsViewModel SettingsVM { get; }

    public MainViewModel(
        DashboardViewModel dashboardVM,
        TweaksViewModel tweaksVM,
        ToolboxViewModel toolboxVM,
        AppsViewModel appsVM,
        PresetsViewModel presetsVM,
        SettingsViewModel settingsVM)
    {
        DashboardVM = dashboardVM;
        TweaksVM = tweaksVM;
        ToolboxVM = toolboxVM;
        AppsVM = appsVM;
        PresetsVM = presetsVM;
        SettingsVM = settingsVM;

        _currentPage = DashboardVM;
    }

    [RelayCommand]
    private void Navigate(string pageKey)
    {
        CurrentPage = pageKey switch
        {
            "Dashboard" => DashboardVM,
            "Tweaks" => TweaksVM,
            "Toolbox" => ToolboxVM,
            "Apps" => AppsVM,
            "Presets" => PresetsVM,
            "Settings" => SettingsVM,
            _ => DashboardVM
        };
        Serilog.Log.Debug("Navigated to page: {Page}", pageKey);
    }

    [RelayCommand]
    private void ToggleLogPanel()
    {
        IsLogPanelOpen = !IsLogPanelOpen;
    }

    [RelayCommand]
    private void ToggleLanguage()
    {
        string newLang = Localization.CurrentLanguage == "en" ? "ar" : "en";
        Localization.SetLanguage(newLang);
        Serilog.Log.Information("Toggled language to: {Language}", newLang);
    }

    [RelayCommand]
    private void ToggleTheme()
    {
        ApplicationTheme current = ApplicationThemeManager.GetAppTheme();
        ApplicationTheme next = current == ApplicationTheme.Dark ? ApplicationTheme.Light : ApplicationTheme.Dark;
        ApplicationThemeManager.Apply(next);
        Serilog.Log.Information("Toggled theme to: {Theme}", next);
    }
}
