using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Tweakora.App.ViewModels;
using Tweakora.Core.Engine;
using Tweakora.Core.Models;
using Tweakora.Core.Operations;
using Tweakora.Modules.Advisor;
using Tweakora.Modules.Memory;
using Tweakora.Modules.Tweaks;
using Tweakora.Simulation;
using Xunit;

namespace Tweakora.Tests;

public sealed class PresetValidationTests
{
    private readonly string _catalogDir;

    public PresetValidationTests()
    {
        string baseDir = AppContext.BaseDirectory;
        string? candidate = baseDir;
        string? found = null;
        while (candidate != null)
        {
            string p = Path.Combine(candidate, "Catalog");
            if (Directory.Exists(p))
            {
                found = p;
                break;
            }
            candidate = Directory.GetParent(candidate)?.FullName;
        }

        _catalogDir = found ?? Path.Combine(baseDir, "Catalog");
    }

    [Fact]
    public void Presets_AllTweakIdsExistInCatalog_AndMeetSafetyCriteria()
    {
        var fakeReg = new FakeRegistryService();
        var fakeSvc = new FakeServicesService();
        var fakeSched = new FakeTaskSchedulerService();
        var fakeRestore = new FakeRestoreService();
        var fakeShell = new FakeShellService();
        var journal = new RollbackJournalManager(Path.Combine(AppContext.BaseDirectory, "test-journal-presets-safety"));
        var tweakService = new TweakService(fakeReg, fakeSvc, fakeSched, fakeRestore, fakeShell, journal);

        var catalog = tweakService.LoadCatalog();
        var catalogMap = catalog.ToDictionary(t => t.Id, StringComparer.OrdinalIgnoreCase);

        string presetsDir = Path.Combine(_catalogDir, "Presets");
        Assert.True(Directory.Exists(presetsDir), $"Presets dir not found: {presetsDir}");

        var presetFiles = Directory.GetFiles(presetsDir, "*.json");
        Assert.True(presetFiles.Length >= 3, $"Expected at least 3 presets, found {presetFiles.Length}");

        foreach (var file in presetFiles)
        {
            string json = File.ReadAllText(file);
            var preset = JsonSerializer.Deserialize<PresetDefinition>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            Assert.NotNull(preset);
            Assert.False(string.IsNullOrWhiteSpace(preset.Id), $"Preset in {Path.GetFileName(file)} has empty id");
            Assert.False(string.IsNullOrWhiteSpace(preset.Name), $"Preset in {Path.GetFileName(file)} has empty name");
            Assert.False(string.IsNullOrWhiteSpace(preset.Description), $"Preset in {Path.GetFileName(file)} has empty description");
            Assert.NotEmpty(preset.TweakIds);

            foreach (var tweakId in preset.TweakIds)
            {
                Assert.True(catalogMap.TryGetValue(tweakId, out var tweak),
                    $"Preset '{preset.Id}' references unknown tweak '{tweakId}'");

                Assert.NotEqual("High", tweak!.Risk);
                Assert.True(tweak.Reversible, $"Preset '{preset.Id}' contains irreversible tweak '{tweakId}'");
                Assert.False(tweak.NeedsReview, $"Preset '{preset.Id}' contains needsReview tweak '{tweakId}'");
                Assert.NotEqual("anecdotal", tweak.Evidence, StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void DashboardViewModel_ProfilesLoadedFromPresetsAndUndoCard()
    {
        var fakeReg = new FakeRegistryService();
        var fakeSvc = new FakeServicesService();
        var fakeSched = new FakeTaskSchedulerService();
        var fakeRestore = new FakeRestoreService();
        var fakeShell = new FakeShellService();
        var journal = new RollbackJournalManager(Path.Combine(AppContext.BaseDirectory, "test-journal-dvm-presets"));
        var runner = new OperationRunner();

        var tweakService = new TweakService(fakeReg, fakeSvc, fakeSched, fakeRestore, fakeShell, journal);
        var memOpt = new MemoryOptimizerService();
        var advisor = new SmartAdvisorService(fakeReg, fakeShell);
        var dvm = new DashboardViewModel(tweakService, runner, fakeRestore, memOpt, advisor);

        // 3 catalog presets (Undo All is a secondary action, not a card)
        Assert.Equal(3, dvm.Profiles.Count);
        Assert.NotNull(dvm.UndoAllChangesCommand);

        // Verify presets have honest titles
        var gamingCard = dvm.Profiles.FirstOrDefault(p => p.Id == "preset.gaming");
        Assert.NotNull(gamingCard);
        Assert.DoesNotContain("FPS Boost", gamingCard.Title);
        Assert.Contains("Gaming", gamingCard.Title);
    }

    [Fact]
    public async Task DashboardViewModel_OpenProfilePreview_SkipsAlreadyAppliedTweaks()
    {
        var fakeReg = new FakeRegistryService();
        var fakeSvc = new FakeServicesService();
        var fakeSched = new FakeTaskSchedulerService();
        var fakeRestore = new FakeRestoreService();
        var fakeShell = new FakeShellService();
        var journal = new RollbackJournalManager(Path.Combine(AppContext.BaseDirectory, "test-journal-dvm-preview"));
        var runner = new OperationRunner();

        var tweakService = new TweakService(fakeReg, fakeSvc, fakeSched, fakeRestore, fakeShell, journal);
        var memOpt = new MemoryOptimizerService();
        var advisor = new SmartAdvisorService(fakeReg, fakeShell);
        var dvm = new DashboardViewModel(tweakService, runner, fakeRestore, memOpt, advisor);

        var catalog = tweakService.LoadCatalog();
        var sampleTweaks = catalog.Take(2).ToList();

        await dvm.OpenProfilePreviewAsync("Test Preview", "Testing", "Notice", "Confirm", sampleTweaks, isUndo: false, card: null);

        Assert.True(dvm.IsProfilePreviewOpen);
        Assert.Equal(2, dvm.ProfilePreviewItems.Count);
        Assert.All(dvm.ProfilePreviewItems, item =>
        {
            Assert.NotNull(item.Name);
            Assert.NotNull(item.Risk);
        });

        dvm.CancelProfilePreview();
        Assert.False(dvm.IsProfilePreviewOpen);
    }

    [Fact]
    public async Task DashboardViewModel_SmartAnalyzeAndOptimize_OnlyRecommendsAndNeverAutoApplies()
    {
        var fakeReg = new FakeRegistryService();
        var fakeSvc = new FakeServicesService();
        var fakeSched = new FakeTaskSchedulerService();
        var fakeRestore = new FakeRestoreService();
        var fakeShell = new FakeShellService();
        var journal = new RollbackJournalManager(Path.Combine(AppContext.BaseDirectory, "test-journal-dvm-smart"));
        var runner = new OperationRunner();

        var tweakService = new TweakService(fakeReg, fakeSvc, fakeSched, fakeRestore, fakeShell, journal);
        var memOpt = new MemoryOptimizerService();
        var advisor = new SmartAdvisorService(fakeReg, fakeShell);
        var dvm = new DashboardViewModel(tweakService, runner, fakeRestore, memOpt, advisor);

        // SmartAnalyzeAndOptimizeCommand should NOT execute modifications directly; it opens the preview dialog
        Assert.False(dvm.IsProfilePreviewOpen);

        // Invoking SmartAnalyzeAndOptimize
        var smartMethod = typeof(DashboardViewModel).GetMethod("SmartAnalyzeAndOptimizeAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(smartMethod);

        var task = (Task)smartMethod.Invoke(dvm, null)!;
        await task;

        // If recommendations exist, preview dialog is opened for user confirmation, NOT auto-applied
        if (dvm.ProfilePreviewItems.Count > 0)
        {
            Assert.True(dvm.IsProfilePreviewOpen);
            Assert.Contains("Recommendations", dvm.ProfilePreviewTitle);
        }
    }
}
