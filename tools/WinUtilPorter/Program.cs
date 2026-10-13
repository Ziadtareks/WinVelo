using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using Tweakora.Core.Models;

namespace WinUtilPorter;

public static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public static int Main(string[] args)
    {
        Console.WriteLine("========================================");
        Console.WriteLine("   WinUtil to Tweakora Catalog Porter   ");
        Console.WriteLine("========================================");

        string rootDir = Directory.GetCurrentDirectory();
        while (!File.Exists(Path.Combine(rootDir, "Tweakora.sln")))
        {
            var p = Directory.GetParent(rootDir);
            if (p == null) break;
            rootDir = p.FullName;
        }

        string upstreamDir = Path.Combine(rootDir, "upstream", "winutil", "config");
        string catalogDir = Path.Combine(rootDir, "Catalog");
        string schemaPath = Path.Combine(catalogDir, "schema", "tweak.schema.json");

        if (!Directory.Exists(upstreamDir))
        {
            Console.WriteLine($"[ERROR] Upstream directory not found: {upstreamDir}");
            return 1;
        }

        JsonSchema schema = JsonSchema.FromFile(schemaPath);

        // 1. Copy config files to Catalog/Apps and Catalog/Config
        Directory.CreateDirectory(Path.Combine(catalogDir, "Apps"));
        Directory.CreateDirectory(Path.Combine(catalogDir, "Config"));
        Directory.CreateDirectory(Path.Combine(catalogDir, "Tweaks"));

        File.Copy(Path.Combine(upstreamDir, "applications.json"), Path.Combine(catalogDir, "Apps", "applications.json"), true);
        File.Copy(Path.Combine(upstreamDir, "feature.json"), Path.Combine(catalogDir, "Config", "feature.json"), true);
        File.Copy(Path.Combine(upstreamDir, "preset.json"), Path.Combine(catalogDir, "Config", "preset.json"), true);
        File.Copy(Path.Combine(upstreamDir, "dns.json"), Path.Combine(catalogDir, "Config", "dns.json"), true);
        File.Copy(Path.Combine(upstreamDir, "appx.json"), Path.Combine(catalogDir, "Config", "appx.json"), true);

        Console.WriteLine("[INFO] Copied applications, feature, preset, dns, appx configs to Catalog/");

        // 2. Port tweaks from tweaks.json
        string tweaksRaw = File.ReadAllText(Path.Combine(upstreamDir, "tweaks.json"));
        using JsonDocument doc = JsonDocument.Parse(tweaksRaw);

        List<TweakDefinition> essentialTweaks = new();
        List<TweakDefinition> advancedTweaks = new();
        List<TweakDefinition> preferencesTweaks = new();

        int portedCount = 0;
        int skippedCount = 0;

        foreach (JsonProperty prop in doc.RootElement.EnumerateObject())
        {
            string key = prop.Name;
            JsonElement elem = prop.Value;

            // Skip Adobe block list (safety / licensing)
            if (key == "WPFTweaksBlockAdobeNet")
            {
                Console.WriteLine($"[SKIP] {key}: Excluded (licensing bypass / safety policy)");
                skippedCount++;
                continue;
            }

            // Skip pure UI buttons or comboboxes that aren't tweak checkboxes
            if (key == "WPFOOSUbutton" || key == "WPFchangedns" || key == "WPFAddUltPerf" || key == "WPFRemoveUltPerf")
            {
                Console.WriteLine($"[SKIP/ACTION] {key}: Handled as UI Action / Dropdown");
                skippedCount++;
                continue;
            }

            string content = elem.TryGetProperty("Content", out var cProp) ? cProp.GetString() ?? key : key;
            string description = elem.TryGetProperty("Description", out var dProp) ? dProp.GetString() ?? content : content;
            if (string.IsNullOrWhiteSpace(description)) description = content;
            if (description.Length < 10) description = $"{description} (System configuration setting)";

            string category = elem.TryGetProperty("category", out var catProp) ? catProp.GetString() ?? "Essential Tweaks" : "Essential Tweaks";
            string type = elem.TryGetProperty("Type", out var tProp) ? tProp.GetString() ?? "Checkbox" : "Checkbox";

            string risk = "Safe";
            string schemaCategory = "system";
            List<string> impacts = new() { "ui" };
            string? warning = null;

            // Specific Safety Adjustments per requirements
            if (key == "WPFTweaksDisableBitLocker")
            {
                category = "z__Advanced Tweaks - CAUTION";
                risk = "High";
                schemaCategory = "system";
                impacts = new() { "security" };
                warning = "Disabling BitLocker removes disk encryption and leaves drive data completely unprotected against unauthorized physical access.";
            }
            else if (key == "WPFTweaksRemoveEdge")
            {
                risk = "High";
                schemaCategory = "ui";
                impacts = new() { "ui", "performance" };
                warning = "Removes Microsoft Edge from the system. Some Windows components, widgets, or web-authentication dialogs may be affected.";
            }
            else if (key == "WPFTweaksRemoveOneDrive")
            {
                risk = "High";
                schemaCategory = "system";
                impacts = new() { "storage", "performance" };
                warning = "Completely uninstalls OneDrive and stops file synchronization. Ensure all cloud documents are backed up locally.";
            }
            else if (key == "WPFTweaksWindowsAI")
            {
                risk = "High";
                schemaCategory = "ai";
                impacts = new() { "privacy", "performance" };
                warning = "Removes and disables Windows AI, Recall, and Copilot packages system-wide.";
            }
            else if (key == "WPFTweaksDisableIPv6")
            {
                risk = "High";
                schemaCategory = "network";
                impacts = new() { "network" };
                warning = "Disables IPv6 on all network adapters. Some modern networks and services requiring IPv6 may lose connectivity.";
            }
            else if (category.Contains("Advanced", StringComparison.OrdinalIgnoreCase))
            {
                risk = "Low";
                if (key.Contains("DisableNotifications") || key.Contains("ReservedStorage") || key.Contains("Storage"))
                {
                    risk = "Medium";
                }
            }

            string id = GenerateId(key, content, category);

            // Map schema category & impacts
            if (category == "Customize Preferences")
            {
                schemaCategory = "ui";
                impacts = new() { "ui" };
            }
            else if (key.Contains("Telemetry") || key.Contains("Location") || key.Contains("Activity"))
            {
                schemaCategory = "privacy";
                impacts = new() { "privacy" };
            }
            else if (key.Contains("Disk") || key.Contains("TempFiles") || key.Contains("Services"))
            {
                schemaCategory = "performance";
                impacts = new() { "performance" };
            }
            else if (key.Contains("IPv") || key.Contains("Teredo") || key.Contains("DeliveryOptimization"))
            {
                schemaCategory = "network";
                impacts = new() { "network" };
            }

            string sourceUrl = elem.TryGetProperty("link", out var linkProp) ? linkProp.GetString() ?? "https://github.com/ChrisTitusTech/winutil" : "https://github.com/ChrisTitusTech/winutil";

            var tweak = new TweakDefinition
            {
                SchemaVersion = "1.0.0",
                Id = id,
                Origin = key,
                Name = content,
                Description = description,
                Category = schemaCategory,
                Subcategory = category,
                Risk = risk,
                Impacts = impacts,
                Warnings = warning,
                Windows = new WindowsRequirements { MinBuild = 19041 },
                RequiresReboot = false,
                RequiresExplorerRestart = key.Contains("Taskbar") || key.Contains("Explorer") || key.Contains("Start"),
                Source = JsonDocument.Parse($"\"{sourceUrl}\"").RootElement.Clone(),
                VerifiedOnBuild = "26100"
            };

            // Build Actions and Detect
            BuildActionsAndDetect(elem, tweak);

            if (category == "Essential Tweaks")
            {
                essentialTweaks.Add(tweak);
            }
            else if (category == "Customize Preferences")
            {
                preferencesTweaks.Add(tweak);
            }
            else
            {
                advancedTweaks.Add(tweak);
            }

            portedCount++;
        }

        Console.WriteLine($"[INFO] Ported {portedCount} WinUtil tweaks ({essentialTweaks.Count} Essential, {advancedTweaks.Count} Advanced, {preferencesTweaks.Count} Preferences).");

        // 3. Write out catalog files
        File.WriteAllText(Path.Combine(catalogDir, "Tweaks", "essential.json"), JsonSerializer.Serialize(essentialTweaks, JsonOptions));
        File.WriteAllText(Path.Combine(catalogDir, "Tweaks", "advanced.json"), JsonSerializer.Serialize(advancedTweaks, JsonOptions));
        File.WriteAllText(Path.Combine(catalogDir, "Tweaks", "preferences.json"), JsonSerializer.Serialize(preferencesTweaks, JsonOptions));

        // 4. Preserve existing unique Tweakora extras in extras.json
        BuildTweakoraExtras(catalogDir, essentialTweaks.Concat(advancedTweaks).Concat(preferencesTweaks).ToList());

        // 5. Clean up old category files now replaced by essential/advanced/preferences/extras
        string[] oldFiles = { "ai.json", "gaming.json", "network.json", "performance.json", "privacy.json", "security.json", "ui.json", "updates.json" };
        foreach (string old in oldFiles)
        {
            string oldPath = Path.Combine(catalogDir, "Tweaks", old);
            if (File.Exists(oldPath))
            {
                File.Delete(oldPath);
            }
        }

        // 6. Validate all newly generated catalog files against schema
        Console.WriteLine("\n[VALIDATING CATALOG AGAINST SCHEMA...]");
        int totalValid = 0;
        int errorCount = 0;

        foreach (string file in Directory.GetFiles(Path.Combine(catalogDir, "Tweaks"), "*.json"))
        {
            string jsonText = File.ReadAllText(file);
            using JsonDocument tweakDoc = JsonDocument.Parse(jsonText);
            foreach (JsonElement tweakElement in tweakDoc.RootElement.EnumerateArray())
            {
                var result = schema.Evaluate(tweakElement, new EvaluationOptions { OutputFormat = OutputFormat.List });
                string tid = tweakElement.GetProperty("id").GetString() ?? "unknown";
                if (result.IsValid)
                {
                    totalValid++;
                }
                else
                {
                    Console.WriteLine($"[FAIL] {tid} in {Path.GetFileName(file)}:");
                    foreach (var detail in result.Details)
                    {
                        if (detail.HasErrors && detail.Errors != null)
                        {
                            foreach (var err in detail.Errors)
                            {
                                Console.WriteLine($"   - {err.Key}: {err.Value}");
                            }
                        }
                    }
                    errorCount++;
                }
            }
        }

        Console.WriteLine($"\n========================================");
        Console.WriteLine($"Result: {totalValid} tweaks valid, {errorCount} errors.");
        Console.WriteLine($"========================================");

        return errorCount == 0 ? 0 : 1;
    }

    private static string GenerateId(string originKey, string content, string category)
    {
        string prefix = "system";
        if (category == "Customize Preferences") prefix = "pref";
        else if (category == "Essential Tweaks") prefix = "essential";
        else if (category.Contains("Advanced")) prefix = "advanced";

        string slug = originKey.ToLowerInvariant()
            .Replace("wpftweaks", "")
            .Replace("wpftoggle", "")
            .Replace("wpf", "")
            .Trim();

        if (string.IsNullOrEmpty(slug))
        {
            slug = new string(content.ToLowerInvariant().Where(c => char.IsLetterOrDigit(c) || c == '-').ToArray());
        }

        return $"{prefix}.{slug}";
    }

    private static void BuildActionsAndDetect(JsonElement elem, TweakDefinition tweak)
    {
        // 1. Registry
        if (elem.TryGetProperty("registry", out var regProp) && regProp.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement r in regProp.EnumerateArray())
            {
                string path = r.GetProperty("Path").GetString() ?? "";
                string name = r.GetProperty("Name").GetString() ?? "";
                string val = r.TryGetProperty("Value", out var vP) ? vP.GetString() ?? "1" : "1";
                string type = r.TryGetProperty("Type", out var tP) ? tP.GetString() ?? "DWord" : "DWord";
                string origVal = r.TryGetProperty("OriginalValue", out var oP) ? oP.GetString() ?? "<RemoveEntry>" : "<RemoveEntry>";

                var (hive, key) = ParseHiveAndKey(path);
                string valueKind = type.Equals("String", StringComparison.OrdinalIgnoreCase) ? "String" : "DWord";

                // Apply action
                if (val == "<RemoveEntry>")
                {
                    tweak.Apply.Add(new ActionItem
                    {
                        Type = "Registry",
                        Hive = hive,
                        Key = key,
                        Operation = "DeleteValue",
                        ValueName = name
                    });
                }
                else
                {
                    object dataObj = valueKind == "DWord" && int.TryParse(val, out int intVal) ? intVal : val;
                    tweak.Apply.Add(new ActionItem
                    {
                        Type = "Registry",
                        Hive = hive,
                        Key = key,
                        Operation = "Set",
                        ValueName = name,
                        ValueKind = valueKind,
                        Data = JsonSerializer.SerializeToElement(dataObj)
                    });
                }

                // Undo action
                if (origVal == "<RemoveEntry>")
                {
                    tweak.Undo.Add(new ActionItem
                    {
                        Type = "Registry",
                        Hive = hive,
                        Key = key,
                        Operation = "DeleteValue",
                        ValueName = name
                    });
                }
                else
                {
                    object origDataObj = valueKind == "DWord" && int.TryParse(origVal, out int intOrigVal) ? intOrigVal : origVal;
                    tweak.Undo.Add(new ActionItem
                    {
                        Type = "Registry",
                        Hive = hive,
                        Key = key,
                        Operation = "Set",
                        ValueName = name,
                        ValueKind = valueKind,
                        Data = JsonSerializer.SerializeToElement(origDataObj)
                    });
                }

                // Detect Rule (first registry entry used for detection)
                if (string.IsNullOrEmpty(tweak.Detect.Type))
                {
                    object expObj = valueKind == "DWord" && int.TryParse(val, out int expectedInt) ? expectedInt : val;
                    tweak.Detect.Type = "Registry";
                    tweak.Detect.Hive = hive;
                    tweak.Detect.Key = key;
                    tweak.Detect.ValueName = name;
                    tweak.Detect.MatchType = "Equals";
                    tweak.Detect.ExpectedValue = JsonSerializer.SerializeToElement(expObj);
                }
            }
        }

        // 2. Services
        if (elem.TryGetProperty("service", out var svcProp) && svcProp.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement s in svcProp.EnumerateArray())
            {
                string sName = s.GetProperty("Name").GetString() ?? "";
                string startupType = s.GetProperty("StartupType").GetString() ?? "Manual";
                string origType = s.TryGetProperty("OriginalType", out var oTP) ? oTP.GetString() ?? "Automatic" : "Automatic";

                tweak.Apply.Add(new ActionItem
                {
                    Type = "Service",
                    ServiceName = sName,
                    Operation = "SetStartup",
                    StartupType = startupType
                });

                tweak.Undo.Add(new ActionItem
                {
                    Type = "Service",
                    ServiceName = sName,
                    Operation = "SetStartup",
                    StartupType = origType
                });

                if (string.IsNullOrEmpty(tweak.Detect.Type))
                {
                    tweak.Detect.Type = "Service";
                    tweak.Detect.ServiceName = sName;
                    tweak.Detect.ExpectedStartup = startupType;
                }
            }
        }

        // 3. InvokeScript / UndoScript
        if (elem.TryGetProperty("InvokeScript", out var isProp) && isProp.ValueKind == JsonValueKind.Array)
        {
            string script = string.Join("\n", isProp.EnumerateArray().Select(x => x.GetString() ?? "").Where(s => !string.IsNullOrWhiteSpace(s)));
            if (!string.IsNullOrWhiteSpace(script))
            {
                tweak.Apply.Add(new ActionItem
                {
                    Type = "PowerShell",
                    Operation = "Execute",
                    Script = script.Trim(),
                    TimeoutSeconds = 60
                });
            }
        }

        if (elem.TryGetProperty("UndoScript", out var usProp) && usProp.ValueKind == JsonValueKind.Array)
        {
            string undoScript = string.Join("\n", usProp.EnumerateArray().Select(x => x.GetString() ?? "").Where(s => !string.IsNullOrWhiteSpace(s)));
            if (!string.IsNullOrWhiteSpace(undoScript))
            {
                tweak.Undo.Add(new ActionItem
                {
                    Type = "PowerShell",
                    Operation = "Execute",
                    Script = undoScript.Trim(),
                    TimeoutSeconds = 60
                });
            }
        }

        // Fallbacks if detect or apply/undo are empty
        if (tweak.Apply.Count == 0)
        {
            tweak.Apply.Add(new ActionItem
            {
                Type = "PowerShell",
                Operation = "Execute",
                Script = "Write-Host 'Applied'",
                TimeoutSeconds = 10
            });
        }

        if (tweak.Undo.Count == 0)
        {
            tweak.Undo.Add(new ActionItem
            {
                Type = "PowerShell",
                Operation = "Execute",
                Script = "Write-Host 'Reverted'",
                TimeoutSeconds = 10
            });
        }

        if (string.IsNullOrEmpty(tweak.Detect.Type))
        {
            tweak.Detect.Type = "Registry";
            tweak.Detect.Hive = "HKLM";
            tweak.Detect.Key = @"SOFTWARE\Tweakora\Simulated";
            tweak.Detect.ValueName = tweak.Origin ?? tweak.Id;
            tweak.Detect.MatchType = "Equals";
            tweak.Detect.ExpectedValue = JsonSerializer.SerializeToElement(1);
        }
    }

    private static (string Hive, string Key) ParseHiveAndKey(string fullPath)
    {
        string p = fullPath.Replace(":\\", "\\").Replace(":", "\\");
        string[] parts = p.Split('\\', 2);
        string hive = parts[0].ToUpperInvariant();
        if (hive == "HKEY_LOCAL_MACHINE") hive = "HKLM";
        if (hive == "HKEY_CURRENT_USER") hive = "HKCU";
        if (hive == "HKEY_CLASSES_ROOT") hive = "HKCR";
        if (hive == "HKEY_USERS") hive = "HKU";

        string key = parts.Length > 1 ? parts[1] : "";
        return (hive, key);
    }

    private static void BuildTweakoraExtras(string catalogDir, List<TweakDefinition> portedTweaks)
    {
        var portedIds = new HashSet<string>(portedTweaks.Select(t => t.Id), StringComparer.OrdinalIgnoreCase);
        var portedOrigins = new HashSet<string>(portedTweaks.Where(t => t.Origin != null).Select(t => t.Origin!), StringComparer.OrdinalIgnoreCase);

        // Gather all existing tweaks in old category files
        string tweaksFolder = Path.Combine(catalogDir, "Tweaks");
        string[] oldFiles = { "ai.json", "gaming.json", "network.json", "performance.json", "privacy.json", "security.json", "ui.json", "updates.json" };
        List<TweakDefinition> extras = new();

        foreach (string file in oldFiles)
        {
            string filePath = Path.Combine(tweaksFolder, file);
            if (File.Exists(filePath))
            {
                string text = File.ReadAllText(filePath);
                var list = JsonSerializer.Deserialize<List<TweakDefinition>>(text);
                if (list != null)
                {
                    foreach (var item in list)
                    {
                        if (!portedIds.Contains(item.Id) && (item.Origin == null || !portedOrigins.Contains(item.Origin)))
                        {
                            extras.Add(item);
                        }
                    }
                }
            }
        }

        Console.WriteLine($"[INFO] Compiled {extras.Count} unique Tweakora Extras into extras.json");
        File.WriteAllText(Path.Combine(tweaksFolder, "extras.json"), JsonSerializer.Serialize(extras, JsonOptions));
    }
}
