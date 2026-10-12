using System.Text.Json.Nodes;
using Json.Schema;
using Xunit;

namespace Tweakora.Tests.CatalogTests;

public sealed class CatalogSchemaTests
{
    [Fact]
    public void PrivacyTweak_PassesStrictSchemaValidation()
    {
        // Arrange
        string baseDir = AppContext.BaseDirectory;
        string schemaPath = FindFileUpwards(baseDir, Path.Combine("Catalog", "schema", "tweak.schema.json"));
        string tweakPath = FindFileUpwards(baseDir, Path.Combine("Catalog", "Tweaks", "privacy.json"));

        Assert.True(File.Exists(schemaPath), $"Schema not found at {schemaPath}");
        Assert.True(File.Exists(tweakPath), $"Tweak not found at {tweakPath}");

        string schemaJson = File.ReadAllText(schemaPath);
        string tweakJson = File.ReadAllText(tweakPath);

        JsonSchema schema = JsonSchema.FromText(schemaJson);
        JsonNode? tweakNode = JsonNode.Parse(tweakJson);

        Assert.NotNull(tweakNode);
        JsonArray array = Assert.IsAssignableFrom<JsonArray>(tweakNode);
        Assert.NotEmpty(array);

        // Act & Assert
        foreach (JsonNode? item in array)
        {
            Assert.NotNull(item);
            EvaluationResults result = schema.Evaluate(item, new EvaluationOptions
            {
                OutputFormat = OutputFormat.List
            });

            Assert.True(result.IsValid, $"Validation failed: {string.Join(", ", result.Details.Where(d => d.HasErrors).SelectMany(d => d.Errors?.Values ?? []))}");
        }
    }

    private static string FindFileUpwards(string startDir, string relativePath)
    {
        string? current = startDir;
        while (current != null)
        {
            string candidate = Path.Combine(current, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }
            current = Directory.GetParent(current)?.FullName;
        }
        return Path.Combine(startDir, relativePath);
    }
}
