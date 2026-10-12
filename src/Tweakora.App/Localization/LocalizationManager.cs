using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Tweakora.App.Localization;

public sealed class LocalizationManager : INotifyPropertyChanged
{
    private static readonly Lazy<LocalizationManager> _instance = new(() => new LocalizationManager());
    public static LocalizationManager Instance => _instance.Value;

    private string _currentLanguage = "en";

    private readonly Dictionary<string, string> _strings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AppTitle"] = "Tweakora - Windows Control Center",
        ["NavHome"] = "Dashboard",
        ["NavTweaks"] = "Tweaks",
        ["NavTools"] = "Toolbox",
        ["NavApps"] = "App Installer",
        ["NavPresets"] = "Presets",
        ["NavSettings"] = "Settings",
        ["Theme"] = "Theme",
        ["ThemeLight"] = "Light",
        ["ThemeDark"] = "Dark",
        ["Language"] = "Language",
        ["LanguageEn"] = "English",
        ["StatusReady"] = "Ready",
        ["LogTitle"] = "Activity Log",
        ["SearchPlaceholder"] = "Search tweaks, tools, and settings...",
        ["DashboardWelcome"] = "Welcome to Tweakora",
        ["DashboardSubtitle"] = "Safe, reversible Windows 10/11 optimization and control center.",
        ["TweaksTitle"] = "Windows Tweaks",
        ["TweaksSubtitle"] = "Each tweak is reversible and backed up in the Rollback Journal.",
        ["ToolsTitle"] = "Built-in Toolbox",
        ["ToolsSubtitle"] = "System tools: Cleaner, Startup, Services, Uninstaller, and Network.",
        ["AppsTitle"] = "App Installer (WinGet)",
        ["AppsSubtitle"] = "Install and update software securely through Windows Package Manager.",
        ["PresetsTitle"] = "System Presets",
        ["PresetsSubtitle"] = "Apply tested configurations tailored for Recommended, Gaming, or Privacy.",
        ["SettingsTitle"] = "Preferences & Backup Settings",
        ["SettingsSubtitle"] = "Configure appearance, experience mode, and safety defaults."
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    public string CurrentLanguage
    {
        get => _currentLanguage;
        private set
        {
            if (_currentLanguage != value)
            {
                _currentLanguage = value;
                OnPropertyChanged();
                OnPropertyChanged("Item[]");
            }
        }
    }

    public string this[string key]
    {
        get
        {
            if (_strings.TryGetValue(key, out string? val))
                return val;

            return key;
        }
    }

    public void SetLanguage(string languageCode)
    {
        CurrentLanguage = languageCode;
        CultureInfo culture = new(languageCode.Equals("en", StringComparison.OrdinalIgnoreCase) ? "en-US" : languageCode);
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
