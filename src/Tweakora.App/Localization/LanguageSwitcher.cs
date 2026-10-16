using System.Diagnostics;
using System.Windows;
using Serilog;
using Tweakora.Core.Interfaces;

namespace Tweakora.App.Localization;

/// <summary>Saves the chosen UI language and restarts WinVelo so every screen uses it.</summary>
public static class LanguageSwitcher
{
    public static void SaveAndRestart(IAppSettingsService settings, string languageCode)
    {
        var s = settings.GetSettings();
        s.Language = LocalizationManager.NormalizeCode(languageCode);
        settings.SaveSettings(s);
        Restart();
    }

    /// <summary>Starts a new WinVelo with the same arguments (minus any --lang override) and closes this one.</summary>
    public static void Restart()
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

            Process.Start(psi);
            Application.Current?.Shutdown();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to restart WinVelo to apply the language.");
        }
    }
}
