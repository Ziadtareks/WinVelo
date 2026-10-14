using System.Text.Json.Serialization;

namespace Tweakora.Core.Models;

public sealed class JournalActionRecord
{
    [JsonPropertyName("tweakId")]
    public string TweakId { get; set; } = string.Empty;

    [JsonPropertyName("actionIndex")]
    public int ActionIndex { get; set; }

    [JsonPropertyName("actionType")]
    public string ActionType { get; set; } = string.Empty;

    [JsonPropertyName("targetIdentifier")]
    public string TargetIdentifier { get; set; } = string.Empty;

    [JsonPropertyName("existedBefore")]
    public bool ExistedBefore { get; set; }

    [JsonPropertyName("previousValueKind")]
    public string? PreviousValueKind { get; set; }

    [JsonPropertyName("previousData")]
    public string? PreviousData { get; set; }

    [JsonPropertyName("keyCreated")]
    public bool KeyCreated { get; set; }

    [JsonPropertyName("isBaseline")]
    public bool IsBaseline { get; set; } = true;

    [JsonPropertyName("isReverted")]
    public bool IsReverted { get; set; }

    [JsonPropertyName("revertedAtUtc")]
    public DateTime? RevertedAtUtc { get; set; }

    [JsonPropertyName("appliedAtUtc")]
    public DateTime AppliedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class RollbackJournalSession
{
    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("startedAtUtc")]
    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("isCompleted")]
    public bool IsCompleted { get; set; }

    [JsonPropertyName("records")]
    public List<JournalActionRecord> Records { get; set; } = new();
}
