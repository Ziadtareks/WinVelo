using System.Text.Json;
using System.Text.RegularExpressions;
using WinVelo.Core.Catalog;

namespace CatalogStats;

/// <summary>
/// Counts what is in Catalog/ and keeps the numbers in the README and docs in sync.
///   dotnet run --project tools/CatalogStats            check only (exit code 1 when a number is out of date)
///   dotnet run --project tools/CatalogStats -- --update rewrite the numbers in place
///   dotnet run --project tools/CatalogStats -- --count  print the tweak count only (used by build/release.ps1)
/// </summary>
public static class Program
{
    /// <summary>Text that contains a number taken from the catalog. Group 1 is the number.</summary>
    private sealed record CountPattern(string File, string Regex, Func<Stats, int> Value, string What);

    public sealed record Stats(int Tweaks, int Apps, int Presets);

    private static readonly CountPattern[] Patterns =
    {
        new("README.md", @"img\.shields\.io/badge/tweaks-(\d+)-", s => s.Tweaks, "tweak badge"),
        new("README.md", @"alt=""(\d+) tweaks""", s => s.Tweaks, "tweak badge text"),
        new("README.md", @"All (\d+) tweaks", s => s.Tweaks, "Features table"),
        new("README.md", @"Install (\d+) popular apps", s => s.Apps, "Features table"),
        new("docs/ARCHITECTURE.md", @"# (\d+) tweak definitions", s => s.Tweaks, "folder map"),
    };

    public static int Main(string[] args)
    {
        string? root = FindRepoRoot(AppContext.BaseDirectory) ?? FindRepoRoot(Directory.GetCurrentDirectory());
        if (root == null)
        {
            Console.Error.WriteLine("[FAIL] Could not find the repository root (WinVelo.sln).");
            return 2;
        }

        Stats stats;
        try
        {
            stats = Count(Path.Combine(root, "Catalog"));
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine("[FAIL] Could not read the catalog: " + ex.Message);
            return 2;
        }

        if (args.Contains("--count"))
        {
            Console.WriteLine(stats.Tweaks);
            return 0;
        }

        bool update = args.Contains("--update");
        Console.WriteLine($"Catalog: {stats.Tweaks} tweaks, {stats.Apps} apps, {stats.Presets} presets.");

        int stale = 0;
        foreach (var group in Patterns.GroupBy(p => p.File))
        {
            string path = Path.Combine(root, group.Key);
            if (!File.Exists(path))
            {
                Console.WriteLine($"  [FAIL] {group.Key} not found.");
                stale++;
                continue;
            }

            string text = File.ReadAllText(path);
            string original = text;
            foreach (var pattern in group)
            {
                int expected = pattern.Value(stats);
                var matches = Regex.Matches(text, pattern.Regex);
                if (matches.Count == 0)
                {
                    Console.WriteLine($"  [FAIL] {group.Key}: the {pattern.What} ('{pattern.Regex}') was not found.");
                    stale++;
                    continue;
                }

                foreach (Match m in matches)
                {
                    if (m.Groups[1].Value != expected.ToString())
                    {
                        Console.WriteLine($"  [{(update ? "FIX" : "FAIL")}] {group.Key}: {pattern.What} says {m.Groups[1].Value}, the catalog has {expected}.");
                        stale++;
                    }
                }

                text = Regex.Replace(text, pattern.Regex, m =>
                    m.Value.Substring(0, m.Groups[1].Index - m.Index) + expected + m.Value.Substring(m.Groups[1].Index - m.Index + m.Groups[1].Length));
            }

            if (update && text != original)
            {
                File.WriteAllText(path, text);
            }
        }

        if (stale == 0)
        {
            Console.WriteLine("  [PASS] README and docs match the catalog.");
            return 0;
        }

        if (update)
        {
            Console.WriteLine($"  Updated {stale} number(s).");
            return 0;
        }

        Console.WriteLine("Run 'dotnet run --project tools/CatalogStats -- --update' and commit the result.");
        return 1;
    }

    public static Stats Count(string catalogDir)
    {
        var source = CatalogSource.FromFolderOnly(catalogDir);
        var tweaks = CatalogLoader.LoadTweaks(source);
        if (tweaks.Issues.Count > 0)
        {
            throw new IOException("The catalog has problems: " + string.Join("; ", tweaks.Issues));
        }

        int apps = 0;
        using (var doc = CatalogLoader.LoadDocument(source, "Apps/applications.json"))
        {
            if (doc != null && doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                apps = doc.RootElement.EnumerateObject().Count(p => p.Value.ValueKind == JsonValueKind.Object);
            }
        }

        int presets = CatalogLoader.LoadPresets(source).Items.Count;
        return new Stats(tweaks.Items.Count, apps, presets);
    }

    private static string? FindRepoRoot(string start)
    {
        string? dir = start;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "WinVelo.sln"))) return dir;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }
}
