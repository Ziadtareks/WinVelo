using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace WinVelo.Modules.Startup;

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

    public static (string Color, string Symbol, string? SvgPath) GetBrandIcon(string name, string cleanExePath)
    {
        string n = name.ToLowerInvariant();
        string e = Path.GetFileName(cleanExePath).ToLowerInvariant();

        // 1. Discord
        if (n.Contains("discord") || e.Contains("discord"))
        {
            return ("#5865F2", "Chat24", "M19.54,4.16C18.09,3.49,16.54,2.99,14.92,2.7C14.71,3.08,14.47,3.58,14.3,3.98C12.57,3.72,10.85,3.72,9.15,3.98C8.98,3.58,8.74,3.08,8.53,2.7C6.91,2.99,5.36,3.49,3.91,4.16C1.02,8.47,0.24,12.67,0.62,16.82C2.54,18.23,4.39,19.09,6.21,19.66C6.66,19.05,7.06,18.39,7.4,17.7C6.73,17.45,6.1,17.13,5.5,16.75C5.66,16.63,5.82,16.51,5.97,16.39C9.66,18.09,13.7,18.09,17.36,16.39C17.52,16.51,17.68,16.63,17.84,16.75C17.24,17.13,16.61,17.45,15.94,17.7C16.28,18.39,16.68,19.05,17.13,19.66C18.95,19.09,20.81,18.23,22.72,16.82C23.19,12.02,21.95,7.85,19.54,4.16ZM7.46,14.18C6.36,14.18,5.45,13.17,5.45,11.93C5.45,10.69,6.34,9.68,7.46,9.68C8.6,9.68,9.5,10.69,9.48,11.93C9.48,13.17,8.6,14.18,7.46,14.18ZM15.89,14.18C14.79,14.18,13.88,13.17,13.88,11.93C13.88,10.69,14.77,9.68,15.89,9.68C17.03,9.68,17.93,10.69,17.91,11.93C17.91,13.17,17.03,14.18,15.89,14.18Z");
        }

        // 2. Spotify
        if (n.Contains("spotify") || e.Contains("spotify"))
        {
            return ("#1DB954", "MusicNote224", "M12,2A10,10,0,1,0,22,12,10,10,0,0,0,12,2ZM16.58,16.43a.74.74,0,0,1-1,.25c-2.77-1.69-6.26-2.07-10.37-1.13a.75.75,0,0,1-.33-1.46c4.5-.99,8.38-.56,11.45,1.34A.74.74,0,0,1,16.58,16.43ZM17.9,13.33a.93.93,0,0,1-1.28.31c-3.17-2-8-2.5-11.75-1.37a.93.93,0,1,1-.53-1.78c4.27-1.3,9.59-.72,13.25,1.56A.93.93,0,0,1,17.9,13.33ZM18,10.15C14.36,8,8.33,7.79,4.86,8.84a1.12,1.12,0,1,1-.66-2.14C8,5.48,14.63,5.72,18.84,8.22a1.12,1.12,0,1,1-1.16,1.93Z");
        }

        // 3. Microsoft OneDrive
        if (n.Contains("onedrive") || e.Contains("onedrive"))
        {
            return ("#0078D4", "Cloud24", "M19.35,10.04C18.67,6.59,15.64,4,12,4C9.11,4,6.6,5.64,5.35,8.04C2.34,8.36,0,10.91,0,14c0,3.31,2.69,6,6,6h13c2.76,0,5-2.24,5-5C24,12.36,21.95,10.22,19.35,10.04ZM19,18H6c-2.21,0-4-1.79-4-4c0-2.05,1.53-3.76,3.56-3.97l1.07-0.11l0.5-0.95C8.08,7.14,9.94,6,12,6c2.62,0,4.88,1.86,5.39,4.43l0.3,1.5l1.53,0.11C20.78,12.14,22,13.45,22,15C22,16.65,20.65,18,19,18Z");
        }

        // 4. Microsoft Teams
        if (n.Contains("teams") || e.Contains("teams") || n.Contains("msteams"))
        {
            return ("#5059C9", "PeopleCommunity24", "M16.5,7.5a2,2,0,1,0,2,2A2,2,0,0,0,16.5,7.5ZM13.5,13c-1.1,0-2,.6-2,1.4v2.1h8V14.4c0-.8-.9-1.4-2-1.4ZM8.5,5a3,3,0,1,0,3,3A3,3,0,0,0,8.5,5ZM8.5,13c-2,0-6,1.2-6,3.5V19h12V16.5C14.5,14.2,10.5,13,8.5,13Z");
        }

        // 5. Adobe Creative Cloud
        if (n.Contains("adobe") || e.Contains("adobe") || n.Contains("creative cloud") || e.Contains("creative cloud") || n.Contains("acrobat"))
        {
            return ("#DA1F26", "DesignIdeas24", "M14.5,3L22.5,21h-5.2l-2.5-5.5H9.8L12.4,9.9L13.9,3ZM8.6,18.2L6.9,21H1.5L8.6,3.8V18.2ZM15.4,21h-4L13.4,16.5L15.4,21Z");
        }

        // 6. Steam
        if (n.Contains("steam") || e.Contains("steam"))
        {
            return ("#171A21", "Games24", "M12,2A10,10,0,0,0,2.15,10.88L5.7,12.33a3,3,0,0,1,2.82-.44l2.1-3.07a4,4,0,1,1,4.88,4.88l-3.07,2.1a3,3,0,0,1-3.66,3.37L6.4,21A10,10,0,1,0,12,2ZM15.5,6A2.5,2.5,0,1,0,18,8.5,2.5,2.5,0,0,0,15.5,6ZM7,14a1.5,1.5,0,1,0,1.5,1.5A1.5,1.5,0,0,0,7,14Z");
        }

        // 7. Windows Security / Defender
        if (n.Contains("securityhealth") || e.Contains("securityhealth") || n.Contains("defender") || e.Contains("msascuil"))
        {
            return ("#0078D4", "ShieldCheckmark24", "M12,2L4,5.5v6c0,5.55,3.84,10.74,8,12c4.16-1.26,8-6.45,8-12v-6L12,2ZM10,16.5l-4-4l1.41-1.41L10,13.67l6.59-6.59L18,8.5L10,16.5Z");
        }

        // 8. Realtek Audio
        if (n.Contains("realtek") || e.Contains("rtkaudu") || n.Contains("rtkauduservice") || e.Contains("ravcpl64"))
        {
            return ("#005BAB", "Speaker224", "M3,9v6h4l5,5V4L7,9H3ZM16.5,12A4.5,4.5,0,0,0,14,7.97v8.05A4.47,4.47,0,0,0,16.5,12ZM14,3.23v2.06c2.89.86,5,3.54,5,6.71s-2.11,5.85-5,6.71v2.06c4.01-.91,7-4.49,7-8.77S18.01,4.14,14,3.23Z");
        }

        // 9. 7-Zip
        if (n.Contains("7-zip") || e.Contains("7z") || n.Contains("7zip"))
        {
            return ("#2B579A", "FolderZip24", "M4,4h16v16H4V4ZM6,6v2h12V6H6ZM6,10v2h4v-2H6ZM12,10v2h4v-2H12ZM6,14v4h12v-4H6Z");
        }

        // 10. Epic Games
        if (n.Contains("epicgames") || e.Contains("epicgames") || n.Contains("epic games"))
        {
            return ("#313131", "Games24", "M12,2L3,5v9c0,5,4.5,8,9,9c4.5-1,9-4,9-9V5L12,2ZM15,14H9v-2h5v-2H9V8h6V6H7v10h8V14Z");
        }

        // 11. Google Chrome
        if (n.Contains("chrome") || e.Contains("chrome"))
        {
            return ("#4285F4", "Globe24", null);
        }

        // 12. Microsoft Edge
        if (n.Contains("edge") || e.Contains("msedge"))
        {
            return ("#0078D7", "Globe24", null);
        }

        // 13. Mozilla Firefox
        if (n.Contains("firefox") || e.Contains("firefox"))
        {
            return ("#FF7139", "Globe24", null);
        }

        // 14. Telegram
        if (n.Contains("telegram") || e.Contains("telegram"))
        {
            return ("#26A5E4", "Send24", null);
        }

        // 15. WhatsApp
        if (n.Contains("whatsapp") || e.Contains("whatsapp"))
        {
            return ("#25D366", "Phone24", null);
        }

        // 16. Zoom
        if (n.Contains("zoom") || e.Contains("zoom"))
        {
            return ("#2D8CFF", "VideoChat24", null);
        }

        // 17. Slack
        if (n.Contains("slack") || e.Contains("slack"))
        {
            return ("#4A154B", "Chat24", null);
        }

        // 18. NVIDIA
        if (n.Contains("nvidia") || e.Contains("nvdisplay") || e.Contains("nvtray"))
        {
            return ("#76B900", "Desktop24", null);
        }

        // 19. AMD
        if (n.Contains("amd") || n.Contains("radeon") || e.Contains("amdrsserv"))
        {
            return ("#ED1C24", "Desktop24", null);
        }

        // 20. Intel
        if (n.Contains("intel") || e.Contains("igfxtray") || e.Contains("igfxhk"))
        {
            return ("#0071C5", "Desktop24", null);
        }

        // Default: dynamic palette color with Apps24 symbol
        return (GetIconColor(name), "Apps24", null);
    }
}
