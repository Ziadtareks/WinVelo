using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinVelo.Core.Models;

public sealed class ActionItem
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("hive")]
    public string? Hive { get; set; }

    [JsonPropertyName("key")]
    public string? Key { get; set; }

    [JsonPropertyName("operation")]
    public string Operation { get; set; } = string.Empty;

    [JsonPropertyName("valueName")]
    public string? ValueName { get; set; }

    [JsonPropertyName("valueKind")]
    public string? ValueKind { get; set; }

    [JsonPropertyName("data")]
    public JsonElement? Data { get; set; }

    [JsonPropertyName("serviceName")]
    public string? ServiceName { get; set; }

    [JsonPropertyName("startupType")]
    public string? StartupType { get; set; }

    [JsonPropertyName("taskPath")]
    public string? TaskPath { get; set; }

    [JsonPropertyName("taskName")]
    public string? TaskName { get; set; }

    [JsonPropertyName("packagePattern")]
    public string? PackagePattern { get; set; }

    [JsonPropertyName("allUsers")]
    public bool? AllUsers { get; set; }

    [JsonPropertyName("featureName")]
    public string? FeatureName { get; set; }

    [JsonPropertyName("arguments")]
    public string? Arguments { get; set; }

    [JsonPropertyName("pathPattern")]
    public string? PathPattern { get; set; }

    [JsonPropertyName("allowedRoot")]
    public string? AllowedRoot { get; set; }

    [JsonPropertyName("stopServicesBefore")]
    public List<string>? StopServicesBefore { get; set; }

    [JsonPropertyName("startServicesAfter")]
    public List<string>? StartServicesAfter { get; set; }

    [JsonPropertyName("script")]
    public string? Script { get; set; }

    [JsonPropertyName("timeoutSeconds")]
    public int? TimeoutSeconds { get; set; }

    [JsonPropertyName("captureRegistryState")]
    public bool CaptureRegistryState { get; set; }

    [JsonPropertyName("captureUndoScript")]
    public string? CaptureUndoScript { get; set; }
}

public sealed class DetectRule
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

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
}
