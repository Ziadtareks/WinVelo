using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Json.Schema;
using Tweakora.Core.Safety;

namespace CatalogValidator;

public static class Program
{
    public static int Main(string[] args)
    {
        Console.WriteLine("========================================");
        Console.WriteLine("  Tweakora Catalog Validator (CI Tool) ");
        Console.WriteLine("========================================");

        string rootDir = @"C:\Dev\Tweakora";
        string? candidateRoot = AppContext.BaseDirectory;
        while (candidateRoot != null && !File.Exists(Path.Combine(candidateRoot, "Tweakora.sln")))
        {
            candidateRoot = Directory.GetParent(candidateRoot)?.FullName;
        }
        if (candidateRoot != null && File.Exists(Path.Combine(candidateRoot, "Tweakora.sln")))
        {
            rootDir = candidateRoot;
        }
        else if (!Directory.Exists(rootDir))
        {
            rootDir = Directory.GetCurrentDirectory();
        }

        string schemaPath = Path.Combine(rootDir, "Catalog", "schema", "tweak.schema.json");
        string tweaksDir = Path.Combine(rootDir, "Catalog", "Tweaks");

        if (!File.Exists(schemaPath))
        {
            // Search upwards if run from inside bin/ or tools/
            string? parent = Directory.GetParent(rootDir)?.FullName;
            while (parent != null)
            {
                string candidate = Path.Combine(parent, "Catalog", "schema", "tweak.schema.json");
                if (File.Exists(candidate))
                {
                    schemaPath = candidate;
                    tweaksDir = Path.Combine(parent, "Catalog", "Tweaks");
                    break;
                }
                parent = Directory.GetParent(parent)?.FullName;
            }
        }

        if (!File.Exists(schemaPath))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[ERROR] Schema file not found: {schemaPath}");
            Console.ResetColor();
            return 1;
        }

        Console.WriteLine($"[INFO] Loading schema: {schemaPath}");
        JsonSchema schema;
        try
        {
            string schemaText = File.ReadAllText(schemaPath);
            schema = JsonSchema.FromText(schemaText);
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[ERROR] Failed to parse schema: {ex.Message}");
            Console.ResetColor();
            return 1;
        }

        if (!Directory.Exists(tweaksDir))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[ERROR] Tweaks directory not found: {tweaksDir}");
            Console.ResetColor();
            return 1;
        }

        string[] jsonFiles = Directory.GetFiles(tweaksDir, "*.json", SearchOption.AllDirectories);
        Console.WriteLine($"[INFO] Found {jsonFiles.Length} tweak file(s) to validate.");

        int totalTweaks = 0;
        int errorCount = 0;
        HashSet<string> seenIds = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, JsonNode> tweakMap = new(StringComparer.OrdinalIgnoreCase);

        foreach (string file in jsonFiles)
        {
            Console.WriteLine($"\n[CHECKING] {Path.GetFileName(file)}...");
            try
            {
                string fileContent = File.ReadAllText(file);
                JsonNode? rootNode = JsonNode.Parse(fileContent);

                if (rootNode is JsonArray array)
                {
                    for (int i = 0; i < array.Count; i++)
                    {
                        totalTweaks++;
                        JsonNode? item = array[i];
                        if (item == null) continue;

                        string id = item["id"]?.ToString() ?? $"item-{i}";
                        tweakMap[id] = item;
                        if (!seenIds.Add(id))
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine($"  [FAIL] Duplicate tweak ID found: '{id}'");
                            Console.ResetColor();
                            errorCount++;
                        }

                        EvaluationResults result = schema.Evaluate(item, new EvaluationOptions
                        {
                            OutputFormat = OutputFormat.List
                        });

                        bool? isReversible = item["reversible"]?.GetValue<bool>();
                        string? reason = item["irreversibleReason"]?.ToString();
                        if (isReversible == false && string.IsNullOrWhiteSpace(reason))
                        {
                            errorCount++;
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine($"  [FAIL] Tweak '{id}' has reversible=false but missing or empty irreversibleReason.");
                            Console.ResetColor();
                        }

                        ValidateServicingProtection(item, id, ref errorCount);
                        ValidateEvidenceField(item, id, ref errorCount);

                        if (!result.IsValid)
                        {
                            errorCount++;
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine($"  [FAIL] Tweak '{id}' failed schema validation:");
                            foreach (EvaluationResults detail in result.Details.Where(d => d.HasErrors))
                            {
                                foreach (KeyValuePair<string, string> error in detail.Errors ?? new Dictionary<string, string>())
                                {
                                    Console.WriteLine($"    - {detail.InstanceLocation}: {error.Value}");
                                }
                            }
                            Console.ResetColor();
                        }
                        else if (isReversible != false || !string.IsNullOrWhiteSpace(reason))
                        {
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine($"  [PASS] {id}");
                            Console.ResetColor();
                        }
                    }
                }
                else if (rootNode is JsonObject single)
                {
                    totalTweaks++;
                    string id = single["id"]?.ToString() ?? "single-item";
                    tweakMap[id] = single;
                    if (!seenIds.Add(id))
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"  [FAIL] Duplicate tweak ID found: '{id}'");
                        Console.ResetColor();
                        errorCount++;
                    }

                    EvaluationResults result = schema.Evaluate(single, new EvaluationOptions
                    {
                        OutputFormat = OutputFormat.List
                    });

                    bool? isReversible = single["reversible"]?.GetValue<bool>();
                    string? reason = single["irreversibleReason"]?.ToString();
                    if (isReversible == false && string.IsNullOrWhiteSpace(reason))
                    {
                        errorCount++;
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"  [FAIL] Tweak '{id}' has reversible=false but missing or empty irreversibleReason.");
                        Console.ResetColor();
                    }

                    ValidateServicingProtection(single, id, ref errorCount);
                    ValidateEvidenceField(single, id, ref errorCount);

                    if (!result.IsValid)
                    {
                        errorCount++;
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"  [FAIL] Tweak '{id}' failed schema validation:");
                        foreach (EvaluationResults detail in result.Details.Where(d => d.HasErrors))
                        {
                            foreach (KeyValuePair<string, string> error in detail.Errors ?? new Dictionary<string, string>())
                            {
                                Console.WriteLine($"    - {detail.InstanceLocation}: {error.Value}");
                            }
                        }
                        Console.ResetColor();
                    }
                    else if (isReversible != false || !string.IsNullOrWhiteSpace(reason))
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"  [PASS] {id}");
                        Console.ResetColor();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"  [FAIL] Could not read/parse file {file}: {ex.Message}");
                Console.ResetColor();
                errorCount++;
            }
        }

        Console.WriteLine("\n[CHECKING PRESETS] Validating Catalog/Presets/*.json...");
        string presetsDir = Path.Combine(rootDir, "Catalog", "Presets");
        if (!Directory.Exists(presetsDir))
        {
            string? parent = Directory.GetParent(rootDir)?.FullName;
            while (parent != null)
            {
                string candidate = Path.Combine(parent, "Catalog", "Presets");
                if (Directory.Exists(candidate))
                {
                    presetsDir = candidate;
                    break;
                }
                parent = Directory.GetParent(parent)?.FullName;
            }
        }

        int totalPresets = 0;
        if (!Directory.Exists(presetsDir))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[ERROR] Presets directory not found: {presetsDir}");
            Console.ResetColor();
            errorCount++;
        }
        else
        {
            string[] presetFiles = Directory.GetFiles(presetsDir, "*.json", SearchOption.AllDirectories);
            Console.WriteLine($"[INFO] Found {presetFiles.Length} preset file(s) to validate.");

            foreach (string pFile in presetFiles)
            {
                totalPresets++;
                string fileName = Path.GetFileName(pFile);
                try
                {
                    string pJson = File.ReadAllText(pFile);
                    JsonNode? pNode = JsonNode.Parse(pJson);
                    if (pNode == null)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"  [FAIL] {fileName}: Preset JSON is empty or null.");
                        Console.ResetColor();
                        errorCount++;
                        continue;
                    }

                    string pId = pNode["id"]?.ToString() ?? "";
                    string pName = pNode["name"]?.ToString() ?? "";
                    string pDesc = pNode["description"]?.ToString() ?? "";
                    JsonArray? tweakIds = pNode["tweakIds"]?.AsArray();

                    if (string.IsNullOrWhiteSpace(pId))
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"  [FAIL] {fileName}: Missing or empty 'id'.");
                        Console.ResetColor();
                        errorCount++;
                    }
                    if (string.IsNullOrWhiteSpace(pName))
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"  [FAIL] {fileName}: Missing or empty 'name'.");
                        Console.ResetColor();
                        errorCount++;
                    }
                    if (string.IsNullOrWhiteSpace(pDesc))
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"  [FAIL] {fileName}: Missing or empty 'description'.");
                        Console.ResetColor();
                        errorCount++;
                    }
                    if (tweakIds == null || tweakIds.Count == 0)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"  [FAIL] {fileName}: Missing or empty 'tweakIds' array.");
                        Console.ResetColor();
                        errorCount++;
                        continue;
                    }

                    bool presetOk = true;
                    foreach (var tidNode in tweakIds)
                    {
                        string tid = tidNode?.ToString() ?? "";
                        if (string.IsNullOrWhiteSpace(tid))
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine($"  [FAIL] {fileName}: Empty tweakId in array.");
                            Console.ResetColor();
                            errorCount++;
                            presetOk = false;
                            continue;
                        }

                        if (!tweakMap.TryGetValue(tid, out var tweakNode))
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine($"  [FAIL] {fileName} ({pId}): References nonexistent tweak ID '{tid}'.");
                            Console.ResetColor();
                            errorCount++;
                            presetOk = false;
                            continue;
                        }

                        string? risk = tweakNode["risk"]?.ToString();
                        if (string.Equals(risk, "High", StringComparison.OrdinalIgnoreCase))
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine($"  [FAIL] {fileName} ({pId}): Contains High-risk tweak '{tid}'. Presets cannot contain High-risk tweaks.");
                            Console.ResetColor();
                            errorCount++;
                            presetOk = false;
                        }

                        bool? isReversible = tweakNode["reversible"]?.GetValue<bool>();
                        if (isReversible == false)
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine($"  [FAIL] {fileName} ({pId}): Contains irreversible tweak '{tid}'. Presets cannot contain irreversible tweaks.");
                            Console.ResetColor();
                            errorCount++;
                            presetOk = false;
                        }

                        bool? needsReview = tweakNode["needsReview"]?.GetValue<bool>();
                        if (needsReview == true)
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine($"  [FAIL] {fileName} ({pId}): Contains unverified tweak '{tid}' marked with needsReview=true.");
                            Console.ResetColor();
                            errorCount++;
                            presetOk = false;
                        }

                        if (TouchesServicing(tweakNode, out string sReason))
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine($"  [FAIL] {fileName} ({pId}): Contains tweak '{tid}' which impacts Windows servicing ({sReason}). Presets must protect Windows servicing and cannot contain servicing-impacting tweaks.");
                            Console.ResetColor();
                            errorCount++;
                            presetOk = false;
                        }

                        string? evidence = tweakNode["evidence"]?.ToString();
                        if (!string.Equals(evidence, "documented", StringComparison.OrdinalIgnoreCase))
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine($"  [FAIL] {fileName} ({pId}): Contains tweak '{tid}' with evidence '{evidence ?? "missing"}'. Presets must contain ONLY documented tweaks.");
                            Console.ResetColor();
                            errorCount++;
                            presetOk = false;
                        }

                        string? category = tweakNode["category"]?.ToString();
                        if (string.Equals(category, "Experimental", StringComparison.OrdinalIgnoreCase))
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine($"  [FAIL] {fileName} ({pId}): Contains tweak '{tid}' with category 'Experimental'. Presets cannot contain experimental tweaks.");
                            Console.ResetColor();
                            errorCount++;
                            presetOk = false;
                        }
                    }

                    if (presetOk)
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"  [PASS] Preset '{pId}' ({tweakIds.Count} tweaks)");
                        Console.ResetColor();
                    }
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"  [FAIL] Could not parse preset file {fileName}: {ex.Message}");
                    Console.ResetColor();
                    errorCount++;
                }
            }
        }

        Console.WriteLine("\n----------------------------------------");
        Console.WriteLine($"Summary: {totalTweaks} tweak(s) and {totalPresets} preset(s) validated. {errorCount} error(s).");
        Console.WriteLine("----------------------------------------");

        return errorCount == 0 ? 0 : 1;
    }

    private static bool TouchesServicing(JsonNode item, out string servicingReason)
    {
        servicingReason = string.Empty;
        var applyArray = item["apply"]?.AsArray();
        if (applyArray == null) return false;

        foreach (var action in applyArray)
        {
            if (action == null) continue;
            string type = action["type"]?.ToString() ?? "";

            if (string.Equals(type, "Service", StringComparison.OrdinalIgnoreCase))
            {
                string svc = action["serviceName"]?.ToString() ?? "";
                string startup = action["startupType"]?.ToString() ?? "";
                if (ServicingProtectionHelper.IsProtectedService(svc))
                {
                    if (string.Equals(startup, "Disabled", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(startup, "Manual", StringComparison.OrdinalIgnoreCase))
                    {
                        servicingReason = $"Modifies protected servicing service '{svc}' to '{startup}'";
                        return true;
                    }
                }
            }
            else if (string.Equals(type, "Registry", StringComparison.OrdinalIgnoreCase))
            {
                string key = action["key"]?.ToString() ?? "";
                string val = action["valueName"]?.ToString() ?? "";
                if (ServicingProtectionHelper.IsProtectedPolicy(key, val))
                {
                    servicingReason = $"Modifies protected servicing policy '{key}\\{val}'";
                    return true;
                }
            }
            else if (string.Equals(type, "PowerShell", StringComparison.OrdinalIgnoreCase))
            {
                string script = action["script"]?.ToString() ?? "";
                foreach (var svc in ServicingProtectionConstants.ProtectedServices)
                {
                    if (Regex.IsMatch(script, $@"\b(Set-Service|sc\.exe|Stop-Service)\b.*?\b{svc}\b", RegexOptions.IgnoreCase))
                    {
                        servicingReason = $"Script targets protected service '{svc}'";
                        return true;
                    }
                }
                if (script.Contains("store.db", StringComparison.OrdinalIgnoreCase) && script.Contains("/deny", StringComparison.OrdinalIgnoreCase))
                {
                    servicingReason = "Script denies access to Microsoft Store database (store.db)";
                    return true;
                }
            }
        }
        return false;
    }

    private static void ValidateServicingProtection(JsonNode item, string id, ref int errorCount)
    {
        if (TouchesServicing(item, out string reason))
        {
            string? risk = item["risk"]?.ToString();
            string? sideEffects = item["sideEffects"]?.ToString();

            if (!string.Equals(risk, "High", StringComparison.OrdinalIgnoreCase))
            {
                errorCount++;
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"  [FAIL] Tweak '{id}' impacts Windows servicing ({reason}) but risk is '{risk}' instead of 'High'.");
                Console.ResetColor();
            }

            if (string.IsNullOrWhiteSpace(sideEffects))
            {
                errorCount++;
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"  [FAIL] Tweak '{id}' impacts Windows servicing ({reason}) but is missing required 'sideEffects' explanation.");
                Console.ResetColor();
            }
        }
    }

    private static void ValidateEvidenceField(JsonNode item, string id, ref int errorCount)
    {
        string? evidence = item["evidence"]?.ToString();
        if (string.IsNullOrWhiteSpace(evidence) ||
            (!string.Equals(evidence, "documented", StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(evidence, "situational", StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(evidence, "anecdotal", StringComparison.OrdinalIgnoreCase)))
        {
            errorCount++;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"  [FAIL] Tweak '{id}' is missing a valid 'evidence' field ('documented', 'situational', or 'anecdotal'). Found: '{evidence}'.");
            Console.ResetColor();
        }
    }
}
