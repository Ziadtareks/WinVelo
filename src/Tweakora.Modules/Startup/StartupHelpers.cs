using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Tweakora.Modules.Startup;

public static class StartupPathHelper
{
    public static string ExtractCleanExePath(string? rawCommand)
    {
        if (string.IsNullOrWhiteSpace(rawCommand)) return string.Empty;

        try
        {
            string expanded = Environment.ExpandEnvironmentVariables(rawCommand.Trim());

            // 1. Quoted path: "C:\path\to\app.exe" --args
            if (expanded.StartsWith('"'))
            {
                int closeQuote = expanded.IndexOf('"', 1);
                if (closeQuote > 1)
                {
                    return expanded.Substring(1, closeQuote - 1).Trim();
                }
            }

            // 2. Check if the entire expanded string is an existing file
            try
            {
                if (File.Exists(expanded))
                {
                    return expanded;
                }
            }
            catch { }

            // 3. Search for .exe, .bat, .cmd, .lnk (case-insensitive)
            string[] extensions = { ".exe", ".bat", ".cmd", ".lnk" };
            foreach (var ext in extensions)
            {
                int extIndex = expanded.IndexOf(ext, StringComparison.OrdinalIgnoreCase);
                if (extIndex >= 0)
                {
                    string candidate = expanded.Substring(0, extIndex + ext.Length).Trim('"', ' ');
                    return candidate;
                }
            }

            // 4. Fallback: split on first space
            int spaceIndex = expanded.IndexOf(' ');
            if (spaceIndex > 0)
            {
                return expanded.Substring(0, spaceIndex).Trim('"', ' ');
            }

            return expanded.Trim('"', ' ');
        }
        catch
        {
            return rawCommand.Trim('"', ' ');
        }
    }
}

public static class StartupApprovedHelper
{
    /// <summary>
    /// Reads the 12-byte Windows Task Manager binary value.
    /// Missing value => Enabled.
    /// Bit 0 of byte 0: 0 = Enabled, 1 = Disabled.
    /// </summary>
    public static bool IsEnabled(byte[]? bytes)
    {
        if (bytes == null || bytes.Length == 0) return true;
        return (bytes[0] & 1) == 0;
    }

    /// <summary>
    /// Generates 12 bytes marking an item as ENABLED in Windows Task Manager / StartupApproved.
    /// </summary>
    public static byte[] CreateEnabledBytes(byte[]? existingBytes = null)
    {
        if (existingBytes != null && existingBytes.Length >= 12)
        {
            byte[] copy = (byte[])existingBytes.Clone();
            copy[0] = 0x02; // Enabled flag (bit 0 cleared)
            return copy;
        }

        return new byte[] { 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
    }

    /// <summary>
    /// Generates 12 bytes marking an item as DISABLED in Windows Task Manager / StartupApproved,
    /// writing the current UTC FILETIME timestamp into bytes 4..11.
    /// </summary>
    public static byte[] CreateDisabledBytes(byte[]? existingBytes = null)
    {
        byte[] disabled = existingBytes != null && existingBytes.Length >= 12
            ? (byte[])existingBytes.Clone()
            : new byte[12];

        disabled[0] = 0x03; // Disabled flag (bit 0 set)
        long fileTime = DateTime.UtcNow.ToFileTimeUtc();
        byte[] timeBytes = BitConverter.GetBytes(fileTime);
        Array.Copy(timeBytes, 0, disabled, 4, 8);
        return disabled;
    }
}

public static class StartupImpactEvaluator
{
    private static readonly HashSet<string> HighImpactKnownNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "discord", "steam", "spotify", "teams", "slack", "epicgames", "epic games",
        "onedrive", "adobe", "creative cloud", "dropbox", "zoom", "skype",
        "battle.net", "ea desktop", "origin", "cortana", "overwolf", "razer",
        "icue", "logitech g hub", "lghub", "viber", "whatsapp"
    };

    private static readonly HashSet<string> EssentialKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "securityhealth", "securityhealthsystray", "windows defender", "msascuil",
        "rtkauduservice", "realtek", "realtek audio", "ravcpl64",
        "nvdisplay", "nvbackend", "nvtray", "nvidia",
        "amd", "radeon", "amdrsserv", "cnext",
        "intel", "igfxtray", "igfxhk", "igfxcfg", "intelaudio",
        "ctfmon", "explorer"
    };

    public static (string Impact, double DelaySeconds) CalculateImpact(string name, string cleanExePath, long fileSizeBytes, string command)
    {
        string nameLower = name.ToLowerInvariant();
        string exeName = Path.GetFileName(cleanExePath).ToLowerInvariant();

        // 1. High Impact: File size > 15MB or known heavy launchers / Electron apps
        if (fileSizeBytes > 15 * 1024 * 1024 ||
            HighImpactKnownNames.Any(k => nameLower.Contains(k) || exeName.Contains(k)))
        {
            return ("High", 1.2);
        }

        // 2. Medium Impact: File size between 2MB and 15MB, OR has > 2 command-line arguments
        int argCount = command.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        if (fileSizeBytes >= 2 * 1024 * 1024 || argCount > 2)
        {
            return ("Medium", 0.5);
        }

        // 3. Low Impact: File size < 2MB, lightweight system helper utilities, or audio/display control panels
        return ("Low", 0.2);
    }

    public static bool IsEssential(string name, string cleanExePath)
    {
        string nameLower = name.ToLowerInvariant();
        string exeName = Path.GetFileName(cleanExePath).ToLowerInvariant();

        return EssentialKeywords.Any(k => nameLower.Contains(k) || exeName.Contains(k));
    }

    private static readonly string[] PaletteColors =
    {
        "#3B82F6", // Blue
        "#10B981", // Emerald
        "#8B5CF6", // Purple
        "#EC4899", // Pink
        "#F59E0B", // Amber
        "#06B6D4", // Cyan
        "#6366F1", // Indigo
        "#14B8A6"  // Teal
    };

    public static string GetIconColor(string name)
    {
        if (string.IsNullOrEmpty(name)) return PaletteColors[0];
        int hash = Math.Abs(name.GetHashCode());
        return PaletteColors[hash % PaletteColors.Length];
    }

    public static string GetIconLetter(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "A";
        string clean = name.Trim();
        return clean.Length >= 2 && char.IsLetterOrDigit(clean[1])
            ? clean.Substring(0, 2).ToUpperInvariant()
            : clean.Substring(0, 1).ToUpperInvariant();
    }
}
