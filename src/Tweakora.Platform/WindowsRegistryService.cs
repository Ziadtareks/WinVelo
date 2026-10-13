using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using Tweakora.Core.Interfaces;
using Tweakora.Core.Safety;

namespace Tweakora.Platform;

public sealed class WindowsRegistryService : IRegistryService
{
    public WindowsRegistryService()
    {
        EnsureNotTestMode();
    }

    private static void EnsureNotTestMode()
    {
        if (TestModeManager.IsTestMode)
        {
            throw new InvalidOperationException("Real platform class operation blocked in TEST MODE.");
        }
    }

    private static RegistryKey GetHiveKey(string hive, bool writable = false)
    {
        EnsureNotTestMode();
        return hive.ToUpperInvariant() switch
        {
            "HKEY_LOCAL_MACHINE" or "HKLM" => RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Default),
            "HKEY_CURRENT_USER" or "HKCU" => RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default),
            "HKEY_CLASSES_ROOT" or "HKCR" => RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, RegistryView.Default),
            "HKEY_USERS" or "HKU" => RegistryKey.OpenBaseKey(RegistryHive.Users, RegistryView.Default),
            "HKEY_CURRENT_CONFIG" or "HKCC" => RegistryKey.OpenBaseKey(RegistryHive.CurrentConfig, RegistryView.Default),
            _ => throw new ArgumentException($"Unsupported registry hive: {hive}")
        };
    }

    public bool KeyExists(string hive, string key)
    {
        EnsureNotTestMode();
        using var baseKey = GetHiveKey(hive);
        using var subKey = baseKey.OpenSubKey(key, false);
        return subKey != null;
    }

    public bool ValueExists(string hive, string key, string valueName)
    {
        EnsureNotTestMode();
        using var baseKey = GetHiveKey(hive);
        using var subKey = baseKey.OpenSubKey(key, false);
        if (subKey == null) return false;
        return subKey.GetValueNames().Contains(valueName, StringComparer.OrdinalIgnoreCase);
    }

    public object? GetValue(string hive, string key, string valueName)
    {
        EnsureNotTestMode();
        using var baseKey = GetHiveKey(hive);
        using var subKey = baseKey.OpenSubKey(key, false);
        return subKey?.GetValue(valueName);
    }

    public string? GetValueKind(string hive, string key, string valueName)
    {
        EnsureNotTestMode();
        using var baseKey = GetHiveKey(hive);
        using var subKey = baseKey.OpenSubKey(key, false);
        if (subKey == null) return null;
        try
        {
            var kind = subKey.GetValueKind(valueName);
            return kind switch
            {
                RegistryValueKind.DWord => "DWord",
                RegistryValueKind.QWord => "QWord",
                RegistryValueKind.String => "String",
                RegistryValueKind.ExpandString => "ExpandString",
                RegistryValueKind.MultiString => "MultiString",
                RegistryValueKind.Binary => "Binary",
                _ => "Unknown"
            };
        }
        catch
        {
            return null;
        }
    }

    public void SetValue(string hive, string key, string valueName, object data, string valueKind)
    {
        EnsureNotTestMode();
        using var baseKey = GetHiveKey(hive, true);
        using var subKey = baseKey.CreateSubKey(key, true);
        if (subKey == null) throw new InvalidOperationException($"Could not open or create key {hive}\\{key}");

        RegistryValueKind kind = valueKind switch
        {
            "DWord" => RegistryValueKind.DWord,
            "QWord" => RegistryValueKind.QWord,
            "String" => RegistryValueKind.String,
            "ExpandString" => RegistryValueKind.ExpandString,
            "MultiString" => RegistryValueKind.MultiString,
            "Binary" => RegistryValueKind.Binary,
            _ => RegistryValueKind.DWord
        };

        object realData = data;
        if (kind == RegistryValueKind.DWord && data is not int)
        {
            realData = Convert.ToInt32(data);
        }
        else if (kind == RegistryValueKind.QWord && data is not long)
        {
            realData = Convert.ToInt64(data);
        }

        subKey.SetValue(valueName, realData, kind);
    }

    public void DeleteValue(string hive, string key, string valueName)
    {
        EnsureNotTestMode();
        using var baseKey = GetHiveKey(hive, true);
        using var subKey = baseKey.OpenSubKey(key, true);
        subKey?.DeleteValue(valueName, false);
    }

    public void DeleteKey(string hive, string key)
    {
        EnsureNotTestMode();
        using var baseKey = GetHiveKey(hive, true);
        baseKey.DeleteSubKeyTree(key, false);
    }

    public void CreateKey(string hive, string key)
    {
        EnsureNotTestMode();
        using var baseKey = GetHiveKey(hive, true);
        using var created = baseKey.CreateSubKey(key, true);
    }

    public string ExportRegBackup(string hive, string key, string backupFolder)
    {
        EnsureNotTestMode();
        Directory.CreateDirectory(backupFolder);
        string safeName = $"{hive}_{key}".Replace('\\', '_').Replace('/', '_').Replace(':', '_');
        string backupFile = Path.Combine(backupFolder, $"{safeName}_{DateTime.UtcNow:yyyyMMddHHmmss}.reg");

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "reg.exe",
            Arguments = $"export \"{hive}\\{key}\" \"{backupFile}\" /y",
            CreateNoWindow = true,
            UseShellExecute = false
        });
        process?.WaitForExit(5000);

        return backupFile;
    }

    public IEnumerable<string> GetValueNames(string hive, string key)
    {
        EnsureNotTestMode();
        using var baseKey = GetHiveKey(hive);
        using var subKey = baseKey.OpenSubKey(key, false);
        return subKey?.GetValueNames() ?? Enumerable.Empty<string>();
    }

    public IEnumerable<string> GetSubKeyNames(string hive, string key)
    {
        EnsureNotTestMode();
        using var baseKey = GetHiveKey(hive);
        using var subKey = baseKey.OpenSubKey(key, false);
        return subKey?.GetSubKeyNames() ?? Enumerable.Empty<string>();
    }
}

