using System;

namespace WinVelo.Core.Utilities;

public static class RegistryPathHelper
{
    public static (string Hive, string Key) Normalize(string hive, string key)
    {
        if (string.IsNullOrWhiteSpace(hive)) return (string.Empty, key ?? string.Empty);
        string h = hive.Trim();
        string k = (key ?? string.Empty).Trim();

        if (h.StartsWith("Registry::", StringComparison.OrdinalIgnoreCase))
        {
            h = h.Substring("Registry::".Length).Trim();
        }
        if (k.StartsWith("Registry::", StringComparison.OrdinalIgnoreCase))
        {
            k = k.Substring("Registry::".Length).Trim();
        }

        if (h.Contains('\\'))
        {
            var parts = h.Split('\\', 2, StringSplitOptions.RemoveEmptyEntries);
            h = parts[0];
            k = string.IsNullOrEmpty(k) ? (parts.Length > 1 ? parts[1] : "") : (parts.Length > 1 ? parts[1] + "\\" + k : k);
        }

        h = h.TrimEnd(':', '\\').ToUpperInvariant();
        h = h switch
        {
            "HKEY_LOCAL_MACHINE" or "HKLM" => "HKLM",
            "HKEY_CURRENT_USER" or "HKCU" => "HKCU",
            "HKEY_CLASSES_ROOT" or "HKCR" => "HKCR",
            "HKEY_USERS" or "HKU" => "HKU",
            "HKEY_CURRENT_CONFIG" or "HKCC" => "HKCC",
            _ => h
        };

        k = System.Text.RegularExpressions.Regex.Replace(k, @"\\+", @"\").Trim('\\');
        return (h, k);
    }

    public static int ParseDWord(object? data)
    {
        if (data == null) return 0;
        if (data is int i) return i;
        if (data is long l) return (int)l;
        if (data is byte b) return b;
        if (data is short s) return s;
        if (data is bool boolean) return boolean ? 1 : 0;
        string str = data.ToString()?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(str)) return 0;
        if (str.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            if (int.TryParse(str.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out int hexVal))
                return hexVal;
        }
        if (int.TryParse(str, out int val)) return val;
        if (long.TryParse(str, out long lVal)) return (int)lVal;
        return 0;
    }

    public static long ParseQWord(object? data)
    {
        if (data == null) return 0L;
        if (data is long l) return l;
        if (data is int i) return i;
        if (data is byte b) return b;
        if (data is short s) return s;
        if (data is bool boolean) return boolean ? 1L : 0L;
        string str = data.ToString()?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(str)) return 0L;
        if (str.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            if (long.TryParse(str.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out long hexVal))
                return hexVal;
        }
        if (long.TryParse(str, out long val)) return val;
        return 0L;
    }
}
