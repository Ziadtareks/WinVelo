using System.Text.Encodings.Web;
using System.Text.Json;
using Tweakora.Core.Models;

namespace PresetGenerator;

public static class Program
{
    public sealed record PresetSpec(string Id, string Name, string Description, string FileName, string? Tag);

    public static readonly PresetSpec[] Specs = new[]
    {
        new PresetSpec(
            "preset.gaming",
            "Gaming: Fewer Background Interruptions",
            "Disables background Xbox Game Bar recording overlays, Game DVR background captures, and optimizes system responsiveness and raw input for gaming.",
            "gaming.json",
            "gaming"
        ),
        new PresetSpec(
            "preset.privacy",
            "Privacy: Telemetry & Activity Shield",
            "Minimizes diagnostic collection, tailored experiences, advertising identifiers, and activity history tracking.",
            "privacy.json",
            "privacy"
        ),
        new PresetSpec(
            "preset.ai",
            "AI & Copilot Management",
            "Disables Windows Copilot, Recall automated snapshots, and Bing web suggestions in shell search.",
            "ai.json",
            "ai"
        ),
        new PresetSpec(
            "preset.clean-ui",
            "Clean UI: Distraction-Free Shell",
            "Streamlines the Windows 11 interface with classic menus, file extensions, and distraction-free taskbar.",
            "clean-ui.json",
            "ui"
        ),
        new PresetSpec(
            "preset.performance",
            "Performance: System Responsiveness",
            "Optimizes background services, power management, storage cleanup, and system responsiveness.",
            "performance.json",
            "performance"
        ),
        new PresetSpec(
            "preset.recommended",
            "Recommended Baseline",
            "Safe, reversible baseline settings that balance privacy, system responsiveness, and clean UI without breaking any features.",
            "recommended.json",
            null // across all tags, Safe only
        )
    };

    public static int Main(string[] args)
    {
        string rootDir = Directory.GetCurrentDirectory();
        while (rootDir != null && !File.Exists(Path.Combine(rootDir, "Tweakora.sln")))
        {
            rootDir = Directory.GetParent(rootDir)?.FullName!;
        }

        if (rootDir == null)
        {
            Console.WriteLine("[ERROR] Could not find repository root containing Tweakora.sln");
            return 1;
        }

        string tweaksDir = Path.Combine(rootDir, "Catalog", "Tweaks");
        string presetsDir = Path.Combine(rootDir, "Catalog", "Presets");
        Directory.CreateDirectory(presetsDir);

        var allTweaks = LoadCatalogTweaks(tweaksDir);
        Console.WriteLine($"[INFO] Loaded {allTweaks.Count} catalog tweaks.");

        var generatedPresets = GeneratePresets(allTweaks);

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        Console.WriteLine("\n================ PRESET GENERATION REPORT ================");
        Console.WriteLine(string.Format("{0,-16} | {1,-8} | {2,-6} | {3,-8} | {4}", "Preset File", "Standard", "Full", "Excluded", "Preset Name"));
        Console.WriteLine(new string('-', 75));

        foreach (var (spec, preset) in generatedPresets)
        {
            string outPath = Path.Combine(presetsDir, spec.FileName);
            string json = JsonSerializer.Serialize(preset, options) + "\n";
            File.WriteAllText(outPath, json);

            Console.WriteLine(string.Format(
                "{0,-16} | {1,8} | {2,6} | {3,8} | {4}",
                spec.FileName,
                preset.Standard.Count,
                preset.Full.Count,
                preset.Excluded.Count,
                preset.Name
            ));
        }

        Console.WriteLine(new string('-', 75));
        Console.WriteLine("[SUCCESS] Presets generated successfully.");
        return 0;
    }

    public static List<TweakDefinition> LoadCatalogTweaks(string tweaksDir)
    {
        var list = new List<TweakDefinition>();
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        foreach (var file in Directory.GetFiles(tweaksDir, "*.json", SearchOption.AllDirectories).OrderBy(f => f))
        {
            string json = File.ReadAllText(file);
            var items = JsonSerializer.Deserialize<List<TweakDefinition>>(json, options);
            if (items != null)
            {
                list.AddRange(items);
            }
        }

        return list;
    }

    public static List<(PresetSpec Spec, PresetDefinition Preset)> GeneratePresets(List<TweakDefinition> allTweaks)
    {
        var result = new List<(PresetSpec, PresetDefinition)>();

        foreach (var spec in Specs)
        {
            var candidates = spec.Tag != null
                ? allTweaks.Where(t => t.Tags.Contains(spec.Tag, StringComparer.OrdinalIgnoreCase)).ToList()
                : allTweaks.ToList();

            var standard = new List<string>();
            var full = new List<string>();
            var excluded = new List<ExcludedPresetTweak>();

            foreach (var tweak in candidates)
            {
                if (IsExcluded(spec, tweak, out string reason))
                {
                    excluded.Add(new ExcludedPresetTweak
                    {
                        Id = tweak.Id,
                        Reason = reason
                    });
                }
                else
                {
                    if (string.Equals(tweak.Risk, "Safe", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(tweak.Risk, "Low", StringComparison.OrdinalIgnoreCase))
                    {
                        standard.Add(tweak.Id);
                        full.Add(tweak.Id);
                    }
                    else if (string.Equals(tweak.Risk, "Medium", StringComparison.OrdinalIgnoreCase) && spec.Tag != null)
                    {
                        full.Add(tweak.Id);
                    }
                }
            }

            var preset = new PresetDefinition
            {
                Id = spec.Id,
                Name = spec.Name,
                Description = spec.Description,
                Standard = standard.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList(),
                Full = full.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList(),
                Excluded = excluded.OrderBy(e => e.Id, StringComparer.OrdinalIgnoreCase).ToList()
            };

            result.Add((spec, preset));
        }

        return result;
    }

    private static bool IsExcluded(PresetSpec spec, TweakDefinition tweak, out string reason)
    {
        if (string.Equals(tweak.Category, "Experimental", StringComparison.OrdinalIgnoreCase))
        {
            reason = "Experimental tweak — requires manual testing";
            return true;
        }

        if (string.Equals(tweak.Risk, "High", StringComparison.OrdinalIgnoreCase))
        {
            reason = "High risk — enable individually from Tweaks";
            return true;
        }

        if (!tweak.Reversible)
        {
            reason = "Irreversible change — must be applied manually";
            return true;
        }

        if (tweak.NeedsReview)
        {
            reason = "Needs manual review before applying";
            return true;
        }

        if (spec.Tag == null)
        {
            // Recommended baseline: Safe only across all tags
            if (!string.Equals(tweak.Risk, "Safe", StringComparison.OrdinalIgnoreCase))
            {
                reason = "Requires elevated risk confirmation (Medium/Low risk)";
                return true;
            }
        }

        reason = string.Empty;
        return false;
    }
}
