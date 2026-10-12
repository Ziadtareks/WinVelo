using System.Runtime.InteropServices;

namespace Tweakora.Modules.Dashboard;

public sealed class SystemInfoSummary
{
    public string OsVersion { get; init; } = string.Empty;
    public string OsBuild { get; init; } = string.Empty;
    public string Architecture { get; init; } = string.Empty;
    public string Uptime { get; init; } = string.Empty;
    public int TotalRamGb { get; init; }
    public string MachineName { get; init; } = string.Empty;
    public int SafetyScore { get; init; } = 95;
}

public static class SystemInfoProvider
{
    public static SystemInfoSummary GetSystemSummary()
    {
        OperatingSystem os = Environment.OSVersion;
        int build = os.Version.Build;
        string friendlyName = build >= 22000 ? "Windows 11" : "Windows 10";

        TimeSpan uptimeSpan = TimeSpan.FromMilliseconds(Environment.TickCount64);
        string uptimeStr = $"{uptimeSpan.Days}d {uptimeSpan.Hours}h {uptimeSpan.Minutes}m";

        // Estimate RAM safely
        int ramGb = (int)(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024 * 1024));
        if (ramGb <= 0) ramGb = 16; // default fallback if restricted

        return new SystemInfoSummary
        {
            OsVersion = $"{friendlyName} ({RuntimeInformation.OSDescription})",
            OsBuild = $"Build {build}",
            Architecture = RuntimeInformation.OSArchitecture.ToString(),
            Uptime = uptimeStr,
            TotalRamGb = ramGb,
            MachineName = Environment.MachineName,
            SafetyScore = 98
        };
    }
}
