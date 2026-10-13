using System;
using Microsoft.Win32;

namespace Tweakora.App.Services;

public sealed class SystemInfoSummary
{
    public string OsName { get; init; } = "Windows";
    public string BuildNumber { get; init; } = "Unknown";
    public string Architecture { get; init; } = "64-bit";
    public string MemoryDisplay { get; init; } = "Unknown";
    public string DisplaySummary => $"{OsName} ({BuildNumber})";
}

public static class SystemInfoHelper
{
    public static SystemInfoSummary GetSystemInfo()
    {
        string osName = "Windows";
        string build = Environment.OSVersion.Version.Build.ToString();
        string displayVersion = "";

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            if (key != null)
            {
                var prod = key.GetValue("ProductName")?.ToString();
                var disp = key.GetValue("DisplayVersion")?.ToString();
                var bld = key.GetValue("CurrentBuildNumber")?.ToString();
                var ubr = key.GetValue("UBR")?.ToString();

                if (!string.IsNullOrEmpty(prod)) osName = prod;
                if (!string.IsNullOrEmpty(disp)) displayVersion = disp;
                if (!string.IsNullOrEmpty(bld))
                {
                    build = !string.IsNullOrEmpty(ubr) ? $"{bld}.{ubr}" : bld;
                }
            }
        }
        catch { }

        if (Environment.OSVersion.Version.Build >= 22000 && osName.Contains("Windows 10"))
        {
            osName = osName.Replace("Windows 10", "Windows 11");
        }

        string arch = Environment.Is64BitOperatingSystem ? "64-bit (x64)" : "32-bit";

        string mem = "RAM Info Available";
        try
        {
            var memInfo = GC.GetGCMemoryInfo();
            if (memInfo.TotalAvailableMemoryBytes > 0)
            {
                double gb = memInfo.TotalAvailableMemoryBytes / (1024.0 * 1024 * 1024);
                mem = $"{Math.Round(gb)} GB RAM";
            }
        }
        catch { }

        return new SystemInfoSummary
        {
            OsName = string.IsNullOrEmpty(displayVersion) ? osName : $"{osName} {displayVersion}",
            BuildNumber = $"Build {build}",
            Architecture = arch,
            MemoryDisplay = mem
        };
    }
}
