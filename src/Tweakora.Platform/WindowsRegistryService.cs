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
        var view = Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Default;
        return hive.ToUpperInvariant() switch
        {
            "HKEY_LOCAL_MACHINE" or "HKLM" => RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view),
            "HKEY_CURRENT_USER" or "HKCU" => RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, view),
            "HKEY_CLASSES_ROOT" or "HKCR" => RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, view),
            "HKEY_USERS" or "HKU" => RegistryKey.OpenBaseKey(RegistryHive.Users, view),
            "HKEY_CURRENT_CONFIG" or "HKCC" => RegistryKey.OpenBaseKey(RegistryHive.CurrentConfig, view),
            _ => throw new ArgumentException($"Unsupported registry hive: {hive}")
        };
    }

    public bool KeyExists(string hive, string key)
    {
        EnsureNotTestMode();
        try
        {
            using var baseKey = GetHiveKey(hive);
            using var subKey = baseKey.OpenSubKey(key, false);
            return subKey != null;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }

    public bool ValueExists(string hive, string key, string valueName)
    {
        EnsureNotTestMode();
        try
        {
            using var baseKey = GetHiveKey(hive);
            using var subKey = baseKey.OpenSubKey(key, false);
            if (subKey == null) return false;
            return subKey.GetValueNames().Contains(valueName, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }

    public object? GetValue(string hive, string key, string valueName)
    {
        EnsureNotTestMode();
        try
        {
            using var baseKey = GetHiveKey(hive);
            using var subKey = baseKey.OpenSubKey(key, false);
            return subKey?.GetValue(valueName);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            return null;
        }
    }

    public string? GetValueKind(string hive, string key, string valueName)
    {
        EnsureNotTestMode();
        try
        {
            using var baseKey = GetHiveKey(hive);
            using var subKey = baseKey.OpenSubKey(key, false);
            if (subKey == null) return null;
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
        try
        {
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
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            throw new UnauthorizedAccessException($"Access denied to {hive}\\{key}\\{valueName}: {ex.Message} (Protected by system or TrustedInstaller).", ex);
        }
    }

    public void DeleteValue(string hive, string key, string valueName)
    {
        EnsureNotTestMode();
        try
        {
            using var baseKey = GetHiveKey(hive, true);
            using var subKey = baseKey.OpenSubKey(key, true);
            subKey?.DeleteValue(valueName, false);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            throw new UnauthorizedAccessException($"Access denied to delete value {hive}\\{key}\\{valueName}: {ex.Message}", ex);
        }
    }

    public void DeleteKey(string hive, string key)
    {
        EnsureNotTestMode();
        try
        {
            using var baseKey = GetHiveKey(hive, true);
            baseKey.DeleteSubKeyTree(key, false);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            throw new UnauthorizedAccessException($"Access denied to delete key {hive}\\{key}: {ex.Message}", ex);
        }
    }

    public void CreateKey(string hive, string key)
    {
        EnsureNotTestMode();
        try
        {
            using var baseKey = GetHiveKey(hive, true);
            using var created = baseKey.CreateSubKey(key, true);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            throw new UnauthorizedAccessException($"Access denied to create key {hive}\\{key}: {ex.Message}", ex);
        }
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
        try
        {
            using var baseKey = GetHiveKey(hive);
            using var subKey = baseKey.OpenSubKey(key, false);
            return subKey?.GetValueNames() ?? Enumerable.Empty<string>();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            return Enumerable.Empty<string>();
        }
    }

    public IEnumerable<string> GetSubKeyNames(string hive, string key)
    {
        EnsureNotTestMode();
        try
        {
            using var baseKey = GetHiveKey(hive);
            using var subKey = baseKey.OpenSubKey(key, false);
            return subKey?.GetSubKeyNames() ?? Enumerable.Empty<string>();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            return Enumerable.Empty<string>();
        }
    }
}

