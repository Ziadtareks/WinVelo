using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using WinVelo.Core.Models;
using WinVelo.Core.Safety;
using Xunit;

namespace WinVelo.Tests;

public sealed class RiskRubricTests
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
    public void Rubric_IrreversibleTweaks_MustBeHighRisk()
    {
        var tweaks = LoadAllTweaks();
        var irreversible = tweaks.Where(t => !t.Reversible).ToList();
        Assert.NotEmpty(irreversible);

        foreach (var t in irreversible)
        {
            Assert.True(string.Equals(t.Risk, "High", StringComparison.OrdinalIgnoreCase),
                $"Irreversible tweak '{t.Id}' must have risk 'High', found '{t.Risk}'.");
        }
    }

    [Fact]
    public void Rubric_ServicingTouchingTweaks_MustBeHighRisk()
    {
        var tweaks = LoadAllTweaks();

        foreach (var t in tweaks)
        {
            bool touchesServicing = false;
            foreach (var a in t.Apply)
            {
                if (string.Equals(a.Type, "Service", StringComparison.OrdinalIgnoreCase) &&
                    ServicingProtectionHelper.IsProtectedService(a.ServiceName ?? string.Empty))
                {
                    touchesServicing = true;
                    break;
                }
                if (string.Equals(a.Type, "Registry", StringComparison.OrdinalIgnoreCase) &&
                    ServicingProtectionHelper.IsProtectedPolicy(a.Key ?? string.Empty, a.ValueName ?? string.Empty))
                {
                    touchesServicing = true;
                    break;
                }
            }

            if (touchesServicing)
            {
                Assert.True(string.Equals(t.Risk, "High", StringComparison.OrdinalIgnoreCase),
                    $"Servicing-touching tweak '{t.Id}' must have risk 'High', found '{t.Risk}'.");
            }
        }
    }

    [Fact]
    public void Rubric_HklmTouchingTweaks_MustBeAtLeastLowRisk()
    {
        var tweaks = LoadAllTweaks();

        foreach (var t in tweaks)
        {
            bool touchesHklm = false;
            foreach (var a in t.Apply)
            {
                if (string.Equals(a.Hive, "HKLM", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(a.Hive, "HKEY_LOCAL_MACHINE", StringComparison.OrdinalIgnoreCase))
                {
                    touchesHklm = true;
                    break;
                }
            }

            if (!touchesHklm && t.Detect != null)
            {
                if (string.Equals(t.Detect.Hive, "HKLM", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(t.Detect.Hive, "HKEY_LOCAL_MACHINE", StringComparison.OrdinalIgnoreCase))
                {
                    touchesHklm = true;
                }
            }

            if (touchesHklm)
            {
                Assert.False(string.Equals(t.Risk, "Safe", StringComparison.OrdinalIgnoreCase),
                    $"Tweak '{t.Id}' touches HKLM machine policy but is marked as 'Safe'. Machine policy tweaks must be at least 'Low'.");
            }
        }
    }

    [Fact]
    public void Rubric_DocumentationAndAuditLog_ExistAndAreComprehensive()
    {
        string root = GetRepoRoot();
        string guidePath = Path.Combine(root, "docs", "TWEAK_GUIDE.md");
        string auditPath = Path.Combine(root, "docs", "RISK_AUDIT.md");

        Assert.True(File.Exists(guidePath), "TWEAK_GUIDE.md must exist.");
        Assert.True(File.Exists(auditPath), "RISK_AUDIT.md must exist.");

        string guideText = File.ReadAllText(guidePath);
        Assert.Contains("Risk Rating Rubric", guideText);
        Assert.Contains("Safe", guideText);
        Assert.Contains("Low", guideText);
        Assert.Contains("Medium", guideText);
        Assert.Contains("High", guideText);

        string auditText = File.ReadAllText(auditPath);
        Assert.Contains("Risk Recalibration Audit", auditText);
        Assert.Contains("Distribution Before vs After", auditText);
        Assert.Contains("Detailed Log of Risk Changes", auditText);
    }
}
