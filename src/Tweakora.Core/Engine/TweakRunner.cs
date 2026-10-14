using Tweakora.Core.Interfaces;
using Tweakora.Core.Models;

namespace Tweakora.Core.Engine;

public enum TweakExecutionStatus
{
    Applied,
    Skipped,
    Failed,
    Undone
}

public sealed class TweakExecutionResult
{
    public required string TweakId { get; init; }
    public required TweakExecutionStatus Status { get; init; }
    public string? Message { get; init; }
}

public sealed class BatchExecutionReport
{
    public int TotalRequested => Results.Count;
    public int AppliedCount => Results.Count(r => r.Status == TweakExecutionStatus.Applied);
    public int SkippedCount => Results.Count(r => r.Status == TweakExecutionStatus.Skipped);
    public int FailedCount => Results.Count(r => r.Status == TweakExecutionStatus.Failed);
    public int UndoneCount => Results.Count(r => r.Status == TweakExecutionStatus.Undone);
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
                foreach (JournalActionRecord record in appliedActionsInThisTweak.AsEnumerable().Reverse())
                {
                    IActionHandler? handler = _handlers.FirstOrDefault(h => h.CanHandle(record.ActionType));
                    if (handler != null)
                    {
                        try { await handler.UndoAsync(record, _context); } catch { /* best effort rollback */ }
                    }
                }

                report.Results.Add(new TweakExecutionResult
                {
                    TweakId = tweak.Id,
                    Status = TweakExecutionStatus.Failed,
                    Message = failureReason
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
        // 1. Try Rollback using Journal (restores REAL previous system state)
        List<RollbackJournalSession> sessions = _context.Journal.GetAllSessions();
        List<JournalActionRecord> recordsForTweak = sessions
            .SelectMany(s => s.Records)
            .Where(r => r.TweakId == tweak.Id)
            .OrderByDescending(r => r.AppliedAtUtc)
            .ToList();

        if (recordsForTweak.Count > 0)
        {
            foreach (JournalActionRecord record in recordsForTweak)
            {
                IActionHandler? handler = _handlers.FirstOrDefault(h => h.CanHandle(record.ActionType));
                if (handler != null)
                {
                    await handler.UndoAsync(record, _context);
                }
            }
            _detector.InvalidateCache(tweak.Id);
            return new ActionResult { Success = true, Message = "Restored original values from Rollback Journal." };
        }

        // 2. Fallback to static undo actions in JSON catalog if journal has no records
        for (int i = 0; i < tweak.Undo.Count; i++)
        {
            ActionItem action = tweak.Undo[i];
            IActionHandler? handler = _handlers.FirstOrDefault(h => h.CanHandle(action.Type));
            if (handler != null)
            {
                await handler.ExecuteAsync(action, tweak.Id, i, _context);
            }
        }

        _detector.InvalidateCache(tweak.Id);
        return new ActionResult { Success = true, Message = "Applied static undo fallback from catalog." };
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
