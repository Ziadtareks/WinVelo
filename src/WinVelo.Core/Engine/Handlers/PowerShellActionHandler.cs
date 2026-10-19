using System;
using System.Threading.Tasks;
using WinVelo.Core.Models;

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

        string targetId = $"powershell://{tweakId}/{actionIndex}";

        var journalRecord = new JournalActionRecord
        {
            TweakId = tweakId,
            ActionIndex = actionIndex,
            ActionType = "PowerShell",
            TargetIdentifier = targetId,
            ExistedBefore = false,
            PreviousData = null,
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
                PowerShellResult result = await context.Shell.ExecutePowerShellAsync(action.Script, timeout);
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

        if (!string.IsNullOrWhiteSpace(record.PreviousData))
        {
            try
            {
                PowerShellResult result = await context.Shell.ExecutePowerShellAsync(record.PreviousData, 60);
                if (!result.Success)
                {
                    string errorDetail = result.TimedOut
                        ? "Undo script timed out."
                        : !string.IsNullOrWhiteSpace(result.StdErr)
                            ? result.StdErr.Trim()
                            : $"Exited with code {result.ExitCode}";
                    return new ActionResult { Success = false, Message = $"PowerShell undo failed: {errorDetail}" };
                }

                return new ActionResult
                {
                    Success = true,
                    Message = !string.IsNullOrWhiteSpace(result.StdOut) ? result.StdOut.Trim() : "PowerShell undo completed successfully."
                };
            }
            catch (Exception ex)
            {
                return new ActionResult { Success = false, Message = $"PowerShell undo failed: {ex.Message}" };
            }
        }

        return new ActionResult { Success = true, Message = "No rollback script specified for PowerShell action." };
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
