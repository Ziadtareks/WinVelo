using System.Text.Encodings.Web;
using System.Text.Json;
using Tweakora.Core.Models;

namespace PresetGenerator;

public static class Program
{
    public sealed record PresetSpec(string Id, string Name, string Description, string FileName, string[] Tags, bool SafeOnly);

    public static readonly PresetSpec[] Specs = new[]
    {
        new PresetSpec(
            "preset.gaming",
            "Gaming: Fewer Background Interruptions",
            "Turns off Game Bar and background game recording, keeps Game Mode on, and stops the Sticky Keys shortcut from interrupting games.",
            "gaming.json",
            new[] { "gaming" },
            false
        ),
        new PresetSpec(
            "preset.privacy",
            "Privacy: Telemetry & Activity Shield",
            "Minimizes diagnostic collection, tailored experiences, advertising identifiers, and activity history tracking.",
            "privacy.json",
            new[] { "privacy" },
            false
        ),
        new PresetSpec(
            "preset.ai",
            "AI & Copilot Management",
            "Turns off Copilot, Recall snapshots, Click to Do, Bing web results in search, and AI features in Paint and Notepad.",
            "ai.json",
            new[] { "ai" },
            false
        ),
        new PresetSpec(
            "preset.clean-ui",
            "Clean UI: Distraction-Free Shell",
            "Removes taskbar clutter, ads and suggestions in Start, File Explorer and the lock screen, and restores the classic right-click menu.",
            "clean-ui.json",
            new[] { "ui" },
            false
        ),
        new PresetSpec(
            "preset.performance",
            "Performance: Fewer Background Tasks",
            "Reduces background work such as Edge preloading, maintenance wake-ups and indexing on battery, and turns off visual effects that cost resources.",
            "performance.json",
            new[] { "performance" },
            false
        ),
        new PresetSpec(
            "preset.recommended",
            "Recommended Baseline",
            "Only Safe tweaks: turns off ads, tips and tracking, shows file extensions, restores the classic right-click menu, and reduces animations and transparency.",
            "recommended.json",
            new[] { "privacy", "ui", "ai", "apps" },
            true
        )
    };

    /// <summary>Reasons shown in the preview dialog for tweaks whose presetRole keeps them out of presets.</summary>
    private static readonly Dictionary<string, string> RoleReasons = new(StringComparer.OrdinalIgnoreCase)
    {
        ["preference"] = "Personal preference — choose it yourself in Tweaks",
        ["fix"] = "Fix for a specific problem — apply it only if you have that problem",
        ["action"] = "One-time action — run it from Tweaks when you need it",
        ["advanced"] = "Advanced tuning — apply it individually after reading the details",
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
            // Deterministic order: tweaks are considered alphabetically, so when two tweaks set the same
            // registry value to different data, the first one wins and the other is listed as excluded.
            var candidates = allTweaks
                .Where(t => t.Tags.Any(tag => spec.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase)))
                .OrderBy(t => t.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var standard = new List<string>();
            var full = new List<string>();
            var excluded = new List<ExcludedPresetTweak>();

            // target -> (value, owning tweak id) for every tweak already accepted into this preset
            var acceptedTargets = new Dictionary<string, (string Value, string TweakId)>(StringComparer.OrdinalIgnoreCase);

            foreach (var tweak in candidates)
            {
                if (IsExcluded(spec, tweak, out string reason))
                {
                    excluded.Add(new ExcludedPresetTweak { Id = tweak.Id, Reason = reason });
                    continue;
                }

                var signature = GetApplySignature(tweak);
                string? conflictWith = null;
                foreach (var (target, value) in signature)
                {
                    if (acceptedTargets.TryGetValue(target, out var existing) &&
                        !string.Equals(existing.Value, value, StringComparison.OrdinalIgnoreCase))
                    {
                        conflictWith = existing.TweakId;
                        break;
                    }
                }

                if (conflictWith != null)
                {
                    excluded.Add(new ExcludedPresetTweak
                    {
                        Id = tweak.Id,
                        Reason = $"Conflicts with {conflictWith} (changes the same setting differently)"
                    });
                    continue;
                }

                bool isStandardRisk =
                    string.Equals(tweak.Risk, "Safe", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(tweak.Risk, "Low", StringComparison.OrdinalIgnoreCase);
                bool isMedium = string.Equals(tweak.Risk, "Medium", StringComparison.OrdinalIgnoreCase);

                if (isStandardRisk)
                {
                    standard.Add(tweak.Id);
                    full.Add(tweak.Id);
                }
                else if (isMedium)
                {
                    full.Add(tweak.Id);
                }
                else
                {
                    continue;
                }

                foreach (var (target, value) in signature)
                {
                    acceptedTargets.TryAdd(target, (value, tweak.Id));
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

    /// <summary>Registry/service targets written by a tweak's apply actions, with the value each one is set to.</summary>
    public static List<(string Target, string Value)> GetApplySignature(TweakDefinition tweak)
    {
        var list = new List<(string, string)>();
        foreach (var a in tweak.Apply)
        {
            if (!string.IsNullOrWhiteSpace(a.ServiceName))
            {
                list.Add(($"SVC:{a.ServiceName.Trim().ToUpperInvariant()}", (a.StartupType ?? string.Empty).Trim()));
                continue;
            }

            if (string.IsNullOrWhiteSpace(a.Hive) && string.IsNullOrWhiteSpace(a.Key))
            {
                continue;
            }

            string hive = (a.Hive ?? string.Empty).Trim().ToUpperInvariant()
                .Replace("HKEY_LOCAL_MACHINE", "HKLM")
                .Replace("HKEY_CURRENT_USER", "HKCU");
            string key = System.Text.RegularExpressions.Regex.Replace(a.Key ?? string.Empty, @"\\+", "\\")
                .Trim().Trim('\\').ToUpperInvariant();
            string valueName = string.IsNullOrWhiteSpace(a.ValueName) ? "@DEFAULT" : a.ValueName.Trim().ToUpperInvariant();
            string data = a.Data.HasValue ? a.Data.Value.ToString() : string.Empty;
            list.Add(($"REG:{hive}\\{key}\\{valueName}", $"{a.Operation}:{data}"));
        }

        return list;
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

        if (!string.Equals(tweak.PresetRole, "optimization", StringComparison.OrdinalIgnoreCase))
        {
            reason = RoleReasons.TryGetValue(tweak.PresetRole ?? string.Empty, out var roleReason)
                ? roleReason
                : "Not classified for presets yet — apply it individually from Tweaks";
            return true;
        }

        if (spec.SafeOnly && !string.Equals(tweak.Risk, "Safe", StringComparison.OrdinalIgnoreCase))
        {
            reason = "Recommended Baseline only includes Safe tweaks — find it in the topic presets or Tweaks";
            return true;
        }

        reason = string.Empty;
        return false;
    }
}
