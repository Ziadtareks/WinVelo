using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Tweakora.App.Services;

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
    public int MemoryLoadPercent { get; init; } = 25;
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

        double totalGb = 16.0;
        double usedGb = 4.0;
        int loadPercent = 25;
        string mem = "16.0 GB RAM";

        try
        {
            var memStatus = new MEMORYSTATUSEX();
            if (GlobalMemoryStatusEx(memStatus))
            {
                totalGb = memStatus.ullTotalPhys / (1024.0 * 1024 * 1024);
                double availGb = memStatus.ullAvailPhys / (1024.0 * 1024 * 1024);
                usedGb = Math.Max(0.1, totalGb - availGb);
                loadPercent = (int)Math.Clamp(memStatus.dwMemoryLoad, 1, 100);
                mem = $"{usedGb:F1} GB / {totalGb:F1} GB ({loadPercent}%)";
            }
        }
        catch { }

        return new SystemInfoSummary
        {
            OsName = string.IsNullOrEmpty(displayVersion) ? osName : $"{osName} {displayVersion}",
            BuildNumber = $"Build {build}",
            Architecture = arch,
            MemoryDisplay = mem,
            Hostname = Environment.MachineName,
            ProcessorName = cpuName,
            TotalPhysicalRamGb = Math.Round(totalGb, 1),
            UsedPhysicalRamGb = Math.Round(usedGb, 1),
            MemoryLoadPercent = loadPercent
        };
    }
}
