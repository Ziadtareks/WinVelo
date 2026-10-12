using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;

namespace Tweakora.App.Localization;

public sealed class LocalizationManager : INotifyPropertyChanged
{
    private static readonly Lazy<LocalizationManager> _instance = new(() => new LocalizationManager());
    public static LocalizationManager Instance => _instance.Value;

    private string _currentLanguage = "en";
    private FlowDirection _currentFlowDirection = FlowDirection.LeftToRight;

    private readonly Dictionary<string, string> _enStrings = new(StringComparer.OrdinalIgnoreCase)
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
        ["LanguageAr"] = "العربية",
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
        ["SettingsSubtitle"] = "Configure language, theme, and safety defaults."
    };

    private readonly Dictionary<string, string> _arStrings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AppTitle"] = "تويكورا - مركز التحكم في ويندوز",
        ["NavHome"] = "لوحة التحكم",
        ["NavTweaks"] = "التعديلات",
        ["NavTools"] = "صندوق الأدوات",
        ["NavApps"] = "مثبت البرامج",
        ["NavPresets"] = "الإعدادات المسبقة",
        ["NavSettings"] = "الإعدادات",
        ["Theme"] = "المظهر",
        ["ThemeLight"] = "فاتح",
        ["ThemeDark"] = "داكن",
        ["Language"] = "اللغة",
        ["LanguageEn"] = "English",
        ["LanguageAr"] = "العربية",
        ["StatusReady"] = "جاهز",
        ["LogTitle"] = "سجل النشاط",
        ["SearchPlaceholder"] = "ابحث عن التعديلات والأدوات والإعدادات...",
        ["DashboardWelcome"] = "مرحبًا بك في تويكورا",
        ["DashboardSubtitle"] = "مركز آمن وقابل للاسترجاع لتحسين والتحكم في ويندوز 10 و 11.",
        ["TweaksTitle"] = "تعديلات ويندوز",
        ["TweaksSubtitle"] = "كل تعديل قابل للتراجع ومحفوظ في سجل الاسترجاع المسبق.",
        ["ToolsTitle"] = "صندوق الأدوات المدمج",
        ["ToolsSubtitle"] = "أدوات النظام: المنظف الذكي، بدء التشغيل، الخدمات، إلغاء التثبيت، والشبكة.",
        ["AppsTitle"] = "مثبت البرامج (WinGet)",
        ["AppsSubtitle"] = "تثبيت وتحديث البرامج بأمان عبر مدير حزم ويندوز الرسمي.",
        ["PresetsTitle"] = "الإعدادات المسبقة",
        ["PresetsSubtitle"] = "تطبيق إعدادات مخصصة ومختبرة للموصى به، الألعاب، أو الخصوصية.",
        ["SettingsTitle"] = "التفضيلات وإعدادات النسخ الاحتياطي",
        ["SettingsSubtitle"] = "تخصيص اللغة، المظهر، وخيارات الأمان الافتراضية."
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
                OnPropertyChanged(nameof(CurrentFlowDirection));
                OnPropertyChanged("Item[]");
            }
        }
    }

    public FlowDirection CurrentFlowDirection
    {
        get => _currentFlowDirection;
        private set
        {
            if (_currentFlowDirection != value)
            {
                _currentFlowDirection = value;
                OnPropertyChanged();
            }
        }
    }

    public string this[string key]
    {
        get
        {
            if (_currentLanguage.Equals("ar", StringComparison.OrdinalIgnoreCase))
            {
                if (_arStrings.TryGetValue(key, out string? arVal))
                    return arVal;
            }

            if (_enStrings.TryGetValue(key, out string? enVal))
                return enVal;

            return key;
        }
    }

    public void SetLanguage(string languageCode)
    {
        string normalized = languageCode.StartsWith("ar", StringComparison.OrdinalIgnoreCase) ? "ar" : "en";
        CurrentLanguage = normalized;
        CurrentFlowDirection = normalized == "ar" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

        CultureInfo culture = new(normalized == "ar" ? "ar-SA" : "en-US");
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
