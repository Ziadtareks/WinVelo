using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

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
                        else
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
                    else
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

        Console.WriteLine("\n----------------------------------------");
        Console.WriteLine($"Summary: {totalTweaks} tweak(s) validated. {errorCount} error(s).");
        Console.WriteLine("----------------------------------------");

        return errorCount == 0 ? 0 : 1;
    }
}
