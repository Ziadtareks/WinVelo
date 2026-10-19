using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using WinVelo.Core.Interfaces;
using WinVelo.Core.Models;
using WinVelo.Core.Operations;
using WinVelo.Modules.Tweaks;

namespace WinVelo.Modules.Advisor;

public enum HardwareFormFactor
{
    Desktop,
    Laptop,
    Unknown
}

public enum StorageKind
{
    SsdOrNvme,
    Hdd,
    Unknown
}

public sealed class HardwareProfile
{
    public HardwareFormFactor FormFactor { get; init; } = HardwareFormFactor.Desktop;
    public StorageKind StorageType { get; init; } = StorageKind.SsdOrNvme;
    public string GpuVendor { get; init; } = "Unknown";
    public string GpuName { get; init; } = "Standard Display Adapter";
    public string CpuVendor { get; init; } = "Unknown";
    public string CpuName { get; init; } = "Multi-Core Processor";
    public int LogicalCores { get; init; } = 4;
    public ulong TotalPhysicalRamKb { get; init; } = 16777216;
    public double TotalPhysicalRamGb => Math.Round(TotalPhysicalRamKb / (1024.0 * 1024.0), 1);
    public string FormFactorDisplay => FormFactor == HardwareFormFactor.Laptop ? "Laptop (Battery Device)" : "Desktop PC";
    public string StorageDisplay => StorageType == StorageKind.SsdOrNvme ? "Solid State Drive (SSD/NVMe)" : "Hard Disk Drive (HDD)";
}

public sealed class OptimizationAssessment
{
    public int ScorePercent { get; init; }
    public int AppliedCount { get; init; }
    public int TotalScoredCount { get; init; }
    public IReadOnlyList<string> TailoredRecommendationNotes { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> RecommendedTweakIds { get; init; } = Array.Empty<string>();
}

public interface ISmartAdvisorService
{
    Task<HardwareProfile> AnalyzeHardwareAsync();
    Task<OptimizationAssessment> CalculateOptimizationScoreAsync(IReadOnlyList<TweakDefinition> catalog, ITweakService tweakService);
    Task<OperationExecutionReport> ApplySmartTailoredOptimizationsAsync(
        IReadOnlyList<TweakDefinition> catalog,
        ITweakService tweakService,
        IOperationRunner runner);
    Task<IReadOnlyList<TweakDefinition>> GetSmartRecommendationsAsync(
        IReadOnlyList<TweakDefinition> catalog,
        ITweakService tweakService);
}

public sealed class SmartAdvisorService : ISmartAdvisorService
{
    private readonly IRegistryService _registry;
    private readonly IShellService _shell;

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS sps);

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

    public SmartAdvisorService(IRegistryService registry, IShellService shell)
    {
        _registry = registry;
        _shell = shell;
    }

    public Task<HardwareProfile> AnalyzeHardwareAsync()
    {
        return Task.Run(() =>
        {
            // 1. Form Factor Detection (Laptop with battery vs Desktop)
            var formFactor = HardwareFormFactor.Desktop;
            try
            {
                if (GetSystemPowerStatus(out var sps))
                {
                    // BatteryFlag: 128 means "No system battery"
                    if (sps.BatteryFlag != 128 && sps.BatteryFlag != 255)
                    {
                        formFactor = HardwareFormFactor.Laptop;
                    }
                }
            }
            catch { }

            // 2. RAM Capacity
            ulong totalRamKb = 16 * 1024 * 1024;
            try
            {
                var mem = new MEMORYSTATUSEX();
                if (GlobalMemoryStatusEx(mem))
                {
                    totalRamKb = mem.ullTotalPhys / 1024;
                }
            }
            catch { }

            // 3. CPU Info
            string cpuName = "Multi-Core Processor";
            string cpuVendor = "Unknown";
            try
            {
                var proc = _registry.GetValue("HKLM", @"HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString")?.ToString();
                if (!string.IsNullOrWhiteSpace(proc)) cpuName = proc.Trim();

                var vendor = _registry.GetValue("HKLM", @"HARDWARE\DESCRIPTION\System\CentralProcessor\0", "VendorIdentifier")?.ToString();
                if (!string.IsNullOrWhiteSpace(vendor))
                {
                    if (vendor.Contains("Intel", StringComparison.OrdinalIgnoreCase)) cpuVendor = "Intel";
                    else if (vendor.Contains("AMD", StringComparison.OrdinalIgnoreCase)) cpuVendor = "AMD";
                    else cpuVendor = vendor;
                }
            }
            catch { }

            // 4. GPU Info
            string gpuVendor = "Unknown";
            string gpuName = "Standard Display Adapter";
            try
            {
                string[] gpuKeys = new[]
                {
                    @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0000",
                    @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0001"
                };

                foreach (var k in gpuKeys)
                {
                    var desc = _registry.GetValue("HKLM", k, "DriverDesc")?.ToString();
                    if (!string.IsNullOrWhiteSpace(desc))
                    {
                        gpuName = desc.Trim();
                        if (desc.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) || desc.Contains("GeForce", StringComparison.OrdinalIgnoreCase))
                        {
                            gpuVendor = "NVIDIA";
                            break;
                        }
                        if (desc.Contains("AMD", StringComparison.OrdinalIgnoreCase) || desc.Contains("Radeon", StringComparison.OrdinalIgnoreCase))
                        {
                            gpuVendor = "AMD";
                            break;
                        }
                        if (desc.Contains("Intel", StringComparison.OrdinalIgnoreCase) || desc.Contains("Arc", StringComparison.OrdinalIgnoreCase))
                        {
                            gpuVendor = "Intel";
                            break;
                        }
                    }
                }
            }
            catch { }

            // 5. Storage Type (OS Drive C:)
            var storageKind = StorageKind.SsdOrNvme;

            return new HardwareProfile
            {
                FormFactor = formFactor,
                StorageType = storageKind,
                GpuVendor = gpuVendor,
                GpuName = gpuName,
                CpuVendor = cpuVendor,
                CpuName = cpuName,
                LogicalCores = Environment.ProcessorCount,
                TotalPhysicalRamKb = totalRamKb
            };
        });
    }

    public async Task<OptimizationAssessment> CalculateOptimizationScoreAsync(
        IReadOnlyList<TweakDefinition> catalog,
        ITweakService tweakService)
    {
        var profile = await AnalyzeHardwareAsync();

        // Tweaks evaluated for the score. Only tweaks with a real, verifiable effect (no experimental or placebo entries).
        var targetIds = new List<string>
        {
            "pref.darkmode",
            "pref.showext",
            "pref.bingsearch",
            "essential.telemetry",
            "privacy.spotlight.lockscreen-ads",
            "essential.activity",
            "performance.input.raw-mouse"
        };

        if (profile.GpuVendor == "NVIDIA" || profile.GpuVendor == "AMD")
        {
            targetIds.Add("performance.gpu.hags");
        }

        if (profile.FormFactor == HardwareFormFactor.Desktop)
        {
            targetIds.Add("performance.power.disable-throttling");
        }

        int appliedCount = 0;
        var recommendedList = new List<string>();

        foreach (var id in targetIds)
        {
            var tweak = catalog.FirstOrDefault(t =>
                string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(t.Origin, id, StringComparison.OrdinalIgnoreCase) ||
                (t.Aliases != null && t.Aliases.Any(a => string.Equals(a, id, StringComparison.OrdinalIgnoreCase))));
            if (tweak != null)
            {
                try
                {
                    bool active = await tweakService.DetectStateAsync(tweak);
                    if (active)
                    {
                        appliedCount++;
                    }
                    else
                    {
                        recommendedList.Add(tweak.Id);
                    }
                }
                catch { }
            }
        }

        int score = targetIds.Count > 0 ? (int)Math.Round((double)appliedCount / targetIds.Count * 100) : 50;

        var notes = new List<string>();
        if (profile.FormFactor == HardwareFormFactor.Laptop)
        {
            notes.Add("Laptop detected: power throttling is left on to protect battery life.");
        }
        else
        {
            notes.Add("Desktop detected: power throttling can be turned off for background apps.");
        }

        if (profile.GpuVendor == "NVIDIA" || profile.GpuVendor == "AMD")
        {
            notes.Add($"{profile.GpuVendor} GPU detected: hardware-accelerated GPU scheduling is available.");
        }

        return new OptimizationAssessment
        {
            ScorePercent = Math.Clamp(score, 0, 100),
            AppliedCount = appliedCount,
            TotalScoredCount = targetIds.Count,
            TailoredRecommendationNotes = notes,
            RecommendedTweakIds = recommendedList
        };
    }

    public async Task<OperationExecutionReport> ApplySmartTailoredOptimizationsAsync(
        IReadOnlyList<TweakDefinition> catalog,
        ITweakService tweakService,
        IOperationRunner runner)
    {
        var profile = await AnalyzeHardwareAsync();

        // Select hardware-tailored tweaks from the catalog. Every change goes through the
        // tweak engine, so it is journaled and can be undone.
        var tweakIdsToApply = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "pref.bingsearch",
            "essential.telemetry",
            "privacy.spotlight.lockscreen-ads",
            "essential.activity",
            "performance.input.raw-mouse"
        };

        if (profile.GpuVendor == "NVIDIA" || profile.GpuVendor == "AMD")
        {
            tweakIdsToApply.Add("performance.gpu.hags");
        }

        if (profile.FormFactor == HardwareFormFactor.Desktop)
        {
            tweakIdsToApply.Add("performance.power.disable-throttling");
        }

        var tweaks = catalog.Where(t =>
            tweakIdsToApply.Contains(t.Id) ||
            (!string.IsNullOrEmpty(t.Origin) && tweakIdsToApply.Contains(t.Origin)) ||
            (t.Aliases != null && t.Aliases.Any(a => tweakIdsToApply.Contains(a)))).ToList();
        return await tweakService.ApplyTweaksAsync(tweaks, runner);
    }

    public async Task<IReadOnlyList<TweakDefinition>> GetSmartRecommendationsAsync(
        IReadOnlyList<TweakDefinition> catalog,
        ITweakService tweakService)
    {
        var assessment = await CalculateOptimizationScoreAsync(catalog, tweakService);
        var recIds = new HashSet<string>(assessment.RecommendedTweakIds, StringComparer.OrdinalIgnoreCase);

        return catalog
            .Where(t => recIds.Contains(t.Id) &&
                        !string.Equals(t.Risk, "High", StringComparison.OrdinalIgnoreCase) &&
                        t.Reversible &&
                        !t.NeedsReview)
            .ToList();
    }
}
