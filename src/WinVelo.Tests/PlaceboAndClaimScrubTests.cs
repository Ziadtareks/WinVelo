using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using WinVelo.App.ViewModels;
using WinVelo.Core.Engine;
using WinVelo.Core.Models;
using WinVelo.Core.Operations;
using WinVelo.Modules.Startup;
using WinVelo.Modules.Tweaks;
using WinVelo.Simulation;
using Xunit;

namespace WinVelo.Tests;

public sealed class PlaceboAndClaimScrubTests
{
    private static string GetCatalogDir()
    {
        string dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            string candidate = Path.Combine(dir, "Catalog");
            if (Directory.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir)!;
        }
        throw new InvalidOperationException("Could not locate Catalog directory.");
    }

    [Fact]
    public void Catalog_AllTweaks_HaveValidEvidenceLevel()
    {
        string tweaksDir = Path.Combine(GetCatalogDir(), "Tweaks");
        var validEvidence = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "documented", "situational", "anecdotal", "ui-equivalent" };

        var files = Directory.GetFiles(tweaksDir, "*.json");
        Assert.NotEmpty(files);

        int count = 0;
        foreach (var file in files)
        {
            var tweaks = JsonSerializer.Deserialize<List<TweakDefinition>>(
                File.ReadAllText(file),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            Assert.NotNull(tweaks);
            foreach (var tweak in tweaks)
            {
                count++;
                Assert.False(string.IsNullOrWhiteSpace(tweak.Evidence), $"Tweak {tweak.Id} in {Path.GetFileName(file)} must define an evidence level.");
                Assert.Contains(tweak.Evidence, validEvidence);
            }
        }

        Assert.Equal(142, count);
    }

    [Fact]
    public void Presets_ContainNoAnecdotal_AndNoExperimentalTweaks()
    {
        string catalogDir = GetCatalogDir();
        string tweaksDir = Path.Combine(catalogDir, "Tweaks");
        string presetsDir = Path.Combine(catalogDir, "Presets");

        var allTweaks = new Dictionary<string, TweakDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.GetFiles(tweaksDir, "*.json"))
        {
            var list = JsonSerializer.Deserialize<List<TweakDefinition>>(
                File.ReadAllText(file),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            foreach (var t in list ?? Enumerable.Empty<TweakDefinition>())
            {
                allTweaks[t.Id] = t;
            }
        }

        var presetFiles = Directory.GetFiles(presetsDir, "*.json");
        Assert.NotEmpty(presetFiles);

        foreach (var pFile in presetFiles)
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(pFile));
            var tweakIds = new List<string>();
            if (doc.RootElement.TryGetProperty("full", out var fullProp))
            {
                tweakIds.AddRange(fullProp.EnumerateArray().Select(x => x.GetString()!));
            }
            else if (doc.RootElement.TryGetProperty("tweakIds", out var tProp))
            {
                tweakIds.AddRange(tProp.EnumerateArray().Select(x => x.GetString()!));
            }

            foreach (var id in tweakIds)
            {
                Assert.True(allTweaks.TryGetValue(id, out var tweak), $"Preset {Path.GetFileName(pFile)} references unknown tweak '{id}'");
                Assert.NotEqual("anecdotal", tweak.Evidence, StringComparer.OrdinalIgnoreCase);
                Assert.NotEqual("Experimental", tweak.Category, StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void AuditedTweaks_HaveAccurateEvidenceAndCategory()
    {
        string tweaksDir = Path.Combine(GetCatalogDir(), "Tweaks");
        var allTweaks = new Dictionary<string, TweakDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.GetFiles(tweaksDir, "*.json"))
        {
            var list = JsonSerializer.Deserialize<List<TweakDefinition>>(
                File.ReadAllText(file),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            foreach (var t in list ?? Enumerable.Empty<TweakDefinition>())
            {
                allTweaks[t.Id] = t;
            }
        }

        // 1. Placebo or no-effect tweaks were removed from the catalog
        foreach (var removed in new[]
        {
            "performance.cpu.unpark-cores", "performance.cpu.priority-separation",
            "performance.memory.svchost-split-threshold", "network.wifi.disable-hotspot-reporting",
            "network.wifi.disable-autoconnect-sense", "performance.network.disable-remote-differential-compression",
            "privacy.ceip.customer-experience-improvement", "pref.hidesettingshome",
            "performance.input.buffer-queue"
        })
        {
            Assert.False(allTweaks.ContainsKey(removed), $"{removed} has no real effect and must stay removed");
        }

        // 3. GPU MPO: situational workaround
        var mpo = allTweaks["performance.gpu.disable-mpo"];
        Assert.Equal("situational", mpo.Evidence);

        // 4. Power throttling: situational (battery impact)
        var throttling = allTweaks["performance.power.disable-throttling"];
        Assert.Equal("situational", throttling.Evidence);

        // 5. MMCSS: moved to Experimental / situational (needsReview: true)
        var mmcss = allTweaks["performance.gaming.mmcss-responsiveness"];
        Assert.Equal("Experimental", mmcss.Category);
        Assert.Equal("situational", mmcss.Evidence);
        Assert.True(mmcss.NeedsReview);

        // 6. Mouse acceleration & HAGS: user preferences (ui-equivalent)
        var mouseAcc1 = allTweaks["gaming.input.disable-mouse-acceleration"];
        Assert.Equal("preferences", mouseAcc1.Category);
        Assert.Equal("ui-equivalent", mouseAcc1.Evidence);
        Assert.NotNull(mouseAcc1.Aliases);
        Assert.Contains("performance.input.raw-mouse", mouseAcc1.Aliases);
        Assert.DoesNotContain("pref.mouseacceleration", mouseAcc1.Aliases);

        var hags = allTweaks["performance.gpu.hags"];
        Assert.Equal("preferences", hags.Category);
        Assert.Equal("ui-equivalent", hags.Evidence);
    }

    [Fact]
    public void TweaksViewModel_ShowExperimentalTweaksToggle_HidesExperimentalByDefault()
    {
        var fakeReg = new FakeRegistryService();
        var fakeServices = new FakeServicesService();
        var fakeTasks = new FakeTaskSchedulerService();
        var fakeRestore = new FakeRestoreService();
        var fakeShell = new FakeShellService();
        string tempJournal = Path.Combine(Path.GetTempPath(), "WinVelo_PlaceboTest_" + Guid.NewGuid().ToString("N"));
        var journal = new RollbackJournalManager(tempJournal);
        var runner = new OperationRunner();

        var tweakService = new TweakService(fakeReg, fakeServices, fakeTasks, fakeRestore, fakeShell, journal);
        var vm = new TweaksViewModel(tweakService, runner);

        // By default, ShowExperimentalTweaks is false
        Assert.False(vm.ShowExperimentalTweaks);

        // FilteredTweaks must not contain any Experimental or anecdotal tweaks
        Assert.DoesNotContain(vm.FilteredTweaks, t => t.Category.Equals("Experimental", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(vm.FilteredTweaks, t => t.Evidence.Equals("anecdotal", StringComparison.OrdinalIgnoreCase));

        // When in All mode and toggled on, Experimental tweaks appear
        vm.IsRecommendedOnly = false;
        vm.ShowExperimentalTweaks = true;
        Assert.Contains(vm.FilteredTweaks, t => t.Category.Equals("Experimental", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(vm.FilteredTweaks, t => t.Evidence.Equals("anecdotal", StringComparison.OrdinalIgnoreCase));

        // When toggled off, they disappear again
        vm.ShowExperimentalTweaks = false;
        Assert.DoesNotContain(vm.FilteredTweaks, t => t.Category.Equals("Experimental", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task StartupViewModel_LabelsImpactAsHeuristic_AndOmitsSavedNumbers()
    {
        var fakeReg = new FakeRegistryService();
        var service = new StartupService(fakeReg);
        var vm = new StartupViewModel(service);

        await vm.RefreshCommandAsync();

        // Must explicitly label as estimated heuristic
        Assert.Contains("heuristic", vm.BootDelayText, StringComparison.OrdinalIgnoreCase);

        // Optimize
        await vm.OptimizeStartupAsync();

        // Must not contain unmeasured boot time saved claims
        Assert.DoesNotContain("saved", vm.BootTimeSavedText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("heuristic", vm.BootDelayText, StringComparison.OrdinalIgnoreCase);
    }
}
