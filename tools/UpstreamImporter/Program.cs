using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace UpstreamImporter;

/// <summary>Produces an auditable source proposal; never rewrites the curated runtime catalog.</summary>
public static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly string[] Files = { "applications.json", "feature.json", "dns.json", "appx.json", "preset.json", "tweaks.json" };
    public static int Main(string[] args)
    {
        string? root = AppContext.BaseDirectory;
        while (root != null && !File.Exists(Path.Combine(root, "WinVelo.sln"))) root = Directory.GetParent(root)?.FullName;
        if (root == null) { Console.Error.WriteLine("Could not find WinVelo.sln."); return 1; }
        string source = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.Combine(root, "upstream", "winutil", "config");
        string output = args.Length > 1 ? Path.GetFullPath(args[1]) : Path.Combine(root, "work", "upstream-proposal-" + Guid.NewGuid().ToString("N"));
        if (Directory.Exists(output) || File.Exists(output)) { Console.Error.WriteLine("Proposal output must be a new directory."); return 1; }
        string curated = Path.GetFullPath(Path.Combine(root, "Catalog"));
        if (output.StartsWith(curated + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || output.Equals(curated, StringComparison.OrdinalIgnoreCase))
        { Console.Error.WriteLine("Proposal output must be outside Catalog."); return 1; }
        var inputs = new Dictionary<string, string>(); var hashes = new Dictionary<string, string>();
        try
        {
            // Finish all reads and parsing before creating any output directories.
            foreach (string name in Files)
            {
                byte[] bytes = File.ReadAllBytes(Path.Combine(source, name));
                string raw = File.ReadAllText(Path.Combine(source, name));
                string normalized = NormalizeJsonStrings(raw);
                using var parsed = JsonDocument.Parse(normalized);
                if (parsed.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException(name + " must contain an object.");
                inputs[name] = normalized; hashes[name] = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            }
            var mappings = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            using (var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(curated, "SourceTracking", "parity.json"))))
                foreach (var row in manifest.RootElement.GetProperty("tweaks").EnumerateArray())
                    mappings[row.GetProperty("key").GetString()!] = row.GetProperty("localIds").EnumerateArray().Select(v => v.GetString()!).ToArray();
            using var tweaks = JsonDocument.Parse(inputs["tweaks.json"]);
            var review = tweaks.RootElement.EnumerateObject().Select(row => new
            {
                key = row.Name,
                localIds = mappings.TryGetValue(row.Name, out var ids) ? ids : Array.Empty<string>(),
                requiresReview = true,
                reason = "Compare source actions, detect probes, captured baselines, aliases and localization before manually promoting changes. An existing mapping does not prove semantic equivalence."
            }).ToList();
            Directory.CreateDirectory(Path.Combine(output, "source"));
            foreach (var input in inputs) File.WriteAllText(Path.Combine(output, "source", input.Key), input.Value, new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(output, "review.json"), JsonSerializer.Serialize(new
            {
                suppliedCommit = args.Length > 2 ? args[2] : null,
                sourceHashes = hashes,
                note = "Source snapshot only. No executable catalog entries were generated. No curated files were overwritten.",
                tweaks = review
            }, JsonOptions));
            Console.WriteLine($"Prepared {Files.Length} source files and {review.Count} tweak/control review entries in {output}.");
            Console.WriteLine("Catalog was not modified. Review the proposal before promoting individual changes.");
            return 0;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or InvalidOperationException)
        { Console.Error.WriteLine("Import failed: " + ex.Message); return 1; }
    }
    private static string NormalizeJsonStrings(string input)
    {
        var output = new StringBuilder(); bool quoted = false, escaped = false;
        foreach (char c in input)
        {
            if (quoted && c < 32) { output.Append("\\u" + ((int)c).ToString("x4")); escaped = false; continue; }
            output.Append(c);
            if (escaped) { escaped = false; continue; }
            if (quoted && c == '\\') escaped = true;
            else if (c == '"') quoted = !quoted;
        }
        return output.ToString();
    }
}
