using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;

namespace WinVelo.App.Localization;

/// <summary>
/// Supplies UI text in the selected language (English or Arabic).
/// XAML uses short keys through {loc:Tr Key}; view models call <see cref="T(string)"/> with the English
/// text itself, so English always works even when a translation is missing.
/// The language is chosen once at startup (Settings › Language applies it after a restart).
/// </summary>
public sealed class LocalizationManager : INotifyPropertyChanged
{
    private static readonly Lazy<LocalizationManager> _instance = new(() => new LocalizationManager());
    public static LocalizationManager Instance => _instance.Value;

    public static IReadOnlyList<LanguageOption> SupportedLanguages { get; } = new[]
    {
        new LanguageOption("en", "English"),
        new LanguageOption("ar", "العربية"),
    };

    private readonly Dictionary<string, string> _english;
    private Dictionary<string, string> _current;
    private string _currentLanguage = "en";

    private LocalizationManager()
    {
        _english = LoadLanguage("en");
        _current = _english;
    }

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
                OnPropertyChanged(nameof(IsRightToLeft));
                OnPropertyChanged(nameof(FlowDirection));
                OnPropertyChanged(nameof(DirectionalIconScaleX));
                OnPropertyChanged("Item[]");
            }
        }
    }

    public bool IsRightToLeft => _currentLanguage == "ar";

    public FlowDirection FlowDirection => IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

    /// <summary>-1 in right-to-left languages. Font icons that point a direction (arrows) are not mirrored by WPF,
    /// so they use this as a ScaleX to point the right way.</summary>
    public double DirectionalIconScaleX => IsRightToLeft ? -1 : 1;

    /// <summary>Text for a XAML key; falls back to English, then to the key itself.</summary>
    public string this[string key]
    {
        get
        {
            if (_current.TryGetValue(key, out string? val)) return val;
            if (_english.TryGetValue(key, out string? en)) return en;
            return key;
        }
    }

    /// <summary>Translates an English UI sentence; returns it unchanged when no translation exists.</summary>
    public static string T(string english)
    {
        if (string.IsNullOrEmpty(english)) return english;
        var mgr = Instance;
        return mgr._current.TryGetValue(english, out string? val) ? val : english;
    }

    /// <summary>
    /// Translates a finished English message produced from a pattern, for example
    /// "Could not stop service 'Spooler': Access is denied." for the pattern "Could not stop service '{0}': {1}".
    /// The values are kept as they are. Returns the text unchanged when no pattern matches.
    /// </summary>
    public static string TranslateMessage(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? string.Empty;
        var mgr = Instance;
        if (mgr._current.TryGetValue(text, out string? exact)) return exact;
        if (ReferenceEquals(mgr._current, mgr._english)) return text;

        foreach (var (regex, key) in mgr.Patterns())
        {
            var m = regex.Match(text);
            if (!m.Success) continue;
            string result = mgr._current[key];
            for (int i = 1; i < m.Groups.Count; i++)
            {
                result = result.Replace("{" + (i - 1) + "}", m.Groups[i].Value, StringComparison.Ordinal);
            }
            return result;
        }
        return text;
    }

    private List<(System.Text.RegularExpressions.Regex Regex, string Key)>? _patterns;
    private Dictionary<string, string>? _patternsFor;

    private List<(System.Text.RegularExpressions.Regex, string)> Patterns()
    {
        if (_patterns != null && ReferenceEquals(_patternsFor, _current)) return _patterns;
        var list = new List<(System.Text.RegularExpressions.Regex, string)>();
        foreach (string key in _current.Keys)
        {
            if (!key.Contains("{0}", StringComparison.Ordinal)) continue;
            string pattern = "^" + System.Text.RegularExpressions.Regex.Escape(key) + "$";
            for (int i = 0; i < 10; i++)
            {
                pattern = pattern.Replace("\\{" + i + "}", "(.*?)", StringComparison.Ordinal);
            }
            list.Add((new System.Text.RegularExpressions.Regex(pattern, System.Text.RegularExpressions.RegexOptions.Singleline), key));
        }
        _patternsFor = _current;
        return _patterns = list;
    }

    /// <summary>Translates an English format string such as "Applied {0} of {1}" and fills in the values.</summary>
    public static string T(string englishFormat, params object?[] args)
    {
        string format = T(englishFormat);
        try
        {
            return string.Format(CultureInfo.CurrentCulture, format, args);
        }
        catch (FormatException)
        {
            return string.Format(CultureInfo.CurrentCulture, englishFormat, args);
        }
    }

    public void SetLanguage(string? languageCode)
    {
        string code = NormalizeCode(languageCode);
        _current = code == "en" ? _english : LoadLanguage(code);
        CurrentLanguage = code;

        try
        {
            CultureInfo ui = new(code == "ar" ? "ar-EG" : "en-US");
            CultureInfo.DefaultThreadCurrentUICulture = ui;
            Thread.CurrentThread.CurrentUICulture = ui;
        }
        catch
        {
        }
    }

    public static string NormalizeCode(string? languageCode)
    {
        string code = (languageCode ?? "en").Trim().ToLowerInvariant();
        return SupportedLanguages.Any(l => l.Code == code) ? code : "en";
    }

    private static Dictionary<string, string> LoadLanguage(string code)
    {
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            string? resource = asm.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith($"Lang-{code}.json", StringComparison.OrdinalIgnoreCase));
            if (resource == null) return dict;

            using Stream? stream = asm.GetManifestResourceStream(resource);
            if (stream == null) return dict;
            var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(stream);
            if (parsed != null)
            {
                foreach (var kv in parsed) dict[kv.Key] = kv.Value;
            }
        }
        catch
        {
        }
        return dict;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed record LanguageOption(string Code, string DisplayName)
{
    public override string ToString() => DisplayName;
}
