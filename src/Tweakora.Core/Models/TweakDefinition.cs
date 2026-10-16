using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tweakora.Core.Models;

public sealed class DetectStructure
{
    [JsonPropertyName("all")]
    public List<DetectRule>? All { get; set; }

    [JsonPropertyName("any")]
    public List<DetectRule>? Any { get; set; }

    // If it's a single rule directly on the detect object
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("hive")]
    public string? Hive { get; set; }

    [JsonPropertyName("key")]
    public string? Key { get; set; }

    [JsonPropertyName("valueName")]
    public string? ValueName { get; set; }

    [JsonPropertyName("matchType")]
    public string? MatchType { get; set; }

    [JsonPropertyName("expectedValue")]
    public JsonElement? ExpectedValue { get; set; }

    [JsonPropertyName("serviceName")]
    public string? ServiceName { get; set; }

    [JsonPropertyName("expectedStartup")]
    public string? ExpectedStartup { get; set; }

    [JsonPropertyName("expectedState")]
    public string? ExpectedState { get; set; }

    [JsonPropertyName("taskPath")]
    public string? TaskPath { get; set; }

    [JsonPropertyName("taskName")]
    public string? TaskName { get; set; }

    [JsonPropertyName("packagePattern")]
    public string? PackagePattern { get; set; }

    [JsonPropertyName("expectedInstalled")]
    public bool? ExpectedInstalled { get; set; }

    [JsonPropertyName("featureName")]
    public string? FeatureName { get; set; }

    [JsonPropertyName("expectedEnabled")]
    public bool? ExpectedEnabled { get; set; }

    [JsonIgnore]
    public bool IsComposite => (All != null && All.Count > 0) || (Any != null && Any.Count > 0);

    public DetectRule AsSingleRule()
    {
        return new DetectRule
        {
            Type = Type ?? string.Empty,
            Hive = Hive,
            Key = Key,
            ValueName = ValueName,
            MatchType = MatchType,
            ExpectedValue = ExpectedValue,
            ServiceName = ServiceName,
            ExpectedStartup = ExpectedStartup,
            ExpectedState = ExpectedState,
            TaskPath = TaskPath,
            TaskName = TaskName,
            PackagePattern = PackagePattern,
            ExpectedInstalled = ExpectedInstalled,
            FeatureName = FeatureName,
            ExpectedEnabled = ExpectedEnabled
        };
    }
}

public sealed class TweakDefinition
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; set; } = "1.0.0";

    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("origin")]
    public string? Origin { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("effectNote")]
    public string? EffectNote { get; set; }

    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    [JsonPropertyName("subcategory")]
    public string? Subcategory { get; set; }

    [JsonPropertyName("risk")]
    public string Risk { get; set; } = "Safe";

    [JsonPropertyName("impacts")]
    public List<string> Impacts { get; set; } = new();

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = new();

    [JsonPropertyName("presets")]
    public List<string>? Presets { get; set; }

    [JsonPropertyName("dependsOn")]
    public List<string>? DependsOn { get; set; }

    [JsonPropertyName("conflictsWith")]
    public List<string>? ConflictsWith { get; set; }

    [JsonPropertyName("aliases")]
    public List<string>? Aliases { get; set; }

    [JsonPropertyName("relatedTweaks")]
    public List<string>? RelatedTweaks { get; set; }

    [JsonPropertyName("oppositeOf")]
    [JsonConverter(typeof(SingleOrListStringConverter))]
    public List<string>? OppositeOf { get; set; }

    [JsonPropertyName("windows")]
    public WindowsRequirements Windows { get; set; } = new();

    [JsonPropertyName("requiresReboot")]
    public bool RequiresReboot { get; set; }

    [JsonPropertyName("requiresExplorerRestart")]
    public bool RequiresExplorerRestart { get; set; }

    [JsonPropertyName("detect")]
    public DetectStructure Detect { get; set; } = new();

    [JsonPropertyName("apply")]
    public List<ActionItem> Apply { get; set; } = new();

    [JsonPropertyName("undo")]
    public List<ActionItem> Undo { get; set; } = new();

    [JsonPropertyName("source")]
    public JsonElement Source { get; set; }

    [JsonPropertyName("verifiedOnBuild")]
    public string VerifiedOnBuild { get; set; } = string.Empty;

    [JsonPropertyName("warnings")]
    public string? Warnings { get; set; }

    [JsonPropertyName("reversible")]
    public bool Reversible { get; set; } = true;

    [JsonPropertyName("irreversibleReason")]
    public string? IrreversibleReason { get; set; }

    [JsonPropertyName("needsReview")]
    public bool NeedsReview { get; set; }

    [JsonPropertyName("evidence")]
    public string Evidence { get; set; } = "documented";

    [JsonPropertyName("uiEquivalentLocation")]
    public string? UiEquivalentLocation { get; set; }

    [JsonPropertyName("whatItDoes")]
    public string? WhatItDoes { get; set; }

    [JsonPropertyName("sideEffects")]
    public string? SideEffects { get; set; }

    [JsonPropertyName("sources")]
    public List<string>? Sources { get; set; }

    [JsonPropertyName("sourceQuote")]
    public string? SourceQuote { get; set; }

    /// <summary>
    /// How presets may use this tweak: "optimization" (eligible for presets), "preference" (personal taste),
    /// "fix" (only for a specific problem), "action" (one-time operation) or "advanced" (manual tuning).
    /// Anything other than "optimization" is never included in a generated preset.
    /// </summary>
    [JsonPropertyName("presetRole")]
    public string PresetRole { get; set; } = "unspecified";
}

public sealed class SingleOrListStringConverter : JsonConverter<List<string>>
{
    public override List<string>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;
        if (reader.TokenType == JsonTokenType.String)
        {
            var str = reader.GetString();
            return str == null ? null : new List<string> { str };
        }
        if (reader.TokenType == JsonTokenType.StartArray)
        {
            var list = new List<string>();
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndArray) break;
                if (reader.TokenType == JsonTokenType.String)
                {
                    var item = reader.GetString();
                    if (item != null) list.Add(item);
                }
            }
            return list;
        }
        return null;
    }

    public override void Write(Utf8JsonWriter writer, List<string>? value, JsonSerializerOptions options)
    {
        if (value == null)
        {
            writer.WriteNullValue();
            return;
        }
        if (value.Count == 1)
        {
            writer.WriteStringValue(value[0]);
        }
        else
        {
            writer.WriteStartArray();
            foreach (var item in value)
            {
                writer.WriteStringValue(item);
            }
            writer.WriteEndArray();
        }
    }
}
