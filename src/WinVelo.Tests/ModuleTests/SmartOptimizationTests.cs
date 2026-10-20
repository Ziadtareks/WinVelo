using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using WinVelo.Core.Engine;
using WinVelo.Core.Engine.Handlers;
using WinVelo.Core.Models;
using WinVelo.Core.Operations;
using WinVelo.Modules.Advisor;
using WinVelo.Modules.Memory;
using WinVelo.Modules.Network;
using WinVelo.Modules.Tweaks;
using WinVelo.Simulation;
using Xunit;
using ExecutionContext = WinVelo.Core.Engine.ExecutionContext;

namespace WinVelo.Tests.ModuleTests;

public sealed class SmartOptimizationTests
{
    [Fact]
    public async Task MemoryOptimizer_PurgeMemoryAndStandbyList_ReturnsValidMetrics()
    {
        var service = new MemoryOptimizerService();
        var result = await service.PurgeMemoryAndStandbyListAsync();

        Assert.NotNull(result);
        Assert.True(result.BytesFreed >= 0);
        Assert.False(string.IsNullOrWhiteSpace(result.FormattedFreed));
        Assert.True(result.UsedRamBeforeGb > 0);
        Assert.True(result.LoadPercentBefore > 0);
    }

    [Fact]
    public async Task NetworkOptimizer_BenchmarkDnsProviders_ReturnsRankedProviders()
    {
        var fakeRegistry = new FakeRegistryService();
        var fakeShell = new FakeShellService();
        var service = new NetworkOptimizerService(fakeRegistry, fakeShell);

        var results = await service.BenchmarkDnsProvidersAsync();

        Assert.NotNull(results);
        Assert.Equal(8, results.Count); // every provider in Catalog/Config/dns.json
        Assert.Contains(results, r => r.Id == "dns.cloudflare.standard");
        Assert.Contains(results, r => r.Id == "dns.google.public");
        Assert.Contains(results, r => r.Id == "dns.quad9.secure");
        Assert.All(results.Where(r => r.IsFastest), r => Assert.True(r.IsNeutral)); // never a filtering resolver

        // Exactly one or at least one is flagged as fastest if responsive
        if (results.Any(r => r.AveragePingMs > 0))
        {
            Assert.Contains(results, r => r.IsFastest);
        }
    }

    [Fact]
    public async Task NetworkOptimizer_NagleAlgorithm_SetAndDetectLifecycle()
    {
        var fakeRegistry = new FakeRegistryService();
        var fakeShell = new FakeShellService();

        // Seed an active network adapter interface in registry
        string guid = "{11111111-2222-3333-4444-555555555555}";
        string nicPath = $@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\{guid}";
        fakeRegistry.CreateKey("HKLM", nicPath);
        fakeRegistry.SetValue("HKLM", nicPath, "IPAddress", "192.168.1.50", "String");

        var service = new NetworkOptimizerService(fakeRegistry, fakeShell);

        // Initially not disabled
        bool initial = await service.DetectNagleAlgorithmDisabledAsync();
        Assert.False(initial);

        // Apply: disable Nagle's algorithm for low-latency gaming
        bool applied = await service.SetNagleAlgorithmStateAsync(true);
        Assert.True(applied);

        bool detected = await service.DetectNagleAlgorithmDisabledAsync();
        Assert.True(detected);

        // Undo: restore standard TCP packet batching
        bool reverted = await service.SetNagleAlgorithmStateAsync(false);
        Assert.True(reverted);

        bool finalState = await service.DetectNagleAlgorithmDisabledAsync();
        Assert.False(finalState);
    }

    [Fact]
    public async Task SmartAdvisor_AnalyzeHardware_ReadsProfileCorrectly()
    {
        var fakeRegistry = new FakeRegistryService();
        var fakeShell = new FakeShellService();

        // Seed CPU and GPU
        fakeRegistry.CreateKey("HKLM", @"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
        fakeRegistry.SetValue("HKLM", @"HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString", "AMD Ryzen 9 7950X 16-Core Processor", "String");
        fakeRegistry.SetValue("HKLM", @"HARDWARE\DESCRIPTION\System\CentralProcessor\0", "VendorIdentifier", "AuthenticAMD", "String");

        fakeRegistry.CreateKey("HKLM", @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0000");
        fakeRegistry.SetValue("HKLM", @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0000", "DriverDesc", "NVIDIA GeForce RTX 4090", "String");

        var advisor = new SmartAdvisorService(fakeRegistry, fakeShell);
        var profile = await advisor.AnalyzeHardwareAsync();

        Assert.NotNull(profile);
        Assert.Equal("AMD", profile.CpuVendor);
        Assert.Equal("NVIDIA", profile.GpuVendor);
        Assert.True(profile.TotalPhysicalRamKb > 0);
        Assert.True(profile.LogicalCores > 0);
    }

    [Fact]
    public async Task SmartAdvisor_OptimizationScoreAndSmartBoost_Lifecycle()
    {
        var fakeRegistry = new FakeRegistryService();
        var fakeServices = new FakeServicesService();
        var fakeTasks = new FakeTaskSchedulerService();
        var fakeRestore = new FakeRestoreService();
        var fakeShell = new FakeShellService();

        string journalDir = Path.Combine(Path.GetTempPath(), "SmartAdvisor_Test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(journalDir);

        try
        {
            var journal = new RollbackJournalManager(journalDir);
            var tweakService = new TweakService(fakeRegistry, fakeServices, fakeTasks, fakeRestore, fakeShell, journal);
            var catalog = tweakService.LoadCatalog();

            Assert.NotEmpty(catalog);

            var advisor = new SmartAdvisorService(fakeRegistry, fakeShell);

            // 1. Initial Score (no tweaks applied)
            var initialAssessment = await advisor.CalculateOptimizationScoreAsync(catalog, tweakService);
            Assert.NotNull(initialAssessment);
            Assert.True(initialAssessment.ScorePercent >= 0 && initialAssessment.ScorePercent <= 100);
            Assert.NotEmpty(initialAssessment.TailoredRecommendationNotes);

            // 2. Apply Smart Boost
            var runner = new OperationRunner();
            var report = await advisor.ApplySmartTailoredOptimizationsAsync(catalog, tweakService, runner);

            Assert.NotNull(report);
            Assert.True(report.SucceededCount > 0);

            // Smart Boost only changes settings through the tweak engine (journaled, undoable).
            Assert.Null(fakeRegistry.GetValue("HKLM", @"SYSTEM\CurrentControlSet\Control", "SvcHostSplitThresholdInKB"));

            // 3. Recalculated Score should be higher
            var postAssessment = await advisor.CalculateOptimizationScoreAsync(catalog, tweakService);
            Assert.True(postAssessment.ScorePercent > initialAssessment.ScorePercent);
            Assert.True(postAssessment.AppliedCount > initialAssessment.AppliedCount);
        }
        finally
        {
            try { Directory.Delete(journalDir, true); } catch { }
        }
    }

    [Fact]
    public void Catalog_ContainsAllSmartOptimizerTweaks()
    {
        var fakeRegistry = new FakeRegistryService();
        var fakeServices = new FakeServicesService();
        var fakeTasks = new FakeTaskSchedulerService();
        var fakeRestore = new FakeRestoreService();
        var fakeShell = new FakeShellService();
        var journal = new RollbackJournalManager(Path.Combine(Path.GetTempPath(), "Smart_CatTest_" + Guid.NewGuid().ToString("N")));

        var tweakService = new TweakService(fakeRegistry, fakeServices, fakeTasks, fakeRestore, fakeShell, journal);
        var catalog = tweakService.LoadCatalog();

        var expectedSmartIds = new[]
        {
            "performance.gpu.hags",
            "performance.input.raw-mouse",
            "performance.power.disable-throttling"
        };

        foreach (var id in expectedSmartIds)
        {
            var tweak = catalog.FirstOrDefault(t =>
                string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(t.Origin, id, StringComparison.OrdinalIgnoreCase) ||
                (t.Aliases != null && t.Aliases.Any(a => string.Equals(a, id, StringComparison.OrdinalIgnoreCase))));
            Assert.NotNull(tweak);
            Assert.NotEmpty(tweak.Apply);
            Assert.NotEmpty(tweak.Undo);
            Assert.False(string.IsNullOrWhiteSpace(tweak.Name));
            Assert.False(string.IsNullOrWhiteSpace(tweak.Description));
        }
    }
}
