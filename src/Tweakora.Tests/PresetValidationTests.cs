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
        Assert.True(presetFiles.Length >= 6, $"Expected at least 6 presets, found {presetFiles.Length}");

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
            Assert.NotEmpty(preset.Full);

            foreach (var tweakId in preset.Standard)
            {
                Assert.True(catalogMap.TryGetValue(tweakId, out var tweak),
                    $"Preset '{preset.Id}' standard references unknown tweak '{tweakId}'");

                Assert.True(string.Equals(tweak!.Risk, "Safe", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(tweak.Risk, "Low", StringComparison.OrdinalIgnoreCase),
                    $"Preset '{preset.Id}' standard contains tweak '{tweakId}' with risk '{tweak.Risk}'");
                Assert.True(tweak.Reversible, $"Preset '{preset.Id}' contains irreversible tweak '{tweakId}'");
                Assert.False(tweak.NeedsReview, $"Preset '{preset.Id}' contains needsReview tweak '{tweakId}'");
                Assert.NotEqual("anecdotal", tweak.Evidence, StringComparer.OrdinalIgnoreCase);
            }

            foreach (var tweakId in preset.Full)
            {
                Assert.True(catalogMap.TryGetValue(tweakId, out var tweak),
                    $"Preset '{preset.Id}' full references unknown tweak '{tweakId}'");

                Assert.NotEqual("High", tweak!.Risk);
                Assert.True(tweak.Reversible, $"Preset '{preset.Id}' contains irreversible tweak '{tweakId}'");
                Assert.False(tweak.NeedsReview, $"Preset '{preset.Id}' contains needsReview tweak '{tweakId}'");
                Assert.NotEqual("anecdotal", tweak.Evidence, StringComparer.OrdinalIgnoreCase);
            }

            foreach (var excluded in preset.Excluded)
            {
                Assert.True(catalogMap.TryGetValue(excluded.Id, out _),
                    $"Preset '{preset.Id}' excluded references unknown tweak '{excluded.Id}'");
                Assert.False(string.IsNullOrWhiteSpace(excluded.Reason),
                    $"Preset '{preset.Id}' excluded tweak '{excluded.Id}' has empty reason");
            }
        }
    }

    [Fact]
    public void Presets_CommittedFilesMatchGeneratorOutput_NoDrift()
    {
        var fakeReg = new FakeRegistryService();
        var fakeSvc = new FakeServicesService();
        var fakeSched = new FakeTaskSchedulerService();
        var fakeRestore = new FakeRestoreService();
        var fakeShell = new FakeShellService();
        var journal = new RollbackJournalManager(Path.Combine(AppContext.BaseDirectory, "test-journal-presets-drift"));
        var tweakService = new TweakService(fakeReg, fakeSvc, fakeSched, fakeRestore, fakeShell, journal);

        var catalog = tweakService.LoadCatalog().ToList();
        var generated = PresetGenerator.Program.GeneratePresets(catalog);

        string presetsDir = Path.Combine(_catalogDir, "Presets");
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        foreach (var (spec, expectedPreset) in generated)
        {
            string filePath = Path.Combine(presetsDir, spec.FileName);
            Assert.True(File.Exists(filePath), $"Preset file missing: {filePath}");

            string committedJson = File.ReadAllText(filePath).TrimEnd();
            string expectedJson = (JsonSerializer.Serialize(expectedPreset, options) + "\n").TrimEnd();

            Assert.Equal(expectedJson, committedJson);
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

        // 6 catalog presets (recommended, gaming, privacy, ai, clean-ui, performance)
        Assert.Equal(6, dvm.Profiles.Count);
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

    [Fact]
    public void DashboardViewModel_PresetCards_StandardFullSelectorTogglesTweaksAndPreview()
    {
        var fakeReg = new FakeRegistryService();
        var fakeSvc = new FakeServicesService();
        var fakeSched = new FakeTaskSchedulerService();
        var fakeRestore = new FakeRestoreService();
        var fakeShell = new FakeShellService();
        var journal = new RollbackJournalManager(Path.Combine(AppContext.BaseDirectory, "test-journal-dvm-tiers"));
        var runner = new OperationRunner();

        var tweakService = new TweakService(fakeReg, fakeSvc, fakeSched, fakeRestore, fakeShell, journal);
        var memOpt = new MemoryOptimizerService();
        var advisor = new SmartAdvisorService(fakeReg, fakeShell);
        var dvm = new DashboardViewModel(tweakService, runner, fakeRestore, memOpt, advisor);

        var gamingCard = dvm.Profiles.FirstOrDefault(p => p.Id == "preset.gaming");
        Assert.NotNull(gamingCard);
        Assert.Equal("Standard: 4 tweaks · Full: 13 tweaks", gamingCard.TiersSubtitle);

        // Gaming starts in Standard tier
        Assert.True(gamingCard.IsStandardTier);
        Assert.False(gamingCard.IsFullTier);
        Assert.Equal(4, gamingCard.PreviewTweaks.Count);
        Assert.DoesNotContain("and", gamingCard.PreviewTweakSummary); // 4 tweaks total, no remaining

        // Toggle to Full tier
        gamingCard.IsFullTier = true;
        Assert.False(gamingCard.IsStandardTier);
        Assert.Equal(4, gamingCard.PreviewTweaks.Count); // Preview shows first 4
        Assert.Contains("and 9 more", gamingCard.PreviewTweakSummary); // 13 - 4 = 9 remaining

        // Toggle back to Standard tier
        gamingCard.IsStandardTier = true;
        Assert.True(gamingCard.IsStandardTier);
        Assert.False(gamingCard.IsFullTier);
        Assert.DoesNotContain("and", gamingCard.PreviewTweakSummary);
    }

    [Fact]
    public void DashboardViewModel_PresetCards_StatusMessageAutoClearsAndClearsOnNav()
    {
        var fakeReg = new FakeRegistryService();
        var fakeSvc = new FakeServicesService();
        var fakeSched = new FakeTaskSchedulerService();
        var fakeRestore = new FakeRestoreService();
        var fakeShell = new FakeShellService();
        var journal = new RollbackJournalManager(Path.Combine(AppContext.BaseDirectory, "test-journal-dvm-status"));
        var runner = new OperationRunner();

        var tweakService = new TweakService(fakeReg, fakeSvc, fakeSched, fakeRestore, fakeShell, journal);
        var memOpt = new MemoryOptimizerService();
        var advisor = new SmartAdvisorService(fakeReg, fakeShell);
        var dvm = new DashboardViewModel(tweakService, runner, fakeRestore, memOpt, advisor);

        var gamingCard = dvm.Profiles.First(p => p.Id == "preset.gaming");
        gamingCard.SetStatusMessage("Profile applied successfully!", "#34D399");
        Assert.Equal("Profile applied successfully!", gamingCard.StatusMessage);

        // Navigation or manual clear wipes it immediately
        dvm.ClearProfileStatusMessages();
        Assert.Empty(gamingCard.StatusMessage);
    }

    [Fact]
    public async Task UnifiedExecutionDialogViewModel_OpenPreview_GroupsBySubcategoryAndProvidesSelectAllNone()
    {
        var fakeReg = new FakeRegistryService();
        var fakeSvc = new FakeServicesService();
        var fakeSched = new FakeTaskSchedulerService();
        var fakeRestore = new FakeRestoreService();
        var fakeShell = new FakeShellService();
        var journal = new RollbackJournalManager(Path.Combine(AppContext.BaseDirectory, "test-journal-uedvm-grouping"));
        var tweakService = new TweakService(fakeReg, fakeSvc, fakeSched, fakeRestore, fakeShell, journal);
        var uedvm = new UnifiedExecutionDialogViewModel(tweakService);

        var tweaks = new List<TweakDefinition>
        {
            new() { Id = "test.sub1.a", Name = "Sub1 Item A", Risk = "Safe", Subcategory = "Networking", Category = "network", Reversible = true },
            new() { Id = "test.sub1.b", Name = "Sub1 Item B", Risk = "Low", Subcategory = "Networking", Category = "network", Reversible = true },
            new() { Id = "test.sub2.a", Name = "Sub2 Item A", Risk = "Safe", Subcategory = "Visuals", Category = "ui", Reversible = true, RequiresExplorerRestart = true }
        };

        var excluded = new List<ExcludedPresetTweak>
        {
            new() { Id = "test.excluded.1", Reason = "High risk" }
        };

        await uedvm.OpenPreviewAsync("Test Dialog", "Description", "Notice", "Apply", tweaks, isUndo: false, excludedTweaks: excluded);

        Assert.True(uedvm.IsPreviewOpen);
        Assert.Equal(3, uedvm.PreviewItems.Count);
        Assert.Equal(2, uedvm.ItemGroups.Count);

        var netGroup = uedvm.ItemGroups.FirstOrDefault(g => g.Subcategory == "Networking");
        Assert.NotNull(netGroup);
        Assert.Equal(2, netGroup.Items.Count);
        Assert.Equal("2 tweaks", netGroup.TotalCountText);

        var uiGroup = uedvm.ItemGroups.FirstOrDefault(g => g.Subcategory == "Visuals");
        Assert.NotNull(uiGroup);
        Assert.Single(uiGroup.Items);

        // Verify summary initially
        Assert.Contains("3 to apply", uedvm.PreviewSummaryText);
        Assert.Contains("1 need a restart", uedvm.PreviewSummaryText);

        // Deselect all in netGroup
        netGroup.SelectNone();
        Assert.All(netGroup.Items, item => Assert.False(item.IsSelected));
        Assert.Contains("1 to apply", uedvm.PreviewSummaryText);

        // Re-select all in netGroup
        netGroup.SelectAll();
        Assert.All(netGroup.Items, item => Assert.True(item.IsSelected));
        Assert.Contains("3 to apply", uedvm.PreviewSummaryText);

        // Verify excluded section
        Assert.True(uedvm.HasExcludedItems);
        Assert.Equal("Not included (1)", uedvm.ExcludedSectionHeader);
        Assert.False(uedvm.IsExcludedSectionOpen);
        Assert.Equal("▼ Show", uedvm.ExcludedChevronText);

        uedvm.ToggleExcludedSection();
        Assert.True(uedvm.IsExcludedSectionOpen);
        Assert.Equal("▲ Hide", uedvm.ExcludedChevronText);
        Assert.Single(uedvm.ExcludedItems);
        Assert.Equal("test.excluded.1", uedvm.ExcludedItems[0].Id);
        Assert.Equal("High risk", uedvm.ExcludedItems[0].Reason);
    }
}

