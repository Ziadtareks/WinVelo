using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace WinVelo.Tests;

/// <summary>Keeps the English and Arabic UI text in sync with the code and the catalog.</summary>
public sealed class LocalizationTests
{
    private static string RepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "WinVelo.sln"))) return dir;
            dir = Path.GetDirectoryName(dir);
        }
        throw new InvalidOperationException("Repository root not found.");
    }

    private static Dictionary<string, string> LoadDict(string path) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))!;

    private static string LocDir => Path.Combine(RepoRoot(), "src", "WinVelo.App", "Localization");

    private static HashSet<string> Placeholders(string s) =>
        Regex.Matches(s, @"\{(\d+)").Select(m => m.Groups[1].Value).ToHashSet();

    [Fact]
    public void Arabic_HasEveryEnglishKey_WithSamePlaceholders()
    {
        var en = LoadDict(Path.Combine(LocDir, "Lang-en.json"));
        var ar = LoadDict(Path.Combine(LocDir, "Lang-ar.json"));

        var missing = en.Keys.Where(k => !ar.ContainsKey(k)).ToList();
        Assert.True(missing.Count == 0, "Missing Arabic keys: " + string.Join(", ", missing));

        foreach (var (key, text) in en)
        {
            Assert.True(Placeholders(text).SetEquals(Placeholders(ar[key])),
                $"Placeholder mismatch for '{key}': '{text}' vs '{ar[key]}'");
        }
    }

    [Fact]
    public void XamlKeys_AllExistInEnglishDictionary()
    {
        var en = LoadDict(Path.Combine(LocDir, "Lang-en.json"));
        string viewsDir = Path.Combine(RepoRoot(), "src", "WinVelo.App", "Views");
        var missing = new List<string>();
        foreach (var file in Directory.GetFiles(viewsDir, "*.xaml"))
        {
            foreach (Match m in Regex.Matches(File.ReadAllText(file), @"\{loc:Tr\s+([A-Za-z0-9_]+)\}"))
            {
                if (!en.ContainsKey(m.Groups[1].Value)) missing.Add($"{Path.GetFileName(file)}: {m.Groups[1].Value}");
            }
        }
        Assert.True(missing.Count == 0, "Unknown XAML keys: " + string.Join(", ", missing));
    }

    [Fact]
    public void ViewModelTexts_AllHaveArabicTranslation()
    {
        var ar = LoadDict(Path.Combine(LocDir, "Lang-ar.json"));
        // Every C# file of the app (view models, App.xaml.cs, services), not only ViewModels/.
        string appDir = Path.Combine(RepoRoot(), "src", "WinVelo.App");
        var missing = new List<string>();
        foreach (var file in Directory.GetFiles(appDir, "*.cs", SearchOption.AllDirectories)
                                      .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                                                  !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")))
        {
            foreach (Match m in Regex.Matches(File.ReadAllText(file), "LocalizationManager\\.T\\(\"((?:[^\"\\\\]|\\\\.)*)\""))
            {
                string key = Regex.Unescape(m.Groups[1].Value);
                if (!ar.ContainsKey(key)) missing.Add(key);
                else
                {
                    var pe = Placeholders(key);
                    Assert.True(Placeholders(ar[key]).IsSubsetOf(pe), $"Arabic text for '{key}' uses unknown placeholders.");
                }
            }
        }
        Assert.True(missing.Count == 0, "Missing Arabic for: " + string.Join(" | ", missing.Distinct()));
    }

    [Fact]
    public void EnglishAndArabicDictionaries_HaveTheSameKeyedEntries()
    {
        var en = LoadDict(Path.Combine(LocDir, "Lang-en.json"));
        var ar = LoadDict(Path.Combine(LocDir, "Lang-ar.json"));

        // Lang-en.json holds the short XAML keys; Lang-ar.json holds those plus English sentences used by T().
        // Every short key must exist in both, and no Arabic entry may be empty.
        var keyed = ar.Keys.Where(k => Regex.IsMatch(k, @"^[A-Za-z]+_[A-Za-z0-9_]+$")).ToList();
        Assert.True(keyed.All(en.ContainsKey), "Arabic XAML keys missing in English: " + string.Join(", ", keyed.Where(k => !en.ContainsKey(k))));
        Assert.True(ar.All(kv => !string.IsNullOrWhiteSpace(kv.Value)), "Empty Arabic texts: " + string.Join(", ", ar.Where(kv => string.IsNullOrWhiteSpace(kv.Value)).Select(kv => kv.Key)));
    }

    [Fact]
    public void CatalogArabic_CoversEveryTweakAndPreset()
    {
        string catalog = Path.Combine(RepoRoot(), "Catalog");
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(catalog, "Localization", "ar.json")));
        var tweaks = doc.RootElement.GetProperty("tweaks");
        var presets = doc.RootElement.GetProperty("presets");

        foreach (var file in Directory.GetFiles(Path.Combine(catalog, "Tweaks"), "*.json"))
        {
            using var t = JsonDocument.Parse(File.ReadAllText(file));
            foreach (var tweak in t.RootElement.EnumerateArray())
            {
                string id = tweak.GetProperty("id").GetString()!;
                Assert.True(tweaks.TryGetProperty(id, out var tr), $"No Arabic text for tweak '{id}'.");
                Assert.False(string.IsNullOrWhiteSpace(tr.GetProperty("name").GetString()), $"Empty Arabic name for '{id}'.");
            }
        }

        foreach (var file in Directory.GetFiles(Path.Combine(catalog, "Presets"), "*.json"))
        {
            using var p = JsonDocument.Parse(File.ReadAllText(file));
            string id = p.RootElement.GetProperty("id").GetString()!;
            Assert.True(presets.TryGetProperty(id, out _), $"No Arabic text for preset '{id}'.");
        }
    }
}
