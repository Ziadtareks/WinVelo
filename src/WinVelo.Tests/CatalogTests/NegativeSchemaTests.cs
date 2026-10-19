using System.Text.Json.Nodes;
using Json.Schema;
using Xunit;

namespace WinVelo.Tests.CatalogTests;

public sealed class NegativeSchemaTests
{
    private readonly JsonSchema _schema;

    public NegativeSchemaTests()
    {
        string baseDir = AppContext.BaseDirectory;
        string schemaPath = FindFileUpwards(baseDir, Path.Combine("Catalog", "schema", "tweak.schema.json"));
        string schemaJson = File.ReadAllText(schemaPath);
        _schema = JsonSchema.FromText(schemaJson);
    }

    [Fact]
    public void Schema_RejectsTweak_WithoutSchemaVersion()
    {
        JsonObject json = CreateValidTweakJson();
        json.Remove("schemaVersion");

        EvaluationResults result = _schema.Evaluate(json);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Schema_RejectsTweak_WithInvalidRisk()
    {
        JsonObject json = CreateValidTweakJson();
        json["risk"] = "ExtremeDanger";

        EvaluationResults result = _schema.Evaluate(json);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Schema_RejectsTweak_WithCommandDetectType()
    {
        JsonObject json = CreateValidTweakJson();
        json["detect"] = new JsonObject
        {
            ["type"] = "Command",
            ["command"] = "echo 1"
        };

        EvaluationResults result = _schema.Evaluate(json);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Schema_RejectsRegistrySet_WhenValueKindOrDataMissing()
    {
        JsonObject json = CreateValidTweakJson();
        json["apply"] = new JsonArray
        {
            new JsonObject
            {
                ["type"] = "Registry",
                ["hive"] = "HKCU",
                ["key"] = "Software\\Test",
                ["operation"] = "Set"
                // Missing valueName, valueKind, data
            }
        };

        EvaluationResults result = _schema.Evaluate(json);
        Assert.False(result.IsValid);
    }

    private static JsonObject CreateValidTweakJson()
    {
        return new JsonObject
        {
            ["schemaVersion"] = "1.0.0",
            ["id"] = "test.sample.rule",
            ["name"] = "Test Rule",
            ["description"] = "A valid test rule for schema checks.",
            ["category"] = "privacy",
            ["risk"] = "Safe",
            ["impacts"] = new JsonArray { "privacy" },
            ["windows"] = new JsonObject { ["minBuild"] = 19041 },
            ["requiresReboot"] = false,
            ["requiresExplorerRestart"] = false,
            ["detect"] = new JsonObject
            {
                ["type"] = "Registry",
                ["hive"] = "HKCU",
                ["key"] = "Software\\Test",
                ["valueName"] = "Enabled",
                ["matchType"] = "Equals",
                ["expectedValue"] = 1
            },
            ["apply"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "Registry",
                    ["hive"] = "HKCU",
                    ["key"] = "Software\\Test",
                    ["valueName"] = "Enabled",
                    ["valueKind"] = "DWord",
                    ["data"] = 1,
                    ["operation"] = "Set"
                }
            },
            ["undo"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "Registry",
                    ["hive"] = "HKCU",
                    ["key"] = "Software\\Test",
                    ["valueName"] = "Enabled",
                    ["operation"] = "DeleteValue"
                }
            },
            ["source"] = "https://learn.microsoft.com",
            ["verifiedOnBuild"] = "22631"
        };
    }

    private static string FindFileUpwards(string startDir, string relativePath)
    {
        string? current = startDir;
        while (current != null)
        {
            string candidate = Path.Combine(current, relativePath);
            if (File.Exists(candidate)) return candidate;
            current = Directory.GetParent(current)?.FullName;
        }
        return Path.Combine(startDir, relativePath);
    }
}
