using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Tweakora.Core.Models;

public sealed class ExcludedPresetTweak
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = string.Empty;
}

public sealed class PresetDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("standard")]
    public List<string> Standard { get; set; } = new();

    [JsonPropertyName("full")]
    public List<string> Full { get; set; } = new();

    [JsonPropertyName("excluded")]
    public List<ExcludedPresetTweak> Excluded { get; set; } = new();

    [JsonPropertyName("tweakIds")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    public List<string>? LegacyTweakIds
    {
        get => null;
        set
        {
            if (value != null && Standard.Count == 0 && Full.Count == 0)
            {
                Standard = value;
                Full = value;
            }
        }
    }

    [JsonIgnore]
    public List<string> TweakIds => Standard.Count > 0 ? Standard : Full;
}
