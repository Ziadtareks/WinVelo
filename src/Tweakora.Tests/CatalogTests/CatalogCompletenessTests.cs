using System.Text.Json;
using Tweakora.Core.Models;
using Xunit;

namespace Tweakora.Tests.CatalogTests;

public sealed class CatalogCompletenessTests
{
    [Fact]
    public void AllCatalogTweaks_HaveValidDetectApplyUndoAndSource()
    {
        string baseDir = AppContext.BaseDirectory;
        string tweaksDir = FindDirectoryUpwards(baseDir, Path.Combine("Catalog", "Tweaks"));

        Assert.True(Directory.Exists(tweaksDir), $"Tweaks directory not found at {tweaksDir}");

        string[] jsonFiles = Directory.GetFiles(tweaksDir, "*.json", SearchOption.AllDirectories);
        Assert.NotEmpty(jsonFiles);

        List<TweakDefinition> allTweaks = new();
        JsonSerializerOptions options = new() { PropertyNameCaseInsensitive = true };

        foreach (string file in jsonFiles)
        {
            string content = File.ReadAllText(file);
            List<TweakDefinition>? list = JsonSerializer.Deserialize<List<TweakDefinition>>(content, options);
            Assert.NotNull(list);
            allTweaks.AddRange(list);
        }

        // Verify all 143 verified tweaks in catalog
        Assert.Equal(143, allTweaks.Count);

        HashSet<string> seenIds = new(StringComparer.OrdinalIgnoreCase);

        foreach (TweakDefinition tweak in allTweaks)
        {
            // ID check
            Assert.False(string.IsNullOrWhiteSpace(tweak.Id), "Tweak ID is empty");
            Assert.True(seenIds.Add(tweak.Id), $"Duplicate tweak ID: {tweak.Id}");

            // Name & Description check
            Assert.False(string.IsNullOrWhiteSpace(tweak.Name), $"Tweak '{tweak.Id}' missing Name");
            Assert.False(string.IsNullOrWhiteSpace(tweak.Description), $"Tweak '{tweak.Id}' missing Description");

            // Category & Risk
            Assert.False(string.IsNullOrWhiteSpace(tweak.Category), $"Tweak '{tweak.Id}' missing Category");
            Assert.Contains(tweak.Risk, new[] { "Safe", "Low", "Medium", "High" });

            // Detect rule check
            bool hasDetect = !string.IsNullOrWhiteSpace(tweak.Detect.Type) ||
                             (tweak.Detect.All != null && tweak.Detect.All.Count > 0) ||
                             (tweak.Detect.Any != null && tweak.Detect.Any.Count > 0);
            Assert.True(hasDetect, $"Tweak '{tweak.Id}' has no valid detect rule");

            // Apply actions check
            Assert.NotNull(tweak.Apply);
            Assert.NotEmpty(tweak.Apply);
            foreach (var action in tweak.Apply)
            {
                Assert.False(string.IsNullOrWhiteSpace(action.Type), $"Tweak '{tweak.Id}' has apply action with missing Type");
                Assert.False(string.IsNullOrWhiteSpace(action.Operation), $"Tweak '{tweak.Id}' has apply action with missing Operation");
            }

            // Undo actions check
            Assert.NotNull(tweak.Undo);
            Assert.NotEmpty(tweak.Undo);
            foreach (var action in tweak.Undo)
            {
                Assert.False(string.IsNullOrWhiteSpace(action.Type), $"Tweak '{tweak.Id}' has undo action with missing Type");
                Assert.False(string.IsNullOrWhiteSpace(action.Operation), $"Tweak '{tweak.Id}' has undo action with missing Operation");
            }

            // Source check
            Assert.True(tweak.Source.ValueKind == JsonValueKind.String || tweak.Source.ValueKind == JsonValueKind.Array,
                $"Tweak '{tweak.Id}' source must be string or array");
            string sourceStr = tweak.Source.ToString();
            Assert.False(string.IsNullOrWhiteSpace(sourceStr), $"Tweak '{tweak.Id}' has empty source URL");
        }
    }

    private static string FindDirectoryUpwards(string startDir, string relativePath)
    {
        string? current = startDir;
        while (current != null)
        {
            string candidate = Path.Combine(current, relativePath);
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
            current = Directory.GetParent(current)?.FullName;
        }
        return Path.Combine(startDir, relativePath);
    }
}
