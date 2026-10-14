using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Tweakora.Core.Models;

public sealed class PresetDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("tweakIds")]
    public List<string> TweakIds { get; set; } = new();
}
