using System.Text.Json;
using Tweakora.Core.Models;

namespace Tweakora.Core.Engine.Handlers;

public sealed class RegistryActionHandler : IActionHandler
{
    public bool CanHandle(string actionType) =>
        actionType.Equals("Registry", StringComparison.OrdinalIgnoreCase) ||
        actionType.Equals("Policy", StringComparison.OrdinalIgnoreCase);

    public Task<ActionResult> ExecuteAsync(ActionItem action, string tweakId, int actionIndex, ExecutionContext context)
    {
        if (string.IsNullOrWhiteSpace(action.Hive) || string.IsNullOrWhiteSpace(action.Key))
        {
            return Task.FromResult(new ActionResult { Success = false, Message = "Missing Hive or Key." });
        }

        string targetId = $"{action.Hive}\\{action.Key}\\{action.ValueName ?? "@"}";

        // Read current state before touching
        bool keyExisted = context.Registry.KeyExists(action.Hive, action.Key);
        bool valueExisted = !string.IsNullOrEmpty(action.ValueName) && context.Registry.ValueExists(action.Hive, action.Key, action.ValueName);
        object? previousData = valueExisted ? context.Registry.GetValue(action.Hive, action.Key, action.ValueName!) : null;
        string? previousKind = valueExisted ? context.Registry.GetValueKind(action.Hive, action.Key, action.ValueName!) : null;

        // Check for existing baseline in journal to preserve original
        var existingBaseline = context.Journal.FindExistingBaselineRecord(tweakId, targetId);
        bool isBaseline = existingBaseline == null;

        var journalRecord = new JournalActionRecord
        {
            TweakId = tweakId,
            ActionIndex = actionIndex,
            ActionType = action.Type,
            TargetIdentifier = targetId,
            ExistedBefore = valueExisted,
            PreviousValueKind = isBaseline ? previousKind : existingBaseline!.PreviousValueKind,
            PreviousData = isBaseline ? SerializeData(previousData) : existingBaseline!.PreviousData,
            KeyCreated = !keyExisted,
            IsBaseline = isBaseline,
            AppliedAtUtc = DateTime.UtcNow
        };

        // Write-Ahead Journaling: MUST flush before applying!
        if (!context.IsDryRun)
        {
            context.Journal.RecordActionWriteAhead(journalRecord);

            // Automatic .reg backup if not already dry run
            if (!string.IsNullOrEmpty(context.BackupDirectory) && keyExisted)
            {
                try
                {
                    context.Registry.ExportRegBackup(action.Hive, action.Key, context.BackupDirectory);
                }
                catch
                {
                    // Backup error shouldn't crash but will be logged
                }
            }

            // Perform actual change
            try
            {
                switch (action.Operation)
                {
                    case "Set":
                        object dataToSet = ParseData(action.Data, action.ValueKind ?? "DWord");
                        if (dataToSet is string s && s == "<RemoveEntry>")
                        {
                            if (keyExisted && valueExisted && !string.IsNullOrEmpty(action.ValueName))
                            {
                                context.Registry.DeleteValue(action.Hive, action.Key, action.ValueName);
                            }
                        }
                        else
                        {
                            if (!keyExisted) context.Registry.CreateKey(action.Hive, action.Key);
                            context.Registry.SetValue(action.Hive, action.Key, action.ValueName ?? string.Empty, dataToSet, action.ValueKind ?? "DWord");
                        }
                        break;

                    case "DeleteValue":
                        if (keyExisted && valueExisted && !string.IsNullOrEmpty(action.ValueName))
                        {
                            context.Registry.DeleteValue(action.Hive, action.Key, action.ValueName);
                        }
                        break;

                    case "DeleteKey":
                        if (keyExisted)
                        {
                            context.Registry.DeleteKey(action.Hive, action.Key);
                        }
                        break;

                    default:
                        return Task.FromResult(new ActionResult { Success = false, Message = $"Unknown registry operation: {action.Operation}" });
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
            {
                return Task.FromResult(new ActionResult
                {
                    Success = false,
                    Message = $"Access denied for {action.Hive}\\{action.Key} (protected by system or TrustedInstaller): {ex.Message}"
                });
            }
        }

        return Task.FromResult(new ActionResult
        {
            Success = true,
            JournalRecord = journalRecord
        });
    }

    public Task<ActionResult> UndoAsync(JournalActionRecord record, ExecutionContext context)
    {
        string[] parts = record.TargetIdentifier.Split('\\');
        if (parts.Length < 2)
        {
            return Task.FromResult(new ActionResult { Success = false, Message = "Invalid target identifier." });
        }

        string hive = parts[0];
        string valueName = parts[^1];
        string key = string.Join('\\', parts[1..^1]);

        if (valueName == "@") valueName = string.Empty;

        if (!record.ExistedBefore)
        {
            // Value did not exist originally -> delete it
            if (!string.IsNullOrEmpty(valueName) && context.Registry.ValueExists(hive, key, valueName))
            {
                context.Registry.DeleteValue(hive, key, valueName);
            }

            // If the whole key was created by us and is now empty -> delete key
            if (record.KeyCreated && context.Registry.KeyExists(hive, key))
            {
                context.Registry.DeleteKey(hive, key);
            }
        }
        else
        {
            // Value existed -> restore exact previous data & kind
            if (!context.Registry.KeyExists(hive, key))
            {
                context.Registry.CreateKey(hive, key);
            }

            object restoredData = DeserializeData(record.PreviousData, record.PreviousValueKind ?? "DWord");
            context.Registry.SetValue(hive, key, valueName, restoredData, record.PreviousValueKind ?? "DWord");
        }

        return Task.FromResult(new ActionResult { Success = true });
    }

    public ActionPreview Preview(ActionItem action, ExecutionContext context)
    {
        string targetId = $"{action.Hive}\\{action.Key}\\{action.ValueName ?? "@"}";
        bool valueExisted = !string.IsNullOrEmpty(action.ValueName) &&
            action.Hive != null && action.Key != null &&
            context.Registry.ValueExists(action.Hive, action.Key, action.ValueName);

        object? current = valueExisted ? context.Registry.GetValue(action.Hive!, action.Key!, action.ValueName!) : null;

        return new ActionPreview
        {
            ActionType = action.Type,
            Target = targetId,
            Operation = action.Operation,
            CurrentState = valueExisted ? current?.ToString() : "(Not set)",
            ProposedState = action.Operation == "Set" ? action.Data?.ToString() : "(Deleted)"
        };
    }

    private static string? SerializeData(object? data)
    {
        if (data == null) return null;
        if (data is byte[] bytes) return Convert.ToBase64String(bytes);
        return data.ToString();
    }

    private static object ParseData(JsonElement? element, string kind)
    {
        if (!element.HasValue) return 0;
        JsonElement val = element.Value;

        if (val.ValueKind == JsonValueKind.String && string.Equals(val.GetString(), "<RemoveEntry>", StringComparison.OrdinalIgnoreCase))
        {
            return "<RemoveEntry>";
        }

        return kind switch
        {
            "DWord" => val.ValueKind == JsonValueKind.Number ? val.GetInt32() : (int.TryParse(val.GetString(), out int d) ? d : 0),
            "QWord" => val.ValueKind == JsonValueKind.Number ? val.GetInt64() : (long.TryParse(val.GetString(), out long q) ? q : 0L),
            "Binary" => val.ValueKind == JsonValueKind.String ? Convert.FromBase64String(val.GetString()!) : Array.Empty<byte>(),
            _ => val.GetString() ?? string.Empty
        };
    }

    private static object DeserializeData(string? data, string kind)
    {
        if (data == null) return 0;

        return kind switch
        {
            "DWord" => int.TryParse(data, out int d) ? d : 0,
            "QWord" => long.TryParse(data, out long q) ? q : 0L,
            "Binary" => Convert.FromBase64String(data),
            _ => data
        };
    }
}
