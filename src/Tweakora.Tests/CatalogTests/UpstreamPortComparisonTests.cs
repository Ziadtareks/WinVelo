using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Tweakora.Core.Models;
using Xunit;

namespace Tweakora.Tests.CatalogTests;

public sealed class UpstreamPortComparisonTests
{
    [Fact]
    public void UpstreamTweaks_AllPortedOrExplicitlyDocumentedInUpstreamMd()
    {
        string baseDir = AppContext.BaseDirectory;
        string upstreamDir = FindDirectoryUpwards(baseDir, Path.Combine("upstream", "winutil", "config"));
        string tweaksDir = FindDirectoryUpwards(baseDir, Path.Combine("Catalog", "Tweaks"));
        string upstreamDocPath = FindFileUpwards(baseDir, Path.Combine("docs", "UPSTREAM.md"));

        Assert.True(Directory.Exists(upstreamDir), $"upstream dir not found at {upstreamDir}");
        Assert.True(Directory.Exists(tweaksDir), $"tweaks dir not found at {tweaksDir}");
        Assert.True(File.Exists(upstreamDocPath), $"docs/UPSTREAM.md not found at {upstreamDocPath}");

        string upstreamDoc = File.ReadAllText(upstreamDocPath);

        // Load upstream tweaks
        string upstreamTweaksPath = Path.Combine(upstreamDir, "tweaks.json");
        Assert.True(File.Exists(upstreamTweaksPath), $"upstream tweaks.json missing");
        string rawUpstreamJson = File.ReadAllText(upstreamTweaksPath);
        using var upstreamDocJson = JsonDocument.Parse(rawUpstreamJson);

        // Load our catalog tweaks
        List<TweakDefinition> ourTweaks = new();
        JsonSerializerOptions opts = new() { PropertyNameCaseInsensitive = true };
        foreach (var file in Directory.GetFiles(tweaksDir, "*.json", SearchOption.AllDirectories))
        {
            var list = JsonSerializer.Deserialize<List<TweakDefinition>>(File.ReadAllText(file), opts);
            if (list != null) ourTweaks.AddRange(list);
        }

        HashSet<string> ourOrigins = new(ourTweaks.Where(t => t.Origin != null).Select(t => t.Origin!), StringComparer.OrdinalIgnoreCase);
        HashSet<string> ourIds = new(ourTweaks.Select(t => t.Id), StringComparer.OrdinalIgnoreCase);
        HashSet<string> ourAliases = new(ourTweaks.Where(t => t.Aliases != null).SelectMany(t => t.Aliases!), StringComparer.OrdinalIgnoreCase);

        string overridesPath = FindFileUpwards(baseDir, Path.Combine("Catalog", "overrides.json"));
        Assert.True(File.Exists(overridesPath), $"Catalog/overrides.json missing at {overridesPath}");
        string rawOverrides = File.ReadAllText(overridesPath);
        using var overridesDoc = JsonDocument.Parse(rawOverrides);

        List<string> unportedAndUndocumented = new();

        foreach (var prop in upstreamDocJson.RootElement.EnumerateObject())
        {
            string key = prop.Name;
            bool isPorted = ourOrigins.Contains(key) || ourIds.Contains(key) || ourAliases.Contains(key) || DeprecatedTweaks.IsDeprecated(key);
            bool isDocumented = upstreamDoc.Contains(key, StringComparison.OrdinalIgnoreCase);

            if (!isPorted && !isDocumented)
            {
                unportedAndUndocumented.Add(key);
            }
        }

        Assert.Empty(unportedAndUndocumented);

        // Safety Assertions:
        // 1. BitLocker must be High risk and have a clear warning
        var bitLockerTweak = ourTweaks.FirstOrDefault(t => string.Equals(t.Origin, "WPFTweaksDisableBitLocker", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(bitLockerTweak);
        Assert.Equal("High", bitLockerTweak.Risk);
        Assert.NotNull(bitLockerTweak.Warnings);

        // 2. Presets must NOT contain BitLocker
        string presetPath = FindFileUpwards(baseDir, Path.Combine("Catalog", "Config", "preset.json"));
        Assert.True(File.Exists(presetPath));
        string presetText = File.ReadAllText(presetPath);
        Assert.DoesNotContain("WPFTweaksDisableBitLocker", presetText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UpstreamConfigs_AllFeaturesAppsPresetsDnsPresentInCatalog()
    {
        string baseDir = AppContext.BaseDirectory;
        string configDir = FindDirectoryUpwards(baseDir, Path.Combine("Catalog", "Config"));
        string appsDir = FindDirectoryUpwards(baseDir, Path.Combine("Catalog", "Apps"));

        Assert.True(Directory.Exists(configDir), $"Catalog/Config missing at {configDir}");
        Assert.True(Directory.Exists(appsDir), $"Catalog/Apps missing at {appsDir}");

        // Features
        string featurePath = Path.Combine(configDir, "feature.json");
        Assert.True(File.Exists(featurePath));
        using (var fDoc = JsonDocument.Parse(File.ReadAllText(featurePath)))
        {
            Assert.Equal(33, fDoc.RootElement.EnumerateObject().Count());
        }

        // Applications (Curated Famous Software Catalog)
        string appPath = Path.Combine(appsDir, "applications.json");
        Assert.True(File.Exists(appPath));
        using (var aDoc = JsonDocument.Parse(File.ReadAllText(appPath)))
        {
            Assert.True(aDoc.RootElement.EnumerateObject().Count() >= 32);
        }

        // Presets
        string presetPath = Path.Combine(configDir, "preset.json");
        Assert.True(File.Exists(presetPath));
        using (var pDoc = JsonDocument.Parse(File.ReadAllText(presetPath)))
        {
            Assert.True(pDoc.RootElement.TryGetProperty("Standard", out _));
            Assert.True(pDoc.RootElement.TryGetProperty("Minimal", out _));
            Assert.True(pDoc.RootElement.TryGetProperty("Advanced", out _));
        }

        // DNS
        string dnsPath = Path.Combine(configDir, "dns.json");
        Assert.True(File.Exists(dnsPath));
        using (var dDoc = JsonDocument.Parse(File.ReadAllText(dnsPath)))
        {
            Assert.True(dDoc.RootElement.EnumerateObject().Count() >= 8);
        }
    }

    private static string FindDirectoryUpwards(string startDir, string relativePath)
    {
        string? current = startDir;
        while (current != null)
        {
            string candidate = Path.Combine(current, relativePath);
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
            current = Directory.GetParent(current)?.FullName;
        }
        return Path.Combine(startDir, relativePath);
    }

    private static string FindFileUpwards(string startDir, string relativePath)
    {
        string? current = startDir;
        while (current != null)
        {
            string candidate = Path.Combine(current, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }
            current = Directory.GetParent(current)?.FullName;
        }
        return Path.Combine(startDir, relativePath);
    }
}
