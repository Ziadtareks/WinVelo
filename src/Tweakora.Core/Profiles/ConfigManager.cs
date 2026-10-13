using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tweakora.Core.Profiles;

public sealed class TweakoraConfig
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; set; } = "1.0.0";

    [JsonPropertyName("exportedAtUtc")]
    public DateTime ExportedAtUtc { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("selectedApps")]
    public List<string> SelectedApps { get; set; } = new();

    [JsonPropertyName("selectedTweaks")]
    public List<string> SelectedTweaks { get; set; } = new();

    [JsonPropertyName("stagedToggles")]
    public Dictionary<string, bool> StagedToggles { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("selectedFeatures")]
    public List<string> SelectedFeatures { get; set; } = new();

    [JsonPropertyName("selectedDns")]
    public string? SelectedDns { get; set; }

    [JsonPropertyName("updatesMode")]
    public string? UpdatesMode { get; set; }
}

public static class ConfigManager
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string ExportConfigJson(TweakoraConfig config)
    {
        return JsonSerializer.Serialize(config, JsonOptions);
    }

    public static string ExportConfigToJson(TweakoraConfig config) => ExportConfigJson(config);

    public static TweakoraConfig? ImportConfigJson(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<TweakoraConfig>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public static TweakoraConfig? ImportConfigFromJson(string json) => ImportConfigJson(json);

    public static void SaveToFile(TweakoraConfig config, string filePath)
    {
        string json = ExportConfigJson(config);
        File.WriteAllText(filePath, json);
    }

    public static TweakoraConfig? LoadFromFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return null;
        }

        string json = File.ReadAllText(filePath);
        return ImportConfigJson(json);
    }
}
