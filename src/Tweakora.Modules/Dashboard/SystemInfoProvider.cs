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
    public int AppliedTweaksCount { get; init; }
    public int TotalCatalogTweaksCount { get; init; } = 79;
    public string RollbackJournalStatus { get; init; } = "Active - 0 pending entries";
    public string PlatformMode { get; init; } = "Test Mode (Simulated)";
}

public static class SystemInfoProvider
{
    public static SystemInfoSummary GetSystemSummary(int appliedCount = 0, int totalCatalogCount = 79, string? journalStatus = null, bool isTestMode = true)
    {
        OperatingSystem os = Environment.OSVersion;
        int build = os.Version.Build;
        string friendlyName = build >= 22000 ? "Windows 11" : "Windows 10";

        TimeSpan uptimeSpan = TimeSpan.FromMilliseconds(Environment.TickCount64);
        string uptimeStr = $"{uptimeSpan.Days}d {uptimeSpan.Hours}h {uptimeSpan.Minutes}m";

        int ramGb = (int)(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024 * 1024));
        if (ramGb <= 0) ramGb = 16;

        return new SystemInfoSummary
        {
            OsVersion = $"{friendlyName} ({RuntimeInformation.OSDescription})",
            OsBuild = $"Build {build}",
            Architecture = RuntimeInformation.OSArchitecture.ToString(),
            Uptime = uptimeStr,
            TotalRamGb = ramGb,
            MachineName = Environment.MachineName,
            AppliedTweaksCount = appliedCount,
            TotalCatalogTweaksCount = totalCatalogCount,
            RollbackJournalStatus = journalStatus ?? "Active - 0 pending entries",
            PlatformMode = isTestMode ? "Test Mode (Simulated)" : "Live Windows"
        };
    }
}
