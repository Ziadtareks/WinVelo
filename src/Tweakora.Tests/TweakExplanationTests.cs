using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Tweakora.App.ViewModels;
using Tweakora.Core.Engine;
using Tweakora.Core.Models;
using Tweakora.Core.Operations;
using Tweakora.Core.Safety;
using Tweakora.Modules.Tweaks;
using Tweakora.Simulation;
using Xunit;

namespace Tweakora.Tests;

public sealed class TweakExplanationTests
{
    private static string GetRepoRoot()
    {
        string dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "Tweakora.sln"))) return dir;
            dir = Path.GetDirectoryName(dir)!;
        }
        throw new InvalidOperationException("Could not locate repository root.");
    }

    private static List<TweakDefinition> LoadAllTweaks()
    {
        string tweaksDir = Path.Combine(GetRepoRoot(), "Catalog", "Tweaks");
        var list = new List<TweakDefinition>();
        foreach (var file in Directory.GetFiles(tweaksDir, "*.json"))
        {
            var tweaks = JsonSerializer.Deserialize<List<TweakDefinition>>(
                File.ReadAllText(file),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (tweaks != null) list.AddRange(tweaks);
        }
        return list;
    }

    [Fact]
    public void AllTweaks_HaveWhatItDoes_WithSufficientExplanation()
    {
        var tweaks = LoadAllTweaks();
        Assert.Equal(149, tweaks.Count);

        foreach (var t in tweaks)
        {
            Assert.False(string.IsNullOrWhiteSpace(t.WhatItDoes),
                $"Tweak '{t.Id}' is missing 'whatItDoes'.");
            Assert.True(t.WhatItDoes!.Trim().Length >= 10,
                $"Tweak '{t.Id}' whatItDoes is too short (< 10 chars): '{t.WhatItDoes}'.");
        }
    }

    [Fact]
    public void AllTweaks_HaveValidSources_WithAtLeastOneHttpUrl()
    {
        var tweaks = LoadAllTweaks();
        Assert.Equal(149, tweaks.Count);

        foreach (var t in tweaks)
        {
            Assert.NotNull(t.Sources);
            Assert.NotEmpty(t.Sources!);
            foreach (var s in t.Sources!)
            {
                Assert.True(s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                            s.StartsWith("https://", StringComparison.OrdinalIgnoreCase),
                    $"Tweak '{t.Id}' has invalid source URL: '{s}'.");
            }
        }
    }

    [Fact]
    public void NonSafeTweaks_HaveSpecificSideEffects_ForbiddenNoneKnown()
    {
        var tweaks = LoadAllTweaks();
        var nonSafe = tweaks.Where(t => !string.Equals(t.Risk, "Safe", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.NotEmpty(nonSafe);

        foreach (var t in nonSafe)
        {
            Assert.False(string.IsNullOrWhiteSpace(t.SideEffects),
                $"Non-Safe tweak '{t.Id}' (risk: {t.Risk}) is missing 'sideEffects'.");

            string trimmed = t.SideEffects!.Trim().TrimEnd('.');
            Assert.False(string.Equals(trimmed, "None known", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(trimmed, "None", StringComparison.OrdinalIgnoreCase),
                $"Non-Safe tweak '{t.Id}' (risk: {t.Risk}) has forbidden '{t.SideEffects}' sideEffects.");
        }
    }

    [Fact]
    public void SafeTweaks_HaveValidSideEffects()
    {
        var tweaks = LoadAllTweaks();
        var safe = tweaks.Where(t => string.Equals(t.Risk, "Safe", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.NotEmpty(safe);

        foreach (var t in safe)
        {
            Assert.False(string.IsNullOrWhiteSpace(t.SideEffects),
                $"Safe tweak '{t.Id}' is missing 'sideEffects'.");
        }
    }

    [Fact]
    public void NeedsReviewTweaks_AreExcludedFromAllPresets()
    {
        var tweaks = LoadAllTweaks();
        var needsReview = tweaks.Where(t => t.NeedsReview).ToList();
        Assert.Equal(5, needsReview.Count);

        string presetsDir = Path.Combine(GetRepoRoot(), "Catalog", "Presets");
        var presetFiles = Directory.GetFiles(presetsDir, "*.json");
        Assert.NotEmpty(presetFiles);

        foreach (var pFile in presetFiles)
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(pFile));
            var tweakIds = doc.RootElement.GetProperty("tweakIds")
                .EnumerateArray()
                .Select(x => x.GetString())
                .Where(x => !string.IsNullOrEmpty(x))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var nr in needsReview)
            {
                Assert.DoesNotContain(nr.Id, tweakIds);
            }
        }
    }

    [Fact]
    public void TweakCardModel_ExposesExplanationsAndToggleDrawer()
    {
        var def = new TweakDefinition
        {
            Id = "test.tweak",
            Name = "Test Tweak",
            Description = "Test description",
            Category = "performance",
            Risk = "Low",
            WhatItDoes = "Explains what the tweak actually configures.",
            SideEffects = "May cause mild battery degradation.",
            Sources = new List<string> { "https://learn.microsoft.com/test" },
            NeedsReview = true,
            Reversible = true
        };

        var card = new TweakCardModel(def);

        Assert.Equal("Explains what the tweak actually configures.", card.WhatItDoes);
        Assert.Equal("May cause mild battery degradation.", card.SideEffects);
        Assert.Single(card.Sources);
        Assert.Equal("https://learn.microsoft.com/test", card.Sources[0]);
        Assert.True(card.HasSources);
        Assert.True(card.NeedsReview);
        Assert.False(card.IsExpanded);

        card.ToggleExpandCommand.Execute(null);
        Assert.True(card.IsExpanded);

        card.ToggleExpandCommand.Execute(null);
        Assert.False(card.IsExpanded);
    }

    [Fact]
    public void TweaksViewModel_RecommendedOnlyFilter_ShowsOnlySafeAndLowDocumentedReversible()
    {
        var fakeReg = new FakeRegistryService();
        var fakeServices = new FakeServicesService();
        var fakeTasks = new FakeTaskSchedulerService();
        var fakeRestore = new FakeRestoreService();
        var fakeShell = new FakeShellService();
        string tempJournal = Path.Combine(Path.GetTempPath(), "Tweakora_ExplanationTest_" + Guid.NewGuid().ToString("N"));
        var journal = new RollbackJournalManager(tempJournal);
        var runner = new OperationRunner();

        var tweakService = new TweakService(fakeReg, fakeServices, fakeTasks, fakeRestore, fakeShell, journal);
        var vm = new TweaksViewModel(tweakService, runner);

        Assert.False(vm.IsRecommendedOnly);
        int totalBefore = vm.FilteredTweaks.Count;
        Assert.True(totalBefore > 0);

        vm.IsRecommendedOnly = true;

        foreach (var card in vm.FilteredTweaks)
        {
            Assert.True(string.Equals(card.Risk, "Safe", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(card.Risk, "Low", StringComparison.OrdinalIgnoreCase),
                $"Recommended tweak '{card.Id}' has risk '{card.Risk}'. Expected Safe or Low.");

            Assert.Equal("documented", card.Evidence);
            Assert.False(card.NeedsReview, $"Recommended tweak '{card.Id}' has NeedsReview=true.");
            Assert.True(card.Tweak.Reversible, $"Recommended tweak '{card.Id}' is irreversible.");
        }

        // Toggle back off
        vm.ToggleRecommendedOnlyCommand.Execute(null);
        Assert.False(vm.IsRecommendedOnly);
        Assert.Equal(totalBefore, vm.FilteredTweaks.Count);
    }
}
