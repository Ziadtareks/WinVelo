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
        ["AppTitle"] = "WinVelo",
        ["NavHome"] = "Home",
        ["NavTweaks"] = "Tweaks",
        ["NavTools"] = "Tools",
        ["NavApps"] = "Apps",
        ["NavHistory"] = "History",
        ["NavPresets"] = "Presets",
        ["NavSettings"] = "Settings",
        ["Theme"] = "Theme",
        ["ThemeLight"] = "Light",
        ["ThemeDark"] = "Dark",
        ["Language"] = "Language",
        ["LanguageEn"] = "English",
        ["StatusReady"] = "Ready. Select a preset or review settings below.",
        ["LogTitle"] = "Activity Log",
        ["SearchPlaceholder"] = "Search tweaks, tools, apps, and actions... (Ctrl+K)",
        ["DashboardWelcome"] = "Welcome to WinVelo",
        ["DashboardSubtitle"] = "Safe, transparent Windows configuration and control.",
        ["TweaksTitle"] = "Windows Configuration",
        ["TweaksSubtitle"] = "Every setting is verified, reversible, and tracked in the rollback journal.",
        ["ToolsTitle"] = "Tools",
        ["ToolsSubtitle"] = "System utilities: Startup, Services, Cleaner, Network, and Fixes.",
        ["AppsTitle"] = "Apps",
        ["AppsSubtitle"] = "Install and update software securely through Windows Package Manager.",
        ["HistoryTitle"] = "History & Rollbacks",
        ["HistorySubtitle"] = "Inspect previous sessions, restore points, and activity logs.",
        ["SettingsTitle"] = "Settings",
        ["SettingsSubtitle"] = "Configure appearance, catalog mode, and safety defaults.",
        ["UndoAllChanges"] = "Undo all WinVelo changes",
        ["GetRecommendations"] = "Get recommendations",
        ["OptimizeStartup"] = "Optimize Startup Apps",
        ["SystemRestoreProtection"] = "System Restore Protection",
        ["SkipSetup"] = "Skip Setup",
        ["NextStep"] = "Next Step",
        ["GetStarted"] = "Get Started",
        ["AboutDescription"] = "WinVelo does not promise unverified performance gains or make hidden modifications. It transparently applies cataloged Windows settings with change previews, rollback journal tracking, and non-destructive reversal."
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
