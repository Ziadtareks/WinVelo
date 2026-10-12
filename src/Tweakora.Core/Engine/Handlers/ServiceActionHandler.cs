using Tweakora.Core.Models;

namespace Tweakora.Core.Engine.Handlers;

public sealed class ServiceActionHandler : IActionHandler
{
    public bool CanHandle(string actionType) =>
        actionType.Equals("Service", StringComparison.OrdinalIgnoreCase);

    public Task<ActionResult> ExecuteAsync(ActionItem action, string tweakId, int actionIndex, ExecutionContext context)
    {
        if (string.IsNullOrWhiteSpace(action.ServiceName))
        {
            return Task.FromResult(new ActionResult { Success = false, Message = "Missing ServiceName." });
        }

        bool exists = context.Services.ServiceExists(action.ServiceName);
        if (!exists)
        {
            return Task.FromResult(new ActionResult { Success = false, Message = $"Service '{action.ServiceName}' not found." });
        }

        string previousStartup = context.Services.GetStartupType(action.ServiceName);
        string previousState = context.Services.GetState(action.ServiceName);

        var existingBaseline = context.Journal.FindExistingBaselineRecord(tweakId, action.ServiceName);
        bool isBaseline = existingBaseline == null;

        var journalRecord = new JournalActionRecord
        {
            TweakId = tweakId,
            ActionIndex = actionIndex,
            ActionType = "Service",
            TargetIdentifier = action.ServiceName,
            ExistedBefore = true,
            PreviousValueKind = "ServiceConfig",
            PreviousData = isBaseline ? $"{previousStartup}|{previousState}" : existingBaseline!.PreviousData,
            IsBaseline = isBaseline,
            AppliedAtUtc = DateTime.UtcNow
        };

        if (!context.IsDryRun)
        {
            context.Journal.RecordActionWriteAhead(journalRecord);

            switch (action.Operation)
            {
                case "SetStartup":
                    if (!string.IsNullOrEmpty(action.StartupType))
                    {
                        context.Services.SetStartupType(action.ServiceName, action.StartupType);
                    }
                    break;

                case "Start":
                    context.Services.StartService(action.ServiceName);
                    break;

                case "Stop":
                    context.Services.StopService(action.ServiceName);
                    break;

                default:
                    return Task.FromResult(new ActionResult { Success = false, Message = $"Unknown service operation: {action.Operation}" });
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
        if (!context.Services.ServiceExists(record.TargetIdentifier))
        {
            return Task.FromResult(new ActionResult { Success = false, Message = $"Service '{record.TargetIdentifier}' not found during rollback." });
        }

        string[] parts = (record.PreviousData ?? "Manual|Stopped").Split('|');
        string startup = parts[0];
        string state = parts.Length > 1 ? parts[1] : "Stopped";

        context.Services.SetStartupType(record.TargetIdentifier, startup);

        if (state == "Running")
        {
            context.Services.StartService(record.TargetIdentifier);
        }
        else if (state == "Stopped")
        {
            context.Services.StopService(record.TargetIdentifier);
        }

        return Task.FromResult(new ActionResult { Success = true });
    }

    public ActionPreview Preview(ActionItem action, ExecutionContext context)
    {
        string currentStartup = context.Services.ServiceExists(action.ServiceName ?? "")
            ? context.Services.GetStartupType(action.ServiceName!)
            : "(Not Installed)";

        return new ActionPreview
        {
            ActionType = "Service",
            Target = action.ServiceName ?? "",
            Operation = action.Operation,
            CurrentState = currentStartup,
            ProposedState = action.StartupType ?? action.Operation
        };
    }
}
