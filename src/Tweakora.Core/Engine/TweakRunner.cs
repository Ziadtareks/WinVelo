using Tweakora.Core.Interfaces;
using Tweakora.Core.Models;

namespace Tweakora.Core.Engine;

public enum TweakExecutionStatus
{
    Applied,
    Restored,
    Skipped,
    Failed,
    FailedRolledBack,
    FailedRollbackIncomplete,
    Undone
}

public sealed class TweakExecutionResult
{
    public required string TweakId { get; init; }
    public required TweakExecutionStatus Status { get; init; }
    public string? Message { get; init; }

    public string DisplayStatus => Status switch
    {
        TweakExecutionStatus.Applied => "Applied",
        TweakExecutionStatus.Restored => "Restored",
        TweakExecutionStatus.Undone => "Restored",
        TweakExecutionStatus.Skipped => "Skipped",
        TweakExecutionStatus.Failed => "Failed",
        TweakExecutionStatus.FailedRolledBack => "Failed (rolled back)",
        TweakExecutionStatus.FailedRollbackIncomplete => "Failed (rollback incomplete)",
        _ => Status.ToString()
    };

    public bool IsFailure => Status is TweakExecutionStatus.Failed
                                   or TweakExecutionStatus.FailedRolledBack
                                   or TweakExecutionStatus.FailedRollbackIncomplete;
}

public sealed class BatchExecutionReport
{
    public int TotalRequested => Results.Count;
    public int AppliedCount => Results.Count(r => r.Status == TweakExecutionStatus.Applied);
    public int RestoredCount => Results.Count(r => r.Status is TweakExecutionStatus.Restored or TweakExecutionStatus.Undone);
    public int SkippedCount => Results.Count(r => r.Status == TweakExecutionStatus.Skipped);
    public int FailedCount => Results.Count(r => r.IsFailure);
    public int UndoneCount => RestoredCount;
    public bool AllSucceeded => Results.Count > 0 && FailedCount == 0 && SkippedCount == 0;
    public List<TweakExecutionResult> Results { get; } = new();
}

public sealed class TweakRunner
{
    private readonly IEnumerable<IActionHandler> _handlers;
    private readonly ExecutionContext _context;
    private readonly ISystemRestoreService _restoreService;
    private readonly StateDetector _detector;

    public TweakRunner(
        IEnumerable<IActionHandler> handlers,
        ExecutionContext context,
        ISystemRestoreService restoreService,
        StateDetector detector)
    {
        _handlers = handlers;
        _context = context;
        _restoreService = restoreService;
        _detector = detector;
    }

    public async Task<BatchExecutionReport> ApplyBatchAsync(
        IEnumerable<TweakDefinition> tweaks,
        Func<string, Task<bool>>? onRestorePointPromptAsync = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        BatchExecutionReport report = new();
        List<TweakDefinition> tweakList = tweaks.ToList();

        if (tweakList.Count == 0) return report;

        // Safety Step 1: System Restore Point Creation & Verification
        if (!_context.IsDryRun)
        {
            progress?.Report("Verifying System Protection & Restore Point...");
            bool canProceed = await EnsureSystemRestorePointAsync(onRestorePointPromptAsync);
            if (!canProceed)
            {
                foreach (var t in tweakList)
                {
                    report.Results.Add(new TweakExecutionResult
                    {
                        TweakId = t.Id,
                        Status = TweakExecutionStatus.Skipped,
                        Message = "Skipped by user because Restore Point could not be verified."
                    });
                }
                return report;
            }
        }

        // Safety Step 2: Start Rollback Journal Session
        RollbackJournalSession? session = null;
        if (!_context.IsDryRun)
        {
            session = _context.Journal.StartSession();
        }

        // Safety Step 3: Execute Tweaks Transactionally per Tweak
        foreach (TweakDefinition tweak in tweakList)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                report.Results.Add(new TweakExecutionResult
                {
                    TweakId = tweak.Id,
                    Status = TweakExecutionStatus.Skipped,
                    Message = "Batch cancelled by user."
                });
                continue;
            }

            // Check if already active
            bool isAlreadyActive = await _detector.IsTweakActiveAsync(tweak, useCache: false);
            if (isAlreadyActive)
            {
                progress?.Report($"Skipping {tweak.Id}: already active.");
                report.Results.Add(new TweakExecutionResult
                {
                    TweakId = tweak.Id,
                    Status = TweakExecutionStatus.Skipped,
                    Message = "Tweak is already active."
                });
                continue;
            }

            progress?.Report($"Applying {tweak.Id}...");
            List<JournalActionRecord> appliedActionsInThisTweak = new();
            bool tweakSuccess = true;
            string? failureReason = null;

            for (int i = 0; i < tweak.Apply.Count; i++)
            {
                ActionItem action = tweak.Apply[i];
                IActionHandler? handler = _handlers.FirstOrDefault(h => h.CanHandle(action.Type));

                if (handler == null)
                {
                    tweakSuccess = false;
                    failureReason = $"No handler found for action type: {action.Type}";
                    break;
                }

                try
                {
                    ActionResult result = await handler.ExecuteAsync(action, tweak.Id, i, _context);
                    if (!result.Success)
                    {
                        tweakSuccess = false;
                        failureReason = result.Message ?? "Action execution failed.";
                        break;
                    }

                    if (result.JournalRecord != null)
                    {
                        appliedActionsInThisTweak.Add(result.JournalRecord);
                    }
                }
                catch (Exception ex)
                {
                    tweakSuccess = false;
                    failureReason = ex.Message;
                    break;
                }
            }

            if (!tweakSuccess)
            {
                // Transaction rollback for this single tweak
                progress?.Report($"Rolling back partial changes for failed tweak: {tweak.Id}...");
                bool rollbackSucceeded = true;
                var rollbackFailedSteps = new List<string>();

                foreach (JournalActionRecord record in appliedActionsInThisTweak.AsEnumerable().Reverse())
                {
                    IActionHandler? handler = _handlers.FirstOrDefault(h => h.CanHandle(record.ActionType));
                    if (handler != null)
                    {
                        try
                        {
                            var undoRes = await handler.UndoAsync(record, _context);
                            if (!undoRes.Success)
                            {
                                rollbackSucceeded = false;
                                rollbackFailedSteps.Add(undoRes.Message ?? $"{record.ActionType}:{record.TargetIdentifier}");
                            }
                        }
                        catch (Exception ex)
                        {
                            rollbackSucceeded = false;
                            rollbackFailedSteps.Add(ex.Message);
                        }
                    }
                }

                if (appliedActionsInThisTweak.Count > 0)
                {
                    _context.Journal.MarkRecordsReverted(appliedActionsInThisTweak);
                }

                TweakExecutionStatus status;
                if (appliedActionsInThisTweak.Count == 0)
                {
                    status = TweakExecutionStatus.Failed;
                }
                else
                {
                    status = rollbackSucceeded
                        ? TweakExecutionStatus.FailedRolledBack
                        : TweakExecutionStatus.FailedRollbackIncomplete;
                }

                string finalMessage = failureReason ?? "Tweak execution failed.";
                if (appliedActionsInThisTweak.Count > 0)
                {
                    finalMessage += rollbackSucceeded
                        ? " — Changes were rolled back successfully."
                        : $" — Rollback incomplete: {string.Join("; ", rollbackFailedSteps)}";
                }

                report.Results.Add(new TweakExecutionResult
                {
                    TweakId = tweak.Id,
                    Status = status,
                    Message = finalMessage
                });
            }
            else
            {
                _detector.InvalidateCache(tweak.Id);
                report.Results.Add(new TweakExecutionResult
                {
                    TweakId = tweak.Id,
                    Status = TweakExecutionStatus.Applied
                });
            }
        }

        if (!_context.IsDryRun && session != null)
        {
            _context.Journal.CompleteSession();
        }

        return report;
    }

    public async Task<ActionResult> UndoTweakAsync(TweakDefinition tweak)
    {
        var executedSteps = new List<string>();
        var failedSteps = new List<string>();

        // 1. Check for active (non-reverted) journal records for this tweak
        List<RollbackJournalSession> sessions = _context.Journal.GetAllSessions();
        List<JournalActionRecord> activeRecordsForTweak = sessions
            .SelectMany(s => s.Records)
            .Where(r => r.TweakId == tweak.Id && !r.IsReverted)
            .OrderByDescending(r => r.AppliedAtUtc)
            .ThenByDescending(r => r.ActionIndex)
            .ToList();

        if (activeRecordsForTweak.Count > 0)
        {
            // A. Stateful actions: restore from the journal baseline, in reverse order
            // Exclude PowerShell/Script which don't store baseline data
            var statefulRecords = activeRecordsForTweak
                .Where(r => !IsStatelessType(r.ActionType))
                .ToList();

            foreach (JournalActionRecord record in statefulRecords)
            {
                IActionHandler? handler = _handlers.FirstOrDefault(h => h.CanHandle(record.ActionType));
                if (handler != null)
                {
                    try
                    {
                        var res = await handler.UndoAsync(record, _context);
                        if (res.Success)
                        {
                            executedSteps.Add($"Restored {record.ActionType} ({record.TargetIdentifier}) from journal");
                        }
                        else
                        {
                            failedSteps.Add($"Failed to restore {record.ActionType} ({record.TargetIdentifier}): {res.Message}");
                        }
                    }
                    catch (Exception ex)
                    {
                        failedSteps.Add($"Exception restoring {record.ActionType} ({record.TargetIdentifier}): {ex.Message}");
                    }
                }
            }

            // B. Stateless actions: run matching catalog undo actions of type PowerShell/Script
            // Do NOT run catalog registry/service undo items (the journal already restored their baseline!)
            for (int i = 0; i < tweak.Undo.Count; i++)
            {
                ActionItem action = tweak.Undo[i];
                if (IsStatelessType(action.Type))
                {
                    IActionHandler? handler = _handlers.FirstOrDefault(h => h.CanHandle(action.Type));
                    if (handler != null)
                    {
                        try
                        {
                            var res = await handler.ExecuteAsync(action, tweak.Id, i, _context);
                            if (res.Success)
                            {
                                executedSteps.Add($"Executed catalog undo script: {action.Type} (index {i})");
                            }
                            else
                            {
                                failedSteps.Add($"Catalog undo script failed: {action.Type} (index {i}): {res.Message}");
                            }
                        }
                        catch (Exception ex)
                        {
                            failedSteps.Add($"Catalog undo script exception: {action.Type} (index {i}): {ex.Message}");
                        }
                    }
                }
            }
        }
        else
        {
            // 2. Fallback to full catalog undo actions if journal has no active records
            for (int i = 0; i < tweak.Undo.Count; i++)
            {
                ActionItem action = tweak.Undo[i];
                IActionHandler? handler = _handlers.FirstOrDefault(h => h.CanHandle(action.Type));
                if (handler != null)
                {
                    try
                    {
                        var res = await handler.ExecuteAsync(action, tweak.Id, i, _context);
                        if (res.Success)
                        {
                            executedSteps.Add($"Executed catalog fallback undo: {action.Type} ({action.Operation})");
                        }
                        else
                        {
                            failedSteps.Add($"Catalog fallback undo failed: {action.Type} ({action.Operation}): {res.Message}");
                        }
                    }
                    catch (Exception ex)
                    {
                        failedSteps.Add($"Catalog fallback undo exception: {action.Type} ({action.Operation}): {ex.Message}");
                    }
                }
            }
        }

        _detector.InvalidateCache(tweak.Id);

        bool success = failedSteps.Count == 0;
        if (success && activeRecordsForTweak.Count > 0)
        {
            _context.Journal.MarkTweakRecordsReverted(tweak.Id);
        }

        string message = success
            ? (activeRecordsForTweak.Count > 0
                ? $"Restored state from journal ({executedSteps.Count} action(s))."
                : $"Applied catalog undo fallback ({executedSteps.Count} action(s)).")
            : $"Undo failed with {failedSteps.Count} error(s): {string.Join("; ", failedSteps)}";

        return new ActionResult
        {
            Success = success,
            Message = message,
            ExecutedSteps = executedSteps,
            FailedSteps = failedSteps
        };
    }

    private static bool IsStatelessType(string? type)
    {
        if (string.IsNullOrWhiteSpace(type)) return false;
        return string.Equals(type, "PowerShell", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(type, "Script", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<bool> EnsureSystemRestorePointAsync(Func<string, Task<bool>>? onRestorePointPromptAsync)
    {
        if (!_restoreService.IsSystemRestoreEnabled())
        {
            if (onRestorePointPromptAsync != null)
            {
                return await onRestorePointPromptAsync("System Restore is turned off on the system drive. Continue with Registry backup only, or Cancel?");
            }
            return true;
        }

        int countBefore = _restoreService.GetRestorePointCount();
        RestorePointResult createResult = _restoreService.CreateRestorePoint($"WinVelo Session {DateTime.Now:yyyy-MM-dd HH:mm}");

        int countAfter = _restoreService.GetRestorePointCount();

        // Verification: If count didn't increase OR result indicated failure/throttled
        if (!createResult.Success || countAfter <= countBefore)
        {
            string reason = createResult.WasThrottledOrSkipped
                ? "Windows restricted creation (24-hour frequency limit)."
                : createResult.ErrorMessage ?? "Windows did not register the new restore point.";

            if (onRestorePointPromptAsync != null)
            {
                return await onRestorePointPromptAsync($"A new System Restore point could not be created ({reason}). You still have Registry (.reg) backup and Rollback Journal. Do you want to continue, or cancel?");
            }
            return true;
        }

        return true;
    }
}
