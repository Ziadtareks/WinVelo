using System;
using System.Threading.Tasks;
using Tweakora.Core.Models;

namespace Tweakora.Core.Engine.Handlers;

public sealed class TaskActionHandler : IActionHandler
{
    public bool CanHandle(string actionType) =>
        actionType.Equals("ScheduledTask", StringComparison.OrdinalIgnoreCase) ||
        actionType.Equals("Task", StringComparison.OrdinalIgnoreCase);

    public Task<ActionResult> ExecuteAsync(ActionItem action, string tweakId, int actionIndex, ExecutionContext context)
    {
        if (string.IsNullOrWhiteSpace(action.TaskName))
        {
            return Task.FromResult(new ActionResult { Success = false, Message = "Missing TaskName." });
        }

        string path = action.TaskPath ?? "\\";

        // Rule #3 (Upstream WinUtil): If a Scheduled Task does not exist on the current Windows edition/build,
        // SKIP it cleanly as Succeeded (Not present on OS) — NEVER fail or return an error.
        bool exists = context.Tasks.TaskExists(path, action.TaskName);
        if (!exists)
        {
            return Task.FromResult(new ActionResult
            {
                Success = true,
                Message = $"Task '{path}\\{action.TaskName}' not present on this Windows edition (skipped)."
            });
        }

        bool wasEnabled = context.Tasks.IsTaskEnabled(path, action.TaskName);
        string targetId = $"{path}\\{action.TaskName}";

        var existingBaseline = context.Journal.FindExistingBaselineRecord(tweakId, targetId);
        bool isBaseline = existingBaseline == null;

        var journalRecord = new JournalActionRecord
        {
            TweakId = tweakId,
            ActionIndex = actionIndex,
            ActionType = "ScheduledTask",
            TargetIdentifier = targetId,
            ExistedBefore = true,
            PreviousValueKind = "TaskState",
            PreviousData = isBaseline ? wasEnabled.ToString() : existingBaseline!.PreviousData,
            IsBaseline = isBaseline,
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
                switch (action.Operation)
                {
                    case "Enable":
                        context.Tasks.EnableTask(path, action.TaskName);
                        break;
                    case "Disable":
                        context.Tasks.DisableTask(path, action.TaskName);
                        break;
                    case "Delete":
                        context.Tasks.DeleteTask(path, action.TaskName);
                        break;
                    default:
                        return Task.FromResult(new ActionResult { Success = false, Message = $"Unknown task operation: {action.Operation}" });
                }
            }
            catch (Exception ex)
            {
                return Task.FromResult(new ActionResult
                {
                    Success = true,
                    Message = $"[WARN] Task '{path}\\{action.TaskName}' action failed: {ex.Message} (skipped)"
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
        string taskName = parts[^1];
        string taskPath = string.Join('\\', parts[..^1]);
        if (string.IsNullOrEmpty(taskPath)) taskPath = "\\";

        if (!context.Tasks.TaskExists(taskPath, taskName))
        {
            return Task.FromResult(new ActionResult
            {
                Success = true,
                Message = $"Task '{taskPath}\\{taskName}' not present during rollback (skipped)."
            });
        }

        bool wasEnabled = bool.TryParse(record.PreviousData, out bool b) && b;

        try
        {
            if (wasEnabled)
            {
                context.Tasks.EnableTask(taskPath, taskName);
            }
            else
            {
                context.Tasks.DisableTask(taskPath, taskName);
            }
        }
        catch { }

        return Task.FromResult(new ActionResult { Success = true });
    }

    public ActionPreview Preview(ActionItem action, ExecutionContext context)
    {
        string path = action.TaskPath ?? "\\";
        bool exists = context.Tasks.TaskExists(path, action.TaskName ?? "");
        string current = exists ? (context.Tasks.IsTaskEnabled(path, action.TaskName!) ? "Enabled" : "Disabled") : "(Not Found)";

        return new ActionPreview
        {
            ActionType = "ScheduledTask",
            Target = $"{path}\\{action.TaskName}",
            Operation = action.Operation,
            CurrentState = current,
            ProposedState = action.Operation
        };
    }
}
