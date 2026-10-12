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
        bool exists = context.Tasks.TaskExists(path, action.TaskName);
        if (!exists)
        {
            return Task.FromResult(new ActionResult { Success = false, Message = $"Task '{path}\\{action.TaskName}' not found." });
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

        if (!context.IsDryRun)
        {
            context.Journal.RecordActionWriteAhead(journalRecord);

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

        return Task.FromResult(new ActionResult
        {
            Success = true,
            JournalRecord = journalRecord
        });
    }

    public Task<ActionResult> UndoAsync(JournalActionRecord record, ExecutionContext context)
    {
        string[] parts = record.TargetIdentifier.Split('\\');
        string taskName = parts[^1];
        string taskPath = string.Join('\\', parts[..^1]);
        if (string.IsNullOrEmpty(taskPath)) taskPath = "\\";

        bool wasEnabled = bool.TryParse(record.PreviousData, out bool b) && b;

        if (wasEnabled)
        {
            context.Tasks.EnableTask(taskPath, taskName);
        }
        else
        {
            context.Tasks.DisableTask(taskPath, taskName);
        }

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
