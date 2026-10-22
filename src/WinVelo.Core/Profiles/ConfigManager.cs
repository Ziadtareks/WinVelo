using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinVelo.Core.Profiles;

public sealed class WinVeloConfig
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

    [JsonPropertyName("dnsAdapterIndex")]
    public int? DnsAdapterIndex { get; set; }

    [JsonPropertyName("useDnsOverHttps")]
    public bool UseDnsOverHttps { get; set; }

    [JsonPropertyName("updatesMode")]
    public string? UpdatesMode { get; set; }
}

public static class ConfigManager
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string ExportConfigJson(WinVeloConfig config)
    {
        return JsonSerializer.Serialize(config, JsonOptions);
    }

    public static string ExportConfigToJson(WinVeloConfig config) => ExportConfigJson(config);

    public static WinVeloConfig? ImportConfigJson(string json)
    {
        try
        {
            var config = JsonSerializer.Deserialize<WinVeloConfig>(json, JsonOptions);
            if (config == null) return null;
            config.SelectedTweaks ??= new(); config.SelectedApps ??= new(); config.SelectedFeatures ??= new(); config.StagedToggles ??= new();
            if (config.SelectedTweaks.Concat(config.SelectedApps).Concat(config.SelectedFeatures).Any(string.IsNullOrWhiteSpace)) return null;
            return config;
        }
        catch
        {
            return null;
        }
    }

    public static WinVeloConfig? ImportConfigFromJson(string json) => ImportConfigJson(json);

    public static void SaveToFile(WinVeloConfig config, string filePath)
    {
        string json = ExportConfigJson(config);
        File.WriteAllText(filePath, json);
    }

    public static WinVeloConfig? LoadFromFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return null;
        }

        string json = File.ReadAllText(filePath);
        return ImportConfigJson(json);
    }
}
