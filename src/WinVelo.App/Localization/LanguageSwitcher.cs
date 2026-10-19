using System.Diagnostics;
using System.Windows;
using Serilog;
using WinVelo.Core.Interfaces;

namespace WinVelo.App.Localization;

/// <summary>Saves the chosen UI language and restarts WinVelo so every screen uses it.</summary>
public static class LanguageSwitcher
{
    public static void SaveAndRestart(IAppSettingsService settings, string languageCode)
    {
        var s = settings.GetSettings();
        s.Language = LocalizationManager.NormalizeCode(languageCode);
        settings.SaveSettings(s);
        Restart(s.Language);
    }

    /// <summary>
    /// Starts a new WinVelo with the same arguments plus "--lang &lt;code&gt;" and closes this one.
    /// The language is passed explicitly because test mode starts with fresh settings on every launch.
    /// </summary>
    public static void Restart(string languageCode)
    {
        try
        {
            var args = Environment.GetCommandLineArgs().Skip(1).ToList();
            int idx = args.FindIndex(a => string.Equals(a, "--lang", StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
            {
                args.RemoveAt(idx);
                if (idx < args.Count) args.RemoveAt(idx);
            }

            var psi = new ProcessStartInfo
            {
                FileName = Environment.ProcessPath ?? "WinVelo.exe",
                UseShellExecute = true
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            psi.ArgumentList.Add("--lang");
            psi.ArgumentList.Add(LocalizationManager.NormalizeCode(languageCode));

            Process.Start(psi);
            Application.Current?.Shutdown();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to restart WinVelo to apply the language.");
        }
    }
}
