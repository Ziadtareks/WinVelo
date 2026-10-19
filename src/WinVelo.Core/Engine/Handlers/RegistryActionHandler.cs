using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using WinVelo.Core.Models;
using WinVelo.Core.Safety;
using WinVelo.Core.Utilities;

namespace WinVelo.Core.Engine.Handlers;

public sealed class RegistryActionHandler : IActionHandler, IStateCaptureHandler
{
    public JournalActionRecord? CaptureCurrentState(ActionItem action, string tweakId, int actionIndex, ExecutionContext context)
    {
        if (string.IsNullOrWhiteSpace(action.Hive) || string.IsNullOrWhiteSpace(action.Key)) return null;

        var (normHive, normKey) = RegistryPathHelper.Normalize(action.Hive, action.Key);
        string valName = action.ValueName ?? string.Empty;
        bool keyExisted = context.Registry.KeyExists(normHive, normKey);
        bool valueExisted = keyExisted && context.Registry.ValueExists(normHive, normKey, valName);

        return new JournalActionRecord
        {
            TweakId = tweakId,
            ActionIndex = actionIndex,
            ActionType = action.Type,
            TargetIdentifier = $"{normHive}\\{normKey}\\{(string.IsNullOrEmpty(valName) ? "@" : valName)}",
            ExistedBefore = valueExisted,
            PreviousValueKind = valueExisted ? context.Registry.GetValueKind(normHive, normKey, valName) : null,
            PreviousData = valueExisted ? SerializeData(context.Registry.GetValue(normHive, normKey, valName)) : null,
            KeyCreated = false,
            IsBaseline = true,
            AppliedAtUtc = DateTime.UtcNow
        };
    }

    public bool CanHandle(string actionType) =>
        actionType.Equals("Registry", StringComparison.OrdinalIgnoreCase) ||
        actionType.Equals("Policy", StringComparison.OrdinalIgnoreCase);

    public Task<ActionResult> ExecuteAsync(ActionItem action, string tweakId, int actionIndex, ExecutionContext context)
    {
        string? blockedByServicingGuard = ServicingGuard.CheckAction(action);
        if (blockedByServicingGuard != null)
        {
            return Task.FromResult(new ActionResult { Success = false, Message = "Blocked: " + blockedByServicingGuard });
        }

        if (string.IsNullOrWhiteSpace(action.Hive) || string.IsNullOrWhiteSpace(action.Key))
        {
            return Task.FromResult(new ActionResult { Success = false, Message = "Missing Hive or Key." });
        }

        var (normHive, normKey) = RegistryPathHelper.Normalize(action.Hive, action.Key);
        string valName = action.ValueName ?? string.Empty;
        string targetId = $"{normHive}\\{normKey}\\{(string.IsNullOrEmpty(valName) ? "@" : valName)}";

        // Read current state before touching
        bool keyExisted = context.Registry.KeyExists(normHive, normKey);
        // An empty value name is the key's default value ("@"); it is tracked like any other value.
        bool valueExisted = keyExisted && context.Registry.ValueExists(normHive, normKey, valName);
        object? previousData = valueExisted ? context.Registry.GetValue(normHive, normKey, valName) : null;
        string? previousKind = valueExisted ? context.Registry.GetValueKind(normHive, normKey, valName) : null;

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

        // Write-Ahead Journaling: MUST flush before applying! Skip journaling on undo operations
        if (!context.IsDryRun && !context.IsUndoOperation)
        {
            context.Journal.RecordActionWriteAhead(journalRecord);
        }

        if (!context.IsDryRun)
        {
            // Automatic .reg backup if not already dry run
            if (!string.IsNullOrEmpty(context.BackupDirectory) && keyExisted)
            {
                try
                {
                    context.Registry.ExportRegBackup(normHive, normKey, context.BackupDirectory);
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
                        if (dataToSet is string s && string.Equals(s, "<RemoveEntry>", StringComparison.OrdinalIgnoreCase))
                        {
                            if (keyExisted && valueExisted)
                            {
                                context.Registry.DeleteValue(normHive, normKey, valName);
                            }
                        }
                        else
                        {
                            if (!keyExisted) context.Registry.CreateKey(normHive, normKey);
                            context.Registry.SetValue(normHive, normKey, valName, dataToSet, action.ValueKind ?? "DWord");
                        }
                        break;

                    case "DeleteValue":
                        if (keyExisted && valueExisted)
                        {
                            context.Registry.DeleteValue(normHive, normKey, valName);
                        }
                        break;

                    case "DeleteKey":
                        if (keyExisted)
                        {
                            context.Registry.DeleteKey(normHive, normKey);
                        }
                        break;

                    default:
                        return Task.FromResult(new ActionResult { Success = false, Message = $"Unknown registry operation: {action.Operation}" });
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
            {
                // Reporting this as applied would be false: nothing was written. The runner rolls back the earlier
                // steps of this tweak and shows it as failed.
                return Task.FromResult(new ActionResult
                {
                    Success = false,
                    Message = $"Windows blocked the change to {normHive}\\{normKey} (Tamper Protection or TrustedInstaller permissions): {ex.Message}"
                });
            }
            catch (Exception ex)
            {
                return Task.FromResult(new ActionResult
                {
                    Success = false,
                    Message = $"Registry error on {normHive}\\{normKey}: {ex.Message}"
                });
            }
        }

        return Task.FromResult(new ActionResult
        {
            Success = true,
            JournalRecord = context.IsUndoOperation ? null : journalRecord
        });
    }

    public Task<ActionResult> UndoAsync(JournalActionRecord record, ExecutionContext context)
    {
        string[] parts = record.TargetIdentifier.Split('\\');
        if (parts.Length < 2)
        {
            return Task.FromResult(new ActionResult { Success = false, Message = "Invalid target identifier." });
        }

        string rawHive = parts[0];
        string valueName = parts[^1];
        string rawKey = string.Join('\\', parts[1..^1]);

        var (hive, key) = RegistryPathHelper.Normalize(rawHive, rawKey);
        if (valueName == "@") valueName = string.Empty;

        try
        {
            if (!record.ExistedBefore || string.Equals(record.PreviousData, "<RemoveEntry>", StringComparison.OrdinalIgnoreCase))
            {
                // Value did not exist originally -> delete it
                if (context.Registry.KeyExists(hive, key) && context.Registry.ValueExists(hive, key, valueName))
                {
                    context.Registry.DeleteValue(hive, key, valueName);
                }

                // Remove the key only if WinVelo created it AND nothing else lives in it now. DeleteKey removes the
                // whole tree, so a key that meanwhile holds values of other tweaks, policies or programs must stay.
                if (record.KeyCreated && context.Registry.KeyExists(hive, key) &&
                    !context.Registry.GetValueNames(hive, key).Any() &&
                    !context.Registry.GetSubKeyNames(hive, key).Any())
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
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            // Not restored: say so, and keep the journal record so the undo can be retried.
            return Task.FromResult(new ActionResult
            {
                Success = false,
                Message = $"Windows blocked restoring {hive}\\{key} (Tamper Protection or TrustedInstaller permissions): {ex.Message}"
            });
        }
        catch (Exception ex)
        {
            return Task.FromResult(new ActionResult { Success = false, Message = $"Registry undo failed for {hive}\\{key}: {ex.Message}" });
        }

        return Task.FromResult(new ActionResult { Success = true });
    }

    public ActionPreview Preview(ActionItem action, ExecutionContext context)
    {
        var (hive, key) = RegistryPathHelper.Normalize(action.Hive ?? "", action.Key ?? "");
        string valName = action.ValueName ?? string.Empty;
        string targetId = $"{hive}\\{key}\\{(string.IsNullOrEmpty(valName) ? "@" : valName)}";

        bool valueExisted = context.Registry.ValueExists(hive, key, valName);
        object? current = valueExisted ? context.Registry.GetValue(hive, key, valName) : null;

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
        // REG_MULTI_SZ: ToString() would store "System.String[]" and lose the original value.
        if (data is string[] lines) return JsonSerializer.Serialize(lines);
        return data.ToString();
    }

    private static object ParseData(JsonElement? element, string kind)
    {
        if (!element.HasValue) return 0;
        JsonElement val = element.Value;

        if (val.ValueKind == JsonValueKind.String &&
            string.Equals(val.GetString(), "<RemoveEntry>", StringComparison.OrdinalIgnoreCase))
        {
            return "<RemoveEntry>";
        }

        return (kind ?? "DWord") switch
        {
            "DWord" => RegistryPathHelper.ParseDWord(val.ValueKind == JsonValueKind.Number ? val.GetInt64() : val.GetString()),
            "QWord" => RegistryPathHelper.ParseQWord(val.ValueKind == JsonValueKind.Number ? val.GetInt64() : val.GetString()),
            "Binary" => val.ValueKind == JsonValueKind.String ? (val.GetString()?.Length > 0 ? Convert.FromBase64String(val.GetString()!) : Array.Empty<byte>()) : Array.Empty<byte>(),
            _ => val.GetString() ?? string.Empty
        };
    }

    private static object DeserializeData(string? data, string kind)
    {
        if (data == null) return 0;

        return (kind ?? "DWord") switch
        {
            "DWord" => RegistryPathHelper.ParseDWord(data),
            "QWord" => RegistryPathHelper.ParseQWord(data),
            "Binary" or "None" => !string.IsNullOrEmpty(data) ? Convert.FromBase64String(data) : Array.Empty<byte>(),
            "MultiString" => DeserializeMultiString(data),
            _ => data
        };
    }

    private static string[] DeserializeMultiString(string data)
    {
        if (data.StartsWith('['))
        {
            try
            {
                return JsonSerializer.Deserialize<string[]>(data) ?? Array.Empty<string>();
            }
            catch (JsonException)
            {
                // fall through: an older journal stored the text as is
            }
        }
        return new[] { data };
    }
}
