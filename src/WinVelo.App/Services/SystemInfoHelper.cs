using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace WinVelo.App.Services;

public sealed record RamMetrics(
    double TotalGb,
    double UsedGb,
    double AvailableGb,
    int UsagePercent,
    string DisplayText,
    string AvailableText);

public sealed class SystemInfoSummary
{
    public string OsName { get; init; } = "Windows";
    public string BuildNumber { get; init; } = "Unknown";
    public string Architecture { get; init; } = "64-bit";
    public string MemoryDisplay { get; init; } = "Unknown";
    public string Hostname { get; init; } = Environment.MachineName;
    public string ProcessorName { get; init; } = "Multi-Core Processor";
    public double TotalPhysicalRamGb { get; init; } = 16.0;
    public double UsedPhysicalRamGb { get; init; } = 4.0;
    public double AvailablePhysicalRamGb { get; init; } = 12.0;
    public int MemoryLoadPercent { get; init; } = 25;
    public string AvailableRamText { get; init; } = "12.0 GB Available";
    public string DisplaySummary => $"{OsName} ({BuildNumber})";
}

public static class SystemInfoHelper
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private class MEMORYSTATUSEX
    {
        public uint dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

    public static RamMetrics GetRamMetrics()
    {
        double totalGb = 16.0;
        double availGb = 12.0;
        double usedGb = 4.0;
        int usagePercent = 25;

        try
        {
            var memStatus = new MEMORYSTATUSEX();
            if (GlobalMemoryStatusEx(memStatus) && memStatus.ullTotalPhys > 0)
            {
                totalGb = memStatus.ullTotalPhys / 1073741824.0;
                availGb = memStatus.ullAvailPhys / 1073741824.0;
                usedGb = (memStatus.ullTotalPhys - memStatus.ullAvailPhys) / 1073741824.0;
                usagePercent = (int)Math.Round(((double)(memStatus.ullTotalPhys - memStatus.ullAvailPhys) / memStatus.ullTotalPhys) * 100.0);
                usagePercent = Math.Clamp(usagePercent, 0, 100);
            }
        }
        catch { }

        string displayText = $"{usedGb:F1} / {totalGb:F1} GB ({usagePercent}%)";
        string availableText = $"{availGb:F1} GB Available";

        return new RamMetrics(
            TotalGb: Math.Round(totalGb, 1),
            UsedGb: Math.Round(usedGb, 1),
            AvailableGb: Math.Round(availGb, 1),
            UsagePercent: usagePercent,
            DisplayText: displayText,
            AvailableText: availableText
        );
    }

    public static SystemInfoSummary GetSystemInfo()
    {
        string osName = "Windows";
        string build = Environment.OSVersion.Version.Build.ToString();
        string displayVersion = "";
        string cpuName = "Multi-Core Processor";

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

        try
        {
            using var cpuKey = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            if (cpuKey != null)
            {
                var proc = cpuKey.GetValue("ProcessorNameString")?.ToString();
                if (!string.IsNullOrWhiteSpace(proc)) cpuName = proc.Trim();
            }
        }
        catch { }

        if (Environment.OSVersion.Version.Build >= 22000 && osName.Contains("Windows 10"))
        {
            osName = osName.Replace("Windows 10", "Windows 11");
        }

        string arch = Environment.Is64BitOperatingSystem ? "64-bit (x64)" : "32-bit";

        var ram = GetRamMetrics();

        return new SystemInfoSummary
        {
            OsName = string.IsNullOrEmpty(displayVersion) ? osName : $"{osName} {displayVersion}",
            BuildNumber = $"Build {build}",
            Architecture = arch,
            MemoryDisplay = ram.DisplayText,
            Hostname = Environment.MachineName,
            ProcessorName = cpuName,
            TotalPhysicalRamGb = ram.TotalGb,
            UsedPhysicalRamGb = ram.UsedGb,
            AvailablePhysicalRamGb = ram.AvailableGb,
            MemoryLoadPercent = ram.UsagePercent,
            AvailableRamText = ram.AvailableText
        };
    }
}
