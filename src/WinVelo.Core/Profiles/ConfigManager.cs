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

    [JsonPropertyName("updatesMode")]
    public string? UpdatesMode { get; set; }

    /// <summary>Built-in Windows apps to remove (WinUtil appx.json keys such as WPFAppxMicrosoft_BingNews).</summary>
    [JsonPropertyName("selectedAppx")]
    public List<string> SelectedAppx { get; set; } = new();

    [JsonIgnore]
    public int SelectionCount => SelectedApps.Count + SelectedTweaks.Count + SelectedFeatures.Count + SelectedAppx.Count + StagedToggles.Count;
}

public static class ConfigManager
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string ExportConfigJson(WinVeloConfig config)
    {
        return JsonSerializer.Serialize(config, JsonOptions);
    }

    public static string ExportConfigToJson(WinVeloConfig config) => ExportConfigJson(config);

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>
    /// Reads a WinVelo config, a WinUtil config (a flat JSON array of WPF* keys) or an old WinUtil config
    /// (an object whose WPFInstall/WPFTweaks/... properties hold arrays of keys). Returns null if the text is not one of these.
    /// </summary>
    public static WinVeloConfig? ImportConfigJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Array)
            {
                return FromWinUtilKeys(ReadStrings(root));
            }

            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            bool isLegacyWinUtil = false;
            foreach (var prop in root.EnumerateObject())
            {
                if (prop.Name.StartsWith("WPF", StringComparison.OrdinalIgnoreCase) && prop.Value.ValueKind == JsonValueKind.Array)
                {
                    isLegacyWinUtil = true;
                    break;
                }
            }

            if (isLegacyWinUtil)
            {
                var keys = new List<string>();
                foreach (var prop in root.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.Array &&
                        System.Text.RegularExpressions.Regex.IsMatch(prop.Name, "^WPF(?:Install|Tweaks|Toggle|Feature|Appx)", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    {
                        keys.AddRange(ReadStrings(prop.Value));
                    }
                }
                return FromWinUtilKeys(keys);
            }

            var config = root.Deserialize<WinVeloConfig>(ReadOptions);
            if (config == null) return null;
            config.SelectedApps ??= new();
            config.SelectedTweaks ??= new();
            config.SelectedFeatures ??= new();
            config.SelectedAppx ??= new();
            config.StagedToggles = new Dictionary<string, bool>(config.StagedToggles ?? new(), StringComparer.OrdinalIgnoreCase);
            return config;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Sorts WinUtil selection keys into a config, the same way WinUtil's Update-WinUtilSelections does.</summary>
    public static WinVeloConfig FromWinUtilKeys(IEnumerable<string> keys)
    {
        var config = new WinVeloConfig();
        foreach (string raw in keys)
        {
            string key = raw?.Trim() ?? string.Empty;
            if (key.Length == 0) continue;

            if (key.StartsWith("WPFInstall", StringComparison.OrdinalIgnoreCase))
            {
                config.SelectedApps.Add(key.Substring("WPFInstall".Length));
            }
            else if (key.StartsWith("WPFAppx", StringComparison.OrdinalIgnoreCase))
            {
                config.SelectedAppx.Add(key);
            }
            else if (key.StartsWith("WPFFeature", StringComparison.OrdinalIgnoreCase))
            {
                config.SelectedFeatures.Add(key);
            }
            else
            {
                // WPFTweaks*, WPFToggle* and WinVelo ids are resolved against the tweak catalog (ids, origins, aliases).
                config.SelectedTweaks.Add(key);
            }
        }
        return config;
    }

    private static List<string> ReadStrings(JsonElement array)
    {
        var list = new List<string>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
            {
                list.Add(item.GetString()!);
            }
        }
        return list;
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
