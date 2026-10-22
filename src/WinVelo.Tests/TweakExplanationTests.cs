using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using WinVelo.App.ViewModels;
using WinVelo.Core.Engine;
using WinVelo.Core.Models;
using WinVelo.Core.Operations;
using WinVelo.Core.Safety;
using WinVelo.Modules.Tweaks;
using WinVelo.Simulation;
using Xunit;

namespace WinVelo.Tests;

public sealed class TweakExplanationTests
{
    private static string GetRepoRoot()
    {
        string dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "WinVelo.sln"))) return dir;
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
        Assert.Equal(142, tweaks.Count);

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
        Assert.Equal(142, tweaks.Count);

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
            var tweakIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (doc.RootElement.TryGetProperty("full", out var fullProp))
            {
                foreach (var x in fullProp.EnumerateArray())
                {
                    string? s = x.GetString();
                    if (!string.IsNullOrEmpty(s)) tweakIds.Add(s);
                }
            }
            if (doc.RootElement.TryGetProperty("tweakIds", out var tProp))
            {
                foreach (var x in tProp.EnumerateArray())
                {
                    string? s = x.GetString();
                    if (!string.IsNullOrEmpty(s)) tweakIds.Add(s);
                }
            }

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
        string tempJournal = Path.Combine(Path.GetTempPath(), "WinVelo_ExplanationTest_" + Guid.NewGuid().ToString("N"));
        var journal = new RollbackJournalManager(tempJournal);
        var runner = new OperationRunner();

        var tweakService = new TweakService(fakeReg, fakeServices, fakeTasks, fakeRestore, fakeShell, journal);
        var vm = new TweaksViewModel(tweakService, runner);

        Assert.True(vm.IsRecommendedOnly);
        int recBefore = vm.FilteredTweaks.Count;
        Assert.True(recBefore > 0);

        foreach (var card in vm.FilteredTweaks)
        {
            Assert.True(string.Equals(card.Risk, "Safe", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(card.Risk, "Low", StringComparison.OrdinalIgnoreCase),
                $"Recommended tweak '{card.Id}' has risk '{card.Risk}'. Expected Safe or Low.");

            Assert.True(string.Equals(card.Evidence, "documented", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(card.Evidence, "ui-equivalent", StringComparison.OrdinalIgnoreCase),
                $"Recommended tweak '{card.Id}' has evidence '{card.Evidence}'. Expected documented or ui-equivalent.");
            Assert.False(card.NeedsReview, $"Recommended tweak '{card.Id}' has NeedsReview=true.");
            Assert.True(card.Tweak.Reversible, $"Recommended tweak '{card.Id}' is irreversible.");
            Assert.NotEqual("Experimental", card.Category, StringComparer.OrdinalIgnoreCase);
        }

        // Toggle to "All" (Recommended only OFF)
        vm.ToggleRecommendedOnlyCommand.Execute(null);
        Assert.False(vm.IsRecommendedOnly);
        int totalBefore = vm.FilteredTweaks.Count;
        Assert.True(totalBefore > recBefore);

        // Verify sorting in "All": risk ascending (Safe -> Low -> Medium -> High), then by name
        int prevRisk = -1;
        string prevTitle = string.Empty;
        foreach (var card in vm.FilteredTweaks)
        {
            int r = card.Risk.ToLowerInvariant() switch
            {
                "safe" => 0,
                "low" => 1,
                "medium" or "moderate" => 2,
                "high" or "caution" => 3,
                _ => 4
            };
            Assert.True(r >= prevRisk, $"Card {card.Title} risk {card.Risk} appeared after risk rank {prevRisk}");
            if (r == prevRisk)
            {
                Assert.True(string.Compare(card.Title, prevTitle, StringComparison.OrdinalIgnoreCase) >= 0,
                    $"Card {card.Title} appeared out of alphabetical order after {prevTitle}");
            }
            prevRisk = r;
            prevTitle = card.Title;
        }

        // Toggle back to Recommended only
        vm.ToggleRecommendedOnlyCommand.Execute(null);
        Assert.True(vm.IsRecommendedOnly);
        Assert.Equal(recBefore, vm.FilteredTweaks.Count);
    }

    [Fact]
    public void AllTweaks_RegistryValueNamesAndNumbersInExplanations_MatchCatalogActions()
    {
        var tweaks = LoadAllTweaks();
        Assert.Equal(142, tweaks.Count);

        // Gather all registry value names across the entire catalog
        var allValNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in tweaks)
        {
            if (t.Detect?.ValueName != null) allValNames.Add(t.Detect.ValueName);
            if (t.Detect?.All != null)
            {
                foreach (var r in t.Detect.All) if (!string.IsNullOrEmpty(r.ValueName)) allValNames.Add(r.ValueName);
            }
            if (t.Detect?.Any != null)
            {
                foreach (var r in t.Detect.Any) if (!string.IsNullOrEmpty(r.ValueName)) allValNames.Add(r.ValueName);
            }
            foreach (var a in t.Apply.Concat(t.Undo))
            {
                if (!string.IsNullOrEmpty(a.ValueName)) allValNames.Add(a.ValueName);
            }
        }

        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "1", "0", "explorer", "copilot", "disabled", "enabled", "default", "start", "value"
        };
        var validCatalogValNames = allValNames.Where(v => !stopWords.Contains(v) && v.Length >= 3).ToList();

        var ignoredNumbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "10", "11", "7", "8", "260", "8.3", "30"
        };

        var errors = new List<string>();

        foreach (var t in tweaks)
        {
            string text = $"{t.WhatItDoes ?? string.Empty} {t.SideEffects ?? string.Empty}";

            var ownValNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var ownNumbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (t.Detect?.ValueName != null) ownValNames.Add(t.Detect.ValueName);
            if (t.Detect?.ExpectedValue != null) ownNumbers.Add(t.Detect.ExpectedValue.ToString()!);
            if (t.Detect?.All != null)
            {
                foreach (var r in t.Detect.All)
                {
                    if (!string.IsNullOrEmpty(r.ValueName)) ownValNames.Add(r.ValueName);
                    if (r.ExpectedValue != null) ownNumbers.Add(r.ExpectedValue.ToString()!);
                }
            }
            if (t.Detect?.Any != null)
            {
                foreach (var r in t.Detect.Any)
                {
                    if (!string.IsNullOrEmpty(r.ValueName)) ownValNames.Add(r.ValueName);
                    if (r.ExpectedValue != null) ownNumbers.Add(r.ExpectedValue.ToString()!);
                }
            }

            foreach (var a in t.Apply.Concat(t.Undo))
            {
                if (!string.IsNullOrEmpty(a.ValueName)) ownValNames.Add(a.ValueName);
                if (!string.IsNullOrEmpty(a.ServiceName)) ownValNames.Add(a.ServiceName);
                if (!string.IsNullOrEmpty(a.TaskName)) ownValNames.Add(a.TaskName);
                if (!string.IsNullOrEmpty(a.FeatureName)) ownValNames.Add(a.FeatureName);
                if (a.Data != null)
                {
                    string dStr = a.Data.ToString()!;
                    ownNumbers.Add(dStr);
                    if (long.TryParse(dStr, out long numVal))
                    {
                        ownNumbers.Add("0x" + numVal.ToString("x"));
                    }
                }
                if (!string.IsNullOrEmpty(a.Script))
                {
                    var numMatches = System.Text.RegularExpressions.Regex.Matches(a.Script, @"-?\b\d+\b");
                    foreach (System.Text.RegularExpressions.Match m in numMatches) ownNumbers.Add(m.Value);

                    var nameMatches = System.Text.RegularExpressions.Regex.Matches(a.Script, @"-(?:Name|Value)\s+[""']?([A-Za-z0-9_]+)[""']?", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    foreach (System.Text.RegularExpressions.Match m in nameMatches)
                    {
                        if (m.Groups.Count > 1) ownValNames.Add(m.Groups[1].Value);
                    }
                }
            }

            // Verify registry value names mentioned in explanation text belong to this tweak
            foreach (var valName in validCatalogValNames)
            {
                var reg = new System.Text.RegularExpressions.Regex($@"\b{System.Text.RegularExpressions.Regex.Escape(valName)}\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (reg.IsMatch(text) && !ownValNames.Contains(valName))
                {
                    errors.Add($"Tweak '{t.Id}' mentions foreign registry value '{valName}' not found in its actions.");
                }
            }

            // Verify numbers mentioned in explanation text
            // Strip domain constants
            string cleanedText = System.Text.RegularExpressions.Regex.Replace(text, @"\b(16|32|64)[ -]?bit\b", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            cleanedText = System.Text.RegularExpressions.Regex.Replace(cleanedText, @"\b1:1\b", "");
            cleanedText = System.Text.RegularExpressions.Regex.Replace(cleanedText, @"Shift\s+5\s+times", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            var numberMatches = System.Text.RegularExpressions.Regex.Matches(cleanedText, @"-?\b\d+(?:\.\d+)?\b|0x[0-9a-fA-F]+\b");
            foreach (System.Text.RegularExpressions.Match m in numberMatches)
            {
                string num = m.Value;
                if (ignoredNumbers.Contains(num)) continue;

                if (!ownNumbers.Contains(num))
                {
                    errors.Add($"Tweak '{t.Id}' mentions setting number '{num}' in explanation that does not appear in apply/undo actions.");
                }
            }
        }

        Assert.Empty(errors);
    }

    [Fact]
    public void TweakActionFormatter_FormatsExpectedSpecifications()
    {
        var tweak = new TweakDefinition
        {
            Id = "test.exact.changes",
            Name = "Test Exact Changes",
            Apply = new List<ActionItem>
            {
                new ActionItem
                {
                    Type = "Registry",
                    Hive = "HKLM",
                    Key = "SYSTEM\\CurrentControlSet\\Control\\PriorityControl",
                    ValueName = "Win32PrioritySeparation",
                    ValueKind = "DWord",
                    Data = JsonSerializer.SerializeToElement(38)
                },
                new ActionItem
                {
                    Type = "Service",
                    ServiceName = "DiagTrack",
                    StartupType = "Disabled"
                },
                new ActionItem
                {
                    Type = "PowerShell",
                    Script = "# Disable Defender Auto Sample Submission\nSet-MpPreference -SubmitSamplesConsent 2"
                }
            }
        };

        var changes = WinVelo.Core.Utilities.TweakActionFormatter.FormatExactChanges(tweak);
        Assert.Equal(3, changes.Count);
        Assert.Equal(@"HKLM\SYSTEM\CurrentControlSet\Control\PriorityControl\Win32PrioritySeparation = 38 (DWord)", changes[0]);
        Assert.Equal("Service 'DiagTrack' → StartupType: Disabled", changes[1]);
        Assert.Equal("PowerShell: Disable Defender Auto Sample Submission", changes[2]);
    }

    [Fact]
    public void DocumentedTweaks_HaveExplicitSourceQuotes()
    {
        var tweaks = LoadAllTweaks();
        var documentedTweaks = tweaks.Where(t => string.Equals(t.Evidence, "documented", StringComparison.OrdinalIgnoreCase)).ToList();

        Assert.NotEmpty(documentedTweaks);
        Assert.Equal(19, documentedTweaks.Count);

        foreach (var t in documentedTweaks)
        {
            Assert.False(string.IsNullOrWhiteSpace(t.SourceQuote),
                $"Tweak '{t.Id}' has evidence='documented' but missing or empty 'sourceQuote'.");
            Assert.True(t.SourceQuote!.Trim().Length >= 10,
                $"Tweak '{t.Id}' sourceQuote is too short (< 10 chars): '{t.SourceQuote}'.");
        }
    }

    [Fact]
    public void UiEquivalentTweaks_HaveNonEmptyUiLocation()
    {
        var tweaks = LoadAllTweaks();
        var uiTweaks = tweaks.Where(t => string.Equals(t.Evidence, "ui-equivalent", StringComparison.OrdinalIgnoreCase)).ToList();

        Assert.NotEmpty(uiTweaks);
        Assert.Equal(64, uiTweaks.Count);

        foreach (var t in uiTweaks)
        {
            Assert.False(string.IsNullOrWhiteSpace(t.UiEquivalentLocation),
                $"Tweak '{t.Id}' has evidence='ui-equivalent' but missing or empty 'uiEquivalentLocation'.");
            Assert.True(t.UiEquivalentLocation!.Trim().Length >= 5,
                $"Tweak '{t.Id}' uiEquivalentLocation is too short (< 5 chars): '{t.UiEquivalentLocation}'.");
        }
    }
}
