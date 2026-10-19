using System;
using System.IO;
using System.Text.Json;
using WinVelo.Core.Interfaces;
using WinVelo.Core.Models;
using WinVelo.Core.Safety;

namespace WinVelo.Platform;

public sealed class AppSettingsService : IAppSettingsService
{
    private readonly string _settingsFilePath;
    private UserSettings _cachedSettings;

    public AppSettingsService(string? customPath = null)
    {
        _settingsFilePath = customPath ?? WinVeloStoragePaths.GetSettingsPath(TestModeManager.IsTestMode);

        _cachedSettings = LoadSettings();
    }

    public UserSettings GetSettings() => _cachedSettings;

    public void SaveSettings(UserSettings settings)
    {
        _cachedSettings = settings ?? new UserSettings();
        try
        {
            string? dir = Path.GetDirectoryName(_settingsFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string json = JsonSerializer.Serialize(_cachedSettings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_settingsFilePath, json);
        }
        catch
        {
        }
    }

    private UserSettings LoadSettings()
    {
        try
        {
            if (File.Exists(_settingsFilePath))
            {
                string json = File.ReadAllText(_settingsFilePath);
                var settings = JsonSerializer.Deserialize<UserSettings>(json);
                if (settings != null) return settings;
            }
        }
        catch
        {
        }

        return new UserSettings();
    }
}
