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
                        ValidateRiskFloors(item, id, ref errorCount);
                        ValidateExplanations(item, id, ref errorCount);
                        ValidateRegistryKeys(item, id, ref errorCount);

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
                    ValidateRiskFloors(single, id, ref errorCount);
                    ValidateExplanations(single, id, ref errorCount);
                    ValidateRegistryKeys(single, id, ref errorCount);

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
                        if (string.Equals(evidence, "anecdotal", StringComparison.OrdinalIgnoreCase))
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine($"  [FAIL] {fileName} ({pId}): Contains tweak '{tid}' with evidence 'anecdotal'. Presets cannot contain unproven or anecdotal tweaks.");
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

        ValidateNoExactDuplicates(tweakMap, ref errorCount);
        ValidateAliasesLock(tweakMap, tweaksDir, ref errorCount);
        ValidateNoConflictingRegistryData(tweakMap, ref errorCount);

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
             !string.Equals(evidence, "anecdotal", StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(evidence, "ui-equivalent", StringComparison.OrdinalIgnoreCase)))
        {
            errorCount++;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"  [FAIL] Tweak '{id}' is missing a valid 'evidence' field ('documented', 'situational', 'anecdotal', or 'ui-equivalent'). Found: '{evidence}'.");
            Console.ResetColor();
        }

        if (string.Equals(evidence, "documented", StringComparison.OrdinalIgnoreCase))
        {
            string? sourceQuote = item["sourceQuote"]?.ToString();
            if (string.IsNullOrWhiteSpace(sourceQuote) || sourceQuote.Trim().Length < 5)
            {
                errorCount++;
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"  [FAIL] Tweak '{id}' has evidence='documented' but missing or empty 'sourceQuote' (must quote documented source naming the setting).");
                Console.ResetColor();
            }
        }

        if (string.Equals(evidence, "ui-equivalent", StringComparison.OrdinalIgnoreCase))
        {
            string? uiLoc = item["uiEquivalentLocation"]?.ToString();
            if (string.IsNullOrWhiteSpace(uiLoc) || uiLoc.Trim().Length < 5)
            {
                errorCount++;
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"  [FAIL] Tweak '{id}' has evidence='ui-equivalent' but missing or empty 'uiEquivalentLocation' (e.g. 'Settings › Personalization › Colors').");
                Console.ResetColor();
            }
        }
    }

    private static void ValidateRiskFloors(JsonNode item, string id, ref int errorCount)
    {
        string? risk = item["risk"]?.ToString();
        bool? isReversible = item["reversible"]?.GetValue<bool>();

        // Floor 1: irreversible => High
        if (isReversible == false && !string.Equals(risk, "High", StringComparison.OrdinalIgnoreCase))
        {
            errorCount++;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"  [FAIL] Tweak '{id}' has reversible=false but risk is '{risk}'. Irreversible tweaks must have risk 'High'.");
            Console.ResetColor();
        }

        // Floor 2: protected service => High
        if (TouchesServicing(item, out string servicingReason) && !string.Equals(risk, "High", StringComparison.OrdinalIgnoreCase))
        {
            errorCount++;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"  [FAIL] Tweak '{id}' impacts Windows servicing ({servicingReason}) but risk is '{risk}'. Must be risk 'High'.");
            Console.ResetColor();
        }

        // Floor 3: HKLM => at least Low (cannot be Safe)
        bool touchesHklm = false;
        var applyArray = item["apply"]?.AsArray();
        if (applyArray != null)
        {
            foreach (var action in applyArray)
            {
                if (action == null) continue;
                string hive = action["hive"]?.ToString() ?? "";
                if (string.Equals(hive, "HKLM", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(hive, "HKEY_LOCAL_MACHINE", StringComparison.OrdinalIgnoreCase))
                {
                    touchesHklm = true;
                    break;
                }
            }
        }

        if (!touchesHklm)
        {
            var detect = item["detect"];
            if (detect != null)
            {
                string hive = detect["hive"]?.ToString() ?? "";
                if (string.Equals(hive, "HKLM", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(hive, "HKEY_LOCAL_MACHINE", StringComparison.OrdinalIgnoreCase))
                {
                    touchesHklm = true;
                }
            }
        }

        if (touchesHklm && string.Equals(risk, "Safe", StringComparison.OrdinalIgnoreCase))
        {
            errorCount++;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"  [FAIL] Tweak '{id}' targets HKLM machine policy/configuration but risk is 'Safe'. HKLM machine policy tweaks must be at least risk 'Low'.");
            Console.ResetColor();
        }
    }

    private static void ValidateExplanations(JsonNode item, string id, ref int errorCount)
    {
        string? risk = item["risk"]?.ToString();
        string? whatItDoes = item["whatItDoes"]?.ToString();
        string? sideEffects = item["sideEffects"]?.ToString();
        var sourcesArray = item["sources"]?.AsArray();

        bool isSafe = string.Equals(risk, "Safe", StringComparison.OrdinalIgnoreCase);

        if (!isSafe)
        {
            if (string.IsNullOrWhiteSpace(whatItDoes))
            {
                errorCount++;
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"  [FAIL] Tweak '{id}' (risk: {risk}) is missing required 'whatItDoes' explanation.");
                Console.ResetColor();
            }

            if (string.IsNullOrWhiteSpace(sideEffects))
            {
                errorCount++;
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"  [FAIL] Tweak '{id}' (risk: {risk}) is missing required 'sideEffects' note.");
                Console.ResetColor();
            }
            else
            {
                string trimmed = sideEffects.Trim().TrimEnd('.');
                if (string.Equals(trimmed, "None known", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(trimmed, "None", StringComparison.OrdinalIgnoreCase))
                {
                    errorCount++;
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"  [FAIL] Tweak '{id}' (risk: {risk}) has forbidden '{sideEffects}' sideEffects. 'None known' is allowed only for Safe tweaks.");
                    Console.ResetColor();
                }
            }

            if (sourcesArray == null || sourcesArray.Count == 0)
            {
                errorCount++;
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"  [FAIL] Tweak '{id}' (risk: {risk}) is missing required 'sources' list.");
                Console.ResetColor();
            }
            else
            {
                foreach (var srcNode in sourcesArray)
                {
                    string src = srcNode?.ToString() ?? "";
                    if (string.IsNullOrWhiteSpace(src) || !src.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    {
                        errorCount++;
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"  [FAIL] Tweak '{id}' has invalid source URI in sources: '{src}'.");
                        Console.ResetColor();
                    }
                }
            }
        }
    }

    public static (string Signature, string Sha256) ComputeApplySignature(JsonNode item)
    {
        var applyArray = item["apply"]?.AsArray();
        if (applyArray == null || applyArray.Count == 0) return (string.Empty, string.Empty);

        var targets = new List<string>();
        foreach (var action in applyArray)
        {
            if (action == null) continue;
            string? hive = action["hive"]?.ToString();
            string? key = action["key"]?.ToString();
            string? val = action["valueName"]?.ToString();
            string? svc = action["serviceName"]?.ToString();

            if (!string.IsNullOrWhiteSpace(hive) || !string.IsNullOrWhiteSpace(key))
            {
                string h = (hive ?? "").ToUpperInvariant().Replace("HKEY_LOCAL_MACHINE", "HKLM").Replace("HKEY_CURRENT_USER", "HKCU");
                string k = (key ?? "").Trim('\\').ToUpperInvariant();
                string v = string.IsNullOrWhiteSpace(val) ? "@DEFAULT" : val.ToUpperInvariant();
                string data = action["data"]?.ToString() ?? "";
                targets.Add($"REG:{h}\\{k}\\{v}={data}");
            }
            else if (!string.IsNullOrWhiteSpace(svc))
            {
                string s = svc.ToUpperInvariant();
                string startup = action["startupType"]?.ToString() ?? "";
                targets.Add($"SVC:{s}={startup}");
            }
            else
            {
                string? type = action["type"]?.ToString();
                string? script = action["script"]?.ToString();
                if (string.Equals(type, "PowerShell", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(script))
                {
                    targets.Add($"PS:{script.Trim()}");
                }
            }
        }

        targets.Sort(StringComparer.Ordinal);
        string signature = string.Join(";", targets);
        if (string.IsNullOrWhiteSpace(signature)) return (string.Empty, string.Empty);

        using var sha256 = System.Security.Cryptography.SHA256.Create();
        byte[] hashBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(signature));
        string hash = Convert.ToHexString(hashBytes).ToLowerInvariant();
        return (signature, hash);
    }

    private static void ValidateNoExactDuplicates(Dictionary<string, JsonNode> tweakMap, ref int errorCount)
    {
        Console.WriteLine("\n[CHECKING DUPLICATES] Validating that catalog contains zero exact duplicates...");
        var actionSignatures = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var (id, item) in tweakMap)
        {
            var (signature, _) = ComputeApplySignature(item);
            if (string.IsNullOrWhiteSpace(signature)) continue;

            if (!actionSignatures.TryGetValue(signature, out var list))
            {
                list = new List<string>();
                actionSignatures[signature] = list;
            }
            list.Add(id);
        }

        int duplicateCount = 0;
        foreach (var (sig, ids) in actionSignatures)
        {
            if (ids.Count > 1)
            {
                duplicateCount++;
                errorCount++;
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"  [FAIL] Exact duplicate tweaks detected: [{string.Join(", ", ids)}] perform identical actions: {sig}");
                Console.ResetColor();
            }
        }

        if (duplicateCount == 0)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("  [PASS] Zero exact duplicate tweaks found.");
            Console.ResetColor();
        }
    }

    private static void ValidateAliasesLock(Dictionary<string, JsonNode> tweakMap, string tweaksDir, ref int errorCount)
    {
        Console.WriteLine("\n[CHECKING ALIASES LOCK] Validating catalog aliases against Catalog/aliases.lock.json...");
        string catalogDir = Path.GetDirectoryName(tweaksDir) ?? tweaksDir;
        string lockFilePath = Path.Combine(catalogDir, "aliases.lock.json");

        if (!File.Exists(lockFilePath))
        {
            errorCount++;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"  [FAIL] Missing aliases lock file: {lockFilePath}");
            Console.ResetColor();
            return;
        }

        JsonNode? lockRoot;
        try
        {
            lockRoot = JsonNode.Parse(File.ReadAllText(lockFilePath));
        }
        catch (Exception ex)
        {
            errorCount++;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"  [FAIL] Failed to parse aliases lock file: {ex.Message}");
            Console.ResetColor();
            return;
        }

        if (lockRoot is not JsonObject lockObj)
        {
            errorCount++;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("  [FAIL] Aliases lock file root must be a JSON object.");
            Console.ResetColor();
            return;
        }

        var lockedTweakIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Verify each entry in the lockfile
        foreach (var (canonicalId, entryNode) in lockObj)
        {
            lockedTweakIds.Add(canonicalId);
            if (!tweakMap.TryGetValue(canonicalId, out var canonicalTweak))
            {
                errorCount++;
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"  [FAIL] Locked alias tweak '{canonicalId}' does not exist in catalog.");
                Console.ResetColor();
                continue;
            }

            var lockedAliases = entryNode?["aliases"]?.AsArray()
                ?.Select(n => n?.ToString())
                ?.Where(s => !string.IsNullOrWhiteSpace(s))
                ?.ToList() ?? new List<string?>();

            string? lockedSig = entryNode?["signature"]?.ToString();
            string? lockedHash = entryNode?["sha256"]?.ToString();

            var actualAliases = canonicalTweak["aliases"]?.AsArray()
                ?.Select(n => n?.ToString())
                ?.Where(s => !string.IsNullOrWhiteSpace(s))
                ?.ToList() ?? new List<string?>();

            var lockedAliasSet = new HashSet<string>(lockedAliases!, StringComparer.OrdinalIgnoreCase);
            var actualAliasSet = new HashSet<string>(actualAliases!, StringComparer.OrdinalIgnoreCase);

            if (!lockedAliasSet.SetEquals(actualAliasSet))
            {
                errorCount++;
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"  [FAIL] Tweak '{canonicalId}' declared aliases [{string.Join(", ", actualAliases)}] do not match locked aliases [{string.Join(", ", lockedAliases)}].");
                Console.ResetColor();
            }

            var (actualSig, actualHash) = ComputeApplySignature(canonicalTweak);
            if (!string.Equals(actualSig, lockedSig, StringComparison.Ordinal) ||
                !string.Equals(actualHash, lockedHash, StringComparison.OrdinalIgnoreCase))
            {
                errorCount++;
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"  [FAIL] Tweak '{canonicalId}' apply signature changed from locked signature!");
                Console.WriteLine($"    Expected SHA256: {lockedHash}");
                Console.WriteLine($"    Actual SHA256:   {actualHash}");
                Console.ResetColor();
            }
        }

        // Verify that NO tweak in the catalog has aliases that are not locked
        foreach (var (id, item) in tweakMap)
        {
            var aliasesArr = item["aliases"]?.AsArray();
            if (aliasesArr != null && aliasesArr.Count > 0)
            {
                if (!lockedTweakIds.Contains(id))
                {
                    errorCount++;
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"  [FAIL] Tweak '{id}' has aliases [{string.Join(", ", aliasesArr.Select(x => x?.ToString()))}] which are not locked in aliases.lock.json!");
                    Console.ResetColor();
                }
            }
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"  [PASS] Verified {lockedTweakIds.Count} locked alias definitions and signatures.");
        Console.ResetColor();
    }

    private static void ValidateNoConflictingRegistryData(Dictionary<string, JsonNode> tweakMap, ref int errorCount)
    {
        Console.WriteLine("\n[CHECKING CONFLICTING DATA] Validating that tweaks setting same registry value with different data declare oppositeOf...");

        var regValues = new Dictionary<string, List<(string TweakId, string Data, JsonNode TweakNode)>>(StringComparer.OrdinalIgnoreCase);

        foreach (var (id, item) in tweakMap)
        {
            var applyArray = item["apply"]?.AsArray();
            if (applyArray == null || applyArray.Count == 0) continue;

            foreach (var action in applyArray)
            {
                if (action == null) continue;
                string? hive = action["hive"]?.ToString();
                string? key = action["key"]?.ToString();
                string? val = action["valueName"]?.ToString();

                if (!string.IsNullOrWhiteSpace(hive) || !string.IsNullOrWhiteSpace(key))
                {
                    string h = (hive ?? "").ToUpperInvariant().Replace("HKEY_LOCAL_MACHINE", "HKLM").Replace("HKEY_CURRENT_USER", "HKCU");
                    string k = (key ?? "").Trim('\\').ToUpperInvariant();
                    string v = string.IsNullOrWhiteSpace(val) ? "@DEFAULT" : val.ToUpperInvariant();
                    string data = action["data"]?.ToString() ?? "";
                    string target = $"REG:{h}\\{k}\\{v}";

                    if (!regValues.TryGetValue(target, out var list))
                    {
                        list = new List<(string, string, JsonNode)>();
                        regValues[target] = list;
                    }

                    if (!list.Any(x => string.Equals(x.TweakId, id, StringComparison.OrdinalIgnoreCase)))
                    {
                        list.Add((id, data, item));
                    }
                }
            }
        }

        bool HasOppositeMarking(JsonNode tweakA, string idA, JsonNode tweakB, string idB)
        {
            bool Matches(JsonNode t, string targetId)
            {
                var opp = t["oppositeOf"];
                if (opp is JsonValue val && val.TryGetValue<string>(out var s))
                {
                    return string.Equals(s, targetId, StringComparison.OrdinalIgnoreCase);
                }
                if (opp is JsonArray arr)
                {
                    return arr.Any(n => string.Equals(n?.ToString(), targetId, StringComparison.OrdinalIgnoreCase));
                }
                return false;
            }

            return Matches(tweakA, idB) || Matches(tweakB, idA);
        }

        int conflictCount = 0;
        foreach (var (target, entries) in regValues)
        {
            if (entries.Count < 2) continue;

            for (int i = 0; i < entries.Count; i++)
            {
                for (int j = i + 1; j < entries.Count; j++)
                {
                    var a = entries[i];
                    var b = entries[j];

                    if (!string.Equals(a.Data, b.Data, StringComparison.Ordinal))
                    {
                        if (!HasOppositeMarking(a.TweakNode, a.TweakId, b.TweakNode, b.TweakId))
                        {
                            conflictCount++;
                            errorCount++;
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine($"  [FAIL] Conflicting registry setting without oppositeOf declaration:");
                            Console.WriteLine($"    Target:   {target}");
                            Console.WriteLine($"    Tweak 1:  '{a.TweakId}' sets data: '{a.Data}'");
                            Console.WriteLine($"    Tweak 2:  '{b.TweakId}' sets data: '{b.Data}'");
                            Console.ResetColor();
                        }
                    }
                }
            }
        }

        if (conflictCount == 0)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("  [PASS] All conflicting registry settings have valid oppositeOf declarations.");
            Console.ResetColor();
        }
    }

    private static void ValidateRegistryKeys(JsonNode tweak, string id, ref int errorCount)
    {
        var keyNodes = new List<(string Location, string Key)>();

        void InspectAction(JsonNode? action, string loc)
        {
            if (action == null) return;
            var keyNode = action["key"];
            if (keyNode != null)
            {
                keyNodes.Add((loc, keyNode.ToString()));
            }
        }

        if (tweak["detect"] is JsonObject det)
        {
            InspectAction(det, "detect");
            if (det["any"] is JsonArray anyArr)
            {
                int i = 0;
                foreach (var a in anyArr) InspectAction(a, $"detect.any[{i++}]");
            }
            if (det["all"] is JsonArray allArr)
            {
                int i = 0;
                foreach (var a in allArr) InspectAction(a, $"detect.all[{i++}]");
            }
        }

        if (tweak["apply"] is JsonArray applyArr)
        {
            int i = 0;
            foreach (var a in applyArr) InspectAction(a, $"apply[{i++}]");
        }

        if (tweak["undo"] is JsonArray undoArr)
        {
            int i = 0;
            foreach (var a in undoArr) InspectAction(a, $"undo[{i++}]");
        }

        foreach (var (loc, key) in keyNodes)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                errorCount++;
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"  [FAIL] Tweak '{id}' {loc} has an empty or whitespace registry key.");
                Console.ResetColor();
                continue;
            }

            if (key.StartsWith('\\') || key.EndsWith('\\') || key.Contains(@"\\"))
            {
                errorCount++;
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"  [FAIL] Tweak '{id}' {loc} contains malformed registry key with empty path segments or repeated backslashes: '{key}'");
                Console.ResetColor();
                continue;
            }

            var segments = key.Split('\\');
            if (segments.Any(s => string.IsNullOrWhiteSpace(s)))
            {
                errorCount++;
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"  [FAIL] Tweak '{id}' {loc} has empty path segment in registry key: '{key}'");
                Console.ResetColor();
            }
        }
    }
}


