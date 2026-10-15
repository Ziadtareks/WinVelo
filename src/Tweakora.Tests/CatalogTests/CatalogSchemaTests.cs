using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Json.Schema;
using Xunit;

namespace Tweakora.Tests.CatalogTests;

public sealed class CatalogSchemaTests
{
    [Fact]
    public void AllCatalogTweaks_PassStrictSchemaValidation()
    {
        // Arrange
        string baseDir = AppContext.BaseDirectory;
        string schemaPath = FindFileUpwards(baseDir, Path.Combine("Catalog", "schema", "tweak.schema.json"));
        string tweaksDir = FindDirectoryUpwards(baseDir, Path.Combine("Catalog", "Tweaks"));

        Assert.True(File.Exists(schemaPath), $"Schema not found at {schemaPath}");
        Assert.True(Directory.Exists(tweaksDir), $"Tweaks directory not found at {tweaksDir}");

        string schemaJson = File.ReadAllText(schemaPath);
        JsonSchema schema = JsonSchema.FromText(schemaJson);

        string[] tweakFiles = Directory.GetFiles(tweaksDir, "*.json", SearchOption.AllDirectories);
        Assert.NotEmpty(tweakFiles);

        int totalEvaluated = 0;

        foreach (string tweakFile in tweakFiles)
        {
            string tweakJson = File.ReadAllText(tweakFile);
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

                string errStr = string.Join("; ", result.Details.Where(d => d.HasErrors).SelectMany(d => d.Errors?.Values ?? []));
                Assert.True(result.IsValid, $"File {Path.GetFileName(tweakFile)}: Tweak validation failed: {errStr}");
                totalEvaluated++;
            }
        }

        Assert.Equal(137, totalEvaluated);
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
