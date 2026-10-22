using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using WinVelo.Core.Models;
using Xunit;

namespace WinVelo.Tests.CatalogTests;

public sealed class WinUtilParityTests
{
    [Fact]
    public void StorageSenseDisable_CoversUpstreamButtonAndMirrorsEnableTweak()
    {
        var tweaks = LoadTweaks();
        var disable = Assert.Single(tweaks, t => string.Equals(t.Origin, "WPFTweaksStorage", StringComparison.OrdinalIgnoreCase));

        Assert.Equal("performance.system.storage-sense-disable", disable.Id);
        Assert.Equal(0, disable.Detect.ExpectedValue!.Value.GetInt32());
        Assert.Equal(0, Assert.Single(disable.Apply).Data!.Value.GetInt32());
        Assert.Equal(1, Assert.Single(disable.Undo).Data!.Value.GetInt32());
        Assert.Contains("performance.system.storage-sense", disable.OppositeOf!);

        var enable = Assert.Single(tweaks, t => t.Id == "performance.system.storage-sense");
        Assert.Contains(disable.Id, enable.OppositeOf!);
    }

    [Fact]
    public void ToolsTab_ExposesPowerPlanAndOfficialOosuLink()
    {
        string root = FindRepoRoot();
        string xaml = File.ReadAllText(Path.Combine(root, "src", "WinVelo.App", "Views", "ToolsTabView.xaml"));
        string viewModel = File.ReadAllText(Path.Combine(root, "src", "WinVelo.App", "ViewModels", "ToolsViewModel.cs"));

        Assert.Contains("AutomationProperties.AutomationId=\"Tool_PowerPlan\"", xaml);
        Assert.Contains("Command=\"{Binding ApplyPowerPlanCommand}\"", xaml);
        Assert.Contains("CommandParameter=\"Ultimate Performance\"", xaml);
        Assert.Contains("AutomationProperties.AutomationId=\"Tool_OOSU\"", xaml);
        Assert.Contains("Command=\"{Binding OpenOosuCommand}\"", xaml);
        Assert.Contains("SetPerformancePlanAsync", viewModel);
        Assert.Contains("https://www.oo-software.com/en/shutup10", viewModel);
    }

    [Fact]
    public void DocumentationAndOverrides_AreHonestAboutExclusionsAndCounts()
    {
        string root = FindRepoRoot();
        string overridesJson = File.ReadAllText(Path.Combine(root, "Catalog", "overrides.json"));
        using var overrides = JsonDocument.Parse(overridesJson);
        var excludedTweaks = overrides.RootElement.GetProperty("excludedTweaks");

        Assert.True(excludedTweaks.TryGetProperty("WPFToggleHideSettingsHome", out _));
        Assert.True(excludedTweaks.TryGetProperty("WPFOOSUbutton", out _));
        Assert.True(excludedTweaks.TryGetProperty("WPFAddUltPerf", out _));
        Assert.True(excludedTweaks.TryGetProperty("WPFRemoveUltPerf", out _));
        Assert.True(excludedTweaks.TryGetProperty("WPFchangedns", out _));

        string docs = File.ReadAllText(Path.Combine(root, "docs", "UPSTREAM.md"));
        Assert.Contains("2f0bdc8fca52e1ebe31a8d98216428061d3f8956", docs);
        Assert.Contains("68 upstream tweak/control keys", docs);
        Assert.Contains("Curated app catalog", docs);
        Assert.Contains("removes only WinVelo's managed plan", docs);
        Assert.DoesNotContain("Power Plan Dropdown", docs, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("100% ported across all 10 categories", docs, StringComparison.OrdinalIgnoreCase);

        string readme = File.ReadAllText(Path.Combine(root, "README.md"));
        Assert.Contains("badge/tweaks-142-", readme);
    }

    [Fact]
    public void Applications_CoverPinnedSourceAndPreserveCuratedAliases()
    {
        string root=FindRepoRoot();
        using var manifest=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"Catalog","SourceTracking","parity.json")));
        var apps=new WinVelo.Modules.Apps.AppInstallService(new WinVelo.Simulation.FakeShellService()).LoadApplications();
        Assert.Equal(242,apps.Count);
        var ids=apps.Select(app=>app.WingetId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach(var id in manifest.RootElement.GetProperty("applications").EnumerateArray()) Assert.Contains(id.GetString()!,ids);
        foreach(var alias in manifest.RootElement.GetProperty("curatedApplicationAliases").EnumerateObject())
            Assert.Equal(alias.Value.GetString(),apps.Single(app=>app.Key==alias.Name).WingetId);
        Assert.DoesNotContain(apps.GroupBy(app=>app.WingetId,StringComparer.OrdinalIgnoreCase),group=>group.Count()>1);
    }

    private static List<TweakDefinition> LoadTweaks()
    {
        string tweaksDir = Path.Combine(FindRepoRoot(), "Catalog", "Tweaks");
        var result = new List<TweakDefinition>();
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        foreach (string file in Directory.GetFiles(tweaksDir, "*.json", SearchOption.AllDirectories))
        {
            var tweaks = JsonSerializer.Deserialize<List<TweakDefinition>>(File.ReadAllText(file), options);
            if (tweaks != null)
            {
                result.AddRange(tweaks);
            }
        }

        return result;
    }

    private static string FindRepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir, "Catalog")) && File.Exists(Path.Combine(dir, "WinVelo.sln")))
            {
                return dir;
            }
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new InvalidOperationException("Could not locate the WinVelo repository root.");
    }
}
