using System;
using System.Threading.Tasks;
using System.Text.Json;
using WinVelo.Core.Models;
using WinVelo.Core.Safety;

namespace WinVelo.Core.Engine.Handlers;

public sealed class PowerShellActionHandler : IActionHandler
{
    public bool CanHandle(string actionType) =>
        actionType.Equals("PowerShell", StringComparison.OrdinalIgnoreCase) ||
        actionType.Equals("Script", StringComparison.OrdinalIgnoreCase);

    public async Task<ActionResult> ExecuteAsync(ActionItem action, string tweakId, int actionIndex, ExecutionContext context)
    {
        if (string.IsNullOrWhiteSpace(action.Script))
        {
            return new ActionResult { Success = false, Message = "Missing script content." };
        }

        string? blockedByServicingGuard = ServicingGuard.CheckAction(action);
        if (blockedByServicingGuard != null)
        {
            return new ActionResult { Success = false, Message = "Blocked: " + blockedByServicingGuard };
        }


        string targetId = $"powershell://{tweakId}/{actionIndex}";
        var undo = context.PendingScriptUndo;
        if (action.CaptureRegistryState)
        {
            if (string.IsNullOrWhiteSpace(action.Hive) || string.IsNullOrWhiteSpace(action.Key) || action.ValueName == null)
                return ActionResult.Fail("Registry capture target is missing.");
            bool existed = context.Registry.ValueExists(action.Hive, action.Key, action.ValueName);
            undo = new List<ActionItem> { new() { Type="Registry", Hive=action.Hive, Key=action.Key, ValueName=action.ValueName,
                Operation=existed ? "Set" : "DeleteValue", ValueKind=existed ? context.Registry.GetValueKind(action.Hive,action.Key,action.ValueName) : null,
                Data=existed ? JsonSerializer.SerializeToElement(context.Registry.GetValue(action.Hive,action.Key,action.ValueName)) : null } };
        }
        if (!context.IsDryRun && context.Shell.SupportsSystemQueries && !string.IsNullOrWhiteSpace(action.CaptureUndoScript))
        {
            var capture = await context.Shell.ExecutePowerShellAsync(action.CaptureUndoScript, 30, context.CancellationToken);
            if (!capture.Success) return ActionResult.Fail("Could not capture script baseline: " + capture.StdErr);
            try
            {
                var scripts = JsonSerializer.Deserialize<List<string>>(capture.StdOut);
                if (scripts == null || scripts.Count == 0) return ActionResult.Fail("Script baseline was empty.");
                undo = scripts.Select(script => new ActionItem { Type = "PowerShell", Operation = "Execute", Script = script, TimeoutSeconds = action.TimeoutSeconds ?? 60 }).ToList();
            }
            catch (JsonException) { return ActionResult.Fail("Script baseline was not valid JSON."); }
        }

        var journalRecord = new JournalActionRecord
        {
            TweakId = tweakId,
            ActionIndex = actionIndex,
            ActionType = "PowerShell",
            TargetIdentifier = targetId,
            ExistedBefore = false,
            PreviousData = null,
            UndoActions = undo,
            IsBaseline = true,
            AppliedAtUtc = DateTime.UtcNow
        };

        if (!context.IsDryRun && !context.IsUndoOperation)
        {
            context.Journal.RecordActionWriteAhead(journalRecord);
        }

        if (!context.IsDryRun)
        {
            try
            {
                int timeout = action.TimeoutSeconds.HasValue && action.TimeoutSeconds.Value > 0 ? action.TimeoutSeconds.Value : 60;
                PowerShellResult result = await context.Shell.ExecutePowerShellAsync(action.Script, timeout, context.IsUndoOperation ? CancellationToken.None : context.CancellationToken);
                if (!result.Success)
                {
                    string errorDetail = result.TimedOut
                        ? $"Script timed out after {timeout} seconds."
                        : !string.IsNullOrWhiteSpace(result.StdErr)
                            ? result.StdErr.Trim()
                            : $"Exited with code {result.ExitCode}";
                    return new ActionResult
                    {
                        Success = false,
                        Message = $"PowerShell execution failed: {errorDetail}",
                        JournalRecord = context.IsUndoOperation ? null : journalRecord
                    };
                }

                return new ActionResult
                {
                    Success = true,
                    Message = !string.IsNullOrWhiteSpace(result.StdOut) ? result.StdOut.Trim() : "PowerShell script completed successfully.",
                    JournalRecord = context.IsUndoOperation ? null : journalRecord
                };
            }
            catch (Exception ex)
            {
                return new ActionResult
                {
                    Success = false,
                    Message = $"PowerShell execution failed: {ex.Message}",
                    JournalRecord = context.IsUndoOperation ? null : journalRecord
                };
            }
        }

        return new ActionResult
        {
            Success = true,
            Message = "Dry run: PowerShell script simulated.",
            JournalRecord = journalRecord
        };
    }

    public async Task<ActionResult> UndoAsync(JournalActionRecord record, ExecutionContext context)
    {
        if (context.IsDryRun)
        {
            return new ActionResult { Success = true, Message = "Dry run: PowerShell undo simulated." };
        }

        var undoActions = record.UndoActions;
        if (undoActions == null && !string.IsNullOrWhiteSpace(record.PreviousData))
            undoActions = new List<ActionItem> { new() { Type = "PowerShell", Operation = "Execute", Script = record.PreviousData, TimeoutSeconds = 60 } };

        if (undoActions is { Count: > 0 })
        {
            try
            {
                foreach (var action in undoActions)
                {
                    if (action.Type is "Registry" or "Policy")
                    {
                        var restore = await new RegistryActionHandler().ExecuteAsync(action, record.TweakId, record.ActionIndex, context);
                        if (!restore.Success) return restore;
                        continue;
                    }
                    string? blocked = ServicingGuard.CheckAction(action);
                    if (blocked != null) return ActionResult.Fail("Blocked rollback: " + blocked);
                    if (string.IsNullOrWhiteSpace(action.Script)) return ActionResult.Fail("Rollback script is empty.");
                    PowerShellResult result = await context.Shell.ExecutePowerShellAsync(action.Script, action.TimeoutSeconds ?? 60);
                    if (!result.Success)
                    {
                        string errorDetail = result.TimedOut
                            ? "Undo script timed out."
                            : !string.IsNullOrWhiteSpace(result.StdErr)
                                ? result.StdErr.Trim()
                                : $"Exited with code {result.ExitCode}";
                        return new ActionResult { Success = false, Message = $"PowerShell undo failed: {errorDetail}" };
                    }

                }
                return ActionResult.Ok("PowerShell undo completed successfully.");
            }
            catch (Exception ex)
            {
                return new ActionResult { Success = false, Message = $"PowerShell undo failed: {ex.Message}" };
            }
        }

        return ActionResult.Fail("This script has no recorded rollback plan. Its effects have not been restored.");
    }

    public ActionPreview Preview(ActionItem action, ExecutionContext context)
    {
        string scriptSnippet = action.Script != null && action.Script.Length > 80
            ? action.Script[..77] + "..."
            : action.Script ?? string.Empty;

        return new ActionPreview
        {
            ActionType = "PowerShell",
            Target = "System PowerShell Runtime",
            Operation = action.Operation,
            CurrentState = "Unmodified",
            ProposedState = scriptSnippet
        };
    }
}
