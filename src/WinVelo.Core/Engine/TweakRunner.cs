using WinVelo.Core.Interfaces;
using WinVelo.Core.Models;

namespace WinVelo.Core.Engine;

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
    private readonly SemaphoreSlim _operationGate;
    private readonly Dictionary<string, TweakDefinition> _knownTweaks = new(StringComparer.OrdinalIgnoreCase);

    public async Task<BatchExecutionReport> ApplyBatchAsync(IEnumerable<TweakDefinition> tweaks,
        Func<string, Task<bool>>? onRestorePointPromptAsync = null, IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken);
        try { return await ApplyBatchCoreAsync(tweaks, onRestorePointPromptAsync, progress, cancellationToken); }
        finally { _context.PendingScriptUndo = null; _operationGate.Release(); }
    }

    public async Task<ActionResult> UndoTweakAsync(TweakDefinition tweak)
    {
        await _operationGate.WaitAsync();
        try { return await UndoTweakCoreAsync(tweak); }
        finally { _operationGate.Release(); }
    }

    public async Task<ActionResult> RollbackSessionAsync(RollbackJournalSession session)
    {
        await _operationGate.WaitAsync();
        try { return await RollbackSessionCoreAsync(session); }
        finally { _operationGate.Release(); }
    }

    public TweakRunner(
        IEnumerable<IActionHandler> handlers,
        ExecutionContext context,
        ISystemRestoreService restoreService,
        StateDetector detector)
    {
        _handlers = handlers;
        _context = context;
        _operationGate = context.Journal.OperationGate;
        _restoreService = restoreService;
        _detector = detector;
    }

    private async Task<BatchExecutionReport> ApplyBatchCoreAsync(
        IEnumerable<TweakDefinition> tweaks,
        Func<string, Task<bool>>? onRestorePointPromptAsync = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        BatchExecutionReport report = new();
        _context.CancellationToken = cancellationToken;
        List<TweakDefinition> tweakList = tweaks.ToList();

        if (tweakList.Count == 0) return report;
        foreach (var definition in tweakList) _knownTweaks[definition.Id] = definition;
        var conflicts = TweakPlanValidator.FindConflicts(tweakList);
        if (conflicts.Count > 0)
        {
            foreach (var tweak in tweakList) report.Results.Add(new TweakExecutionResult
            { TweakId = tweak.Id, Status = TweakExecutionStatus.Failed, Message = string.Join("; ", conflicts) });
            return report;
        }

        var dependencyPlan = TweakPlanValidator.OrderDependencies(tweakList);
        var dependencyErrors = dependencyPlan.Errors;
        if (tweakList.Any(t => t.DependsOn?.Count > 0))
        {
            var definitions = WinVelo.Core.Catalog.CatalogLoader.LoadTweaks(WinVelo.Core.Catalog.CatalogSource.Default).Items;
            foreach (var tweak in tweakList)
                foreach (string dependency in tweak.DependsOn ?? [])
                {
                    if (tweakList.Any(t => t.Id.Equals(dependency, StringComparison.OrdinalIgnoreCase))) continue;
                    var existing = definitions.FirstOrDefault(t => t.Id.Equals(dependency, StringComparison.OrdinalIgnoreCase));
                    if (existing == null || !await _detector.IsTweakActiveAsync(existing, false)) dependencyErrors.Add($"Missing active dependency: {tweak.Id} requires {dependency}.");
                }
        }
        if (dependencyErrors.Count > 0)
        {
            foreach (var tweak in tweakList) report.Results.Add(new() { TweakId = tweak.Id, Status = TweakExecutionStatus.Failed, Message = string.Join("; ", dependencyErrors) });
            return report;
        }
        tweakList = dependencyPlan.Ordered;

        // Safety Step 1: System Restore Point Creation & Verification
        if (!_context.IsDryRun)
        {
            progress?.Report("Verifying System Protection & Restore Point...");
            bool canProceed = await EnsureSystemRestorePointAsync(onRestorePointPromptAsync, progress);
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

            if ((tweak.DependsOn ?? []).Any(id => report.Results.Any(r => r.TweakId.Equals(id, StringComparison.OrdinalIgnoreCase) && r.IsFailure)))
            {
                report.Results.Add(new() { TweakId = tweak.Id, Status = TweakExecutionStatus.Skipped, Message = "A required dependency failed." });
                continue;
            }
            // Not available on this Windows build or edition: skip instead of writing values Windows ignores.
            var compatibilityIssue = WindowsCompatibility.Check(tweak);
            if (compatibilityIssue != null)
            {
                progress?.Report($"Skipping {tweak.Id}: {compatibilityIssue.Describe()}");
                report.Results.Add(new TweakExecutionResult
                {
                    TweakId = tweak.Id,
                    Status = TweakExecutionStatus.Skipped,
                    Message = "Not available on this Windows version. " + compatibilityIssue.Describe()
                });
                continue;
            }

            // Check if already active
            bool isAlreadyActive = await _detector.IsTweakActiveAsync(tweak, useCache: false);
            if (isAlreadyActive)
            {
                // Nothing is changed, but the current state is journaled. Undo then puts back exactly this state
                // (often a value another tweak set) instead of running the catalog undo, which could switch off
                // that other tweak's setting.
                if (!_context.IsDryRun)
                {
                    RecordAdoptedState(tweak);
                }

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
                progress?.Report($"Applying {tweak.Name}: action {i + 1}/{tweak.Apply.Count} ({action.Type})...");
                IActionHandler? handler = _handlers.FirstOrDefault(h => h.CanHandle(action.Type));

                if (handler == null)
                {
                    tweakSuccess = false;
                    failureReason = $"No handler found for action type: {action.Type}";
                    break;
                }

                _context.PendingScriptUndo = IsStatelessType(action.Type) ? ScriptUndoFor(tweak, i) : null;
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ActionResult result = await handler.ExecuteAsync(action, tweak.Id, i, _context);
                    if (result.JournalRecord != null) appliedActionsInThisTweak.Add(result.JournalRecord);
                    if (!result.Success)
                    {
                        tweakSuccess = false;
                        failureReason = result.Message ?? "Action execution failed.";
                        break;
                    }

                }
                catch (Exception ex)
                {
                    tweakSuccess = false;
                    failureReason = ex.Message;
                    break;
                }
            }

            if (tweakSuccess && !_context.IsDryRun && _context.Shell.SupportsSystemQueries && !string.IsNullOrWhiteSpace(tweak.VerifyScript))
            {
                try
                {
                    var verification = await _context.Shell.ExecutePowerShellAsync(tweak.VerifyScript, 60, cancellationToken);
                    if (!verification.Success || !bool.TryParse(verification.StdOut.Trim(), out bool verified) || !verified)
                    { tweakSuccess = false; failureReason = "Commands completed but verification did not confirm the requested state."; }
                }
                catch (Exception ex) { tweakSuccess = false; failureReason = "Verification failed: " + ex.Message; }
            }
            if (!tweakSuccess)
            {
                // Transaction rollback for this single tweak
                progress?.Report($"Rolling back partial changes for failed tweak: {tweak.Id}...");
                // Include write-ahead records even if a handler failed after partially mutating its target.
                if (session != null)
                {
                    foreach (var record in session.Records.Where(r => !r.IsReverted && r.TweakId == tweak.Id))
                        if (!appliedActionsInThisTweak.Contains(record)) appliedActionsInThisTweak.Add(record);
                }
                var rollback = await RestoreRecordsAsync(appliedActionsInThisTweak);
                bool rollbackSucceeded = rollback.Success;
                var rollbackFailedSteps = rollback.FailedSteps;

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
                if (!tweak.Apply.Any(a => IsStatelessType(a.Type)) && tweak.Undo.Any(a => IsStatelessType(a.Type)) && !_context.IsDryRun)
                {
                    _context.Journal.RecordActionWriteAhead(new JournalActionRecord
                    {
                        TweakId = tweak.Id,
                        ActionIndex = tweak.Apply.Count,
                        ActionType = "PowerShell",
                        TargetIdentifier = $"powershell://{tweak.Id}/refresh",
                        UndoActions = tweak.Undo.Where(a => IsStatelessType(a.Type)).ToList()
                    });
                }
                _detector.InvalidateCache(tweak.Id);
                report.Results.Add(new TweakExecutionResult
                {
                    TweakId = tweak.Id,
                    Status = TweakExecutionStatus.Applied
                });
            }
        }

        if (!_context.IsDryRun && session != null && !report.Results.Any(r => r.Status == TweakExecutionStatus.FailedRollbackIncomplete))
        {
            _context.Journal.CompleteSession();
        }

        return report;
    }

    private async Task<ActionResult> UndoTweakCoreAsync(TweakDefinition tweak)
    {
        _knownTweaks[tweak.Id] = tweak;
        var records = _context.Journal.GetAllSessions().SelectMany(s => s.Records)
            .Where(r => !r.IsReverted && string.Equals(r.TweakId, tweak.Id, StringComparison.OrdinalIgnoreCase)).ToList();
        if (records.Count > 0) return await RestoreRecordsAsync(records);

        var executed = new List<string>();
        var failed = new List<string>();
        bool previous = _context.IsUndoOperation;
        _context.IsUndoOperation = true;
        try
        {
            for (int i = 0; i < tweak.Undo.Count; i++)
            {
                var action = tweak.Undo[i];
                var handler = _handlers.FirstOrDefault(h => h.CanHandle(action.Type));
                if (handler == null) { failed.Add("No undo handler: " + action.Type); continue; }
                try
                {
                    var result = await handler.ExecuteAsync(action, tweak.Id, i, _context);
                    if (result.Success) executed.Add("Executed catalog fallback undo: " + action.Type);
                    else failed.Add(result.Message ?? "Catalog undo failed.");
                }
                catch (Exception ex) { failed.Add(ex.Message); }
            }
        }
        finally { _context.IsUndoOperation = previous; }
        _detector.InvalidateCache(tweak.Id);
        return new ActionResult
        {
            Success = failed.Count == 0,
            Message = failed.Count == 0
            ? $"Applied catalog undo fallback ({executed.Count} action(s))."
            : string.Join("; ", failed),
            ExecutedSteps = executed,
            FailedSteps = failed
        };
    }

    private async Task<ActionResult> RollbackSessionCoreAsync(RollbackJournalSession session)
    {
        var result = await RestoreRecordsAsync(session.Records.Where(r => !r.IsReverted).ToList());
        // A failed recovery stays unfinished and its active records remain retryable.
        if (result.Success) { session.IsCompleted = true; _context.Journal.CompleteSession(session.SessionId); }
        return result;
    }

    private async Task<ActionResult> RestoreRecordsAsync(IReadOnlyList<JournalActionRecord> records)
    {
        var executed = new List<string>();
        var failed = new List<string>();
        bool previous = _context.IsUndoOperation;
        _context.IsUndoOperation = true;
        try
        {
            // Restore registry/service baselines before refresh scripts (SPI reads those restored values).
            var groups = records.OrderByDescending(r => r.AppliedAtUtc).ThenByDescending(r => r.ActionIndex)
                .GroupBy(r => r.TweakId, StringComparer.OrdinalIgnoreCase);
            foreach (var group in groups)
                foreach (var record in group.OrderBy(r => IsStatelessType(r.ActionType) ? 1 : 0))
                {
                    try
                    {
                        if (!IsStatelessType(record.ActionType) && HandOverToNewerTweak(record))
                        {
                            _context.Journal.MarkRecordsReverted(new[] { record });
                            executed.Add("Preserved another active tweak: " + record.TargetIdentifier);
                            continue;
                        }
                        if (IsStatelessType(record.ActionType) && record.UndoActions == null)
                        {
                            if (!_knownTweaks.TryGetValue(record.TweakId, out var definition))
                            {
                                definition = WinVelo.Core.Catalog.CatalogLoader.LoadTweaks(WinVelo.Core.Catalog.CatalogSource.Default)
                                    .Items.FirstOrDefault(t => string.Equals(t.Id, record.TweakId, StringComparison.OrdinalIgnoreCase));
                            }
                            if (definition != null) record.UndoActions = ScriptUndoFor(definition, record.ActionIndex);
                        }
                        var handler = _handlers.FirstOrDefault(h => h.CanHandle(record.ActionType));
                        var result = handler == null ? ActionResult.Fail("No undo handler: " + record.ActionType)
                            : await handler.UndoAsync(record, _context);
                        if (result.Success)
                        {
                            record.IsReverted = true;
                            record.RevertedAtUtc = DateTime.UtcNow;
                            _context.Journal.MarkRecordsReverted(new[] { record });
                            executed.Add(IsStatelessType(record.ActionType) ? "Executed catalog undo script: " + record.ActionType + " " + record.TargetIdentifier : "Restored " + record.ActionType + " from journal: " + record.TargetIdentifier);
                        }
                        else failed.Add(record.TargetIdentifier + ": " + result.Message);
                    }
                    catch (Exception ex) { failed.Add(record.TargetIdentifier + ": " + ex.Message); }
                    finally { _detector.InvalidateCache(record.TweakId); }
                }
        }
        finally { _context.IsUndoOperation = previous; }
        return new ActionResult
        {
            Success = failed.Count == 0,
            Message = failed.Count == 0
            ? $"Restored state from journal ({executed.Count} action(s))."
            : "Rollback incomplete: " + string.Join("; ", failed),
            ExecutedSteps = executed,
            FailedSteps = failed
        };
    }

    private static List<ActionItem> ScriptUndoFor(TweakDefinition tweak, int actionIndex)
    {
        var undo = tweak.Undo.Where(a => IsStatelessType(a.Type)).ToList();
        var scriptIndices = tweak.Apply.Select((a, i) => (a, i)).Where(x => IsStatelessType(x.a.Type)).Select(x => x.i).ToList();
        if (scriptIndices.Count == 1) return undo;
        int ordinal = scriptIndices.IndexOf(actionIndex);
        return ordinal >= 0 && ordinal < undo.Count ? new List<ActionItem> { undo[ordinal] } : new List<ActionItem>();
    }

    public async Task<ActionResult> RollbackAllUnfinishedSessionsAsync()
    {
        var unfinished = _context.Journal.GetUnfinishedSessions();
        if (unfinished.Count == 0)
        {
            return new ActionResult
            {
                Success = true,
                Message = "No unfinished sessions found."
            };
        }

        var allExecuted = new List<string>();
        var allFailed = new List<string>();

        // Revert newest sessions first
        foreach (var session in unfinished.OrderByDescending(s => s.StartedAtUtc))
        {
            var res = await RollbackSessionAsync(session);
            allExecuted.AddRange(res.ExecutedSteps);
            allFailed.AddRange(res.FailedSteps);
        }

        bool success = allFailed.Count == 0;
        return new ActionResult
        {
            Success = success,
            Message = success
                ? $"Rolled back {unfinished.Count} unfinished session(s) ({allExecuted.Count} action(s))."
                : $"Rollback had errors in {allFailed.Count} action(s): {string.Join("; ", allFailed)}",
            ExecutedSteps = allExecuted,
            FailedSteps = allFailed
        };
    }

    public void KeepAndCompleteAllUnfinishedSessions()
    {
        var unfinished = _context.Journal.GetUnfinishedSessions();
        foreach (var session in unfinished)
        {
            _context.Journal.CompleteSession(session);
        }
    }

    private static bool IsStatelessType(string? type)
    {
        if (string.IsNullOrWhiteSpace(type)) return false;
        return string.Equals(type, "PowerShell", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(type, "Script", StringComparison.OrdinalIgnoreCase);
    }

    private void RecordAdoptedState(TweakDefinition tweak)
    {
        for (int i = 0; i < tweak.Apply.Count; i++)
        {
            ActionItem action = tweak.Apply[i];
            if (_handlers.FirstOrDefault(h => h.CanHandle(action.Type)) is not IStateCaptureHandler capture) continue;

            JournalActionRecord? record = capture.CaptureCurrentState(action, tweak.Id, i, _context);
            if (record == null) continue;

            // A tweak that already has an active record for this target keeps its original baseline.
            bool alreadyJournaled = _context.Journal.GetAllSessions()
                .SelectMany(s => s.Records)
                .Any(r => !r.IsReverted && r.TweakId == tweak.Id &&
                          string.Equals(r.TargetIdentifier, record.TargetIdentifier, StringComparison.OrdinalIgnoreCase));
            if (!alreadyJournaled)
            {
                _context.Journal.RecordActionWriteAhead(record);
            }
        }
    }

    /// <summary>
    /// Several tweaks can change the same registry value or service (for example "Telemetry - Disable" and
    /// "Disable Advertising ID"). When the record being undone is OLDER than a still-applied record of another
    /// tweak on the same target, restoring it now would switch that other tweak off, and undoing the other tweak
    /// later would "restore" the value this tweak wrote. Instead the value is left alone and its original state is
    /// handed to the next newer record, so whichever tweak is undone last puts back the true original.
    /// Returns true when the restore must be skipped. Undoing the newest record restores normally: its saved value
    /// is what the older tweaks had set.
    /// </summary>
    private bool HandOverToNewerTweak(JournalActionRecord record)
    {
        JournalActionRecord? nextNewer = _context.Journal.GetAllSessions()
            .SelectMany(s => s.Records)
            .Where(r => !r.IsReverted &&
                        !string.Equals(r.TweakId, record.TweakId, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(r.ActionType, record.ActionType, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(r.TargetIdentifier, record.TargetIdentifier, StringComparison.OrdinalIgnoreCase) &&
                        r.AppliedAtUtc > record.AppliedAtUtc)
            .OrderBy(r => r.AppliedAtUtc)
            .FirstOrDefault();

        if (nextNewer == null) return false;
        return _context.Journal.InheritBaseline(nextNewer, record);
    }

    private async Task<bool> EnsureSystemRestorePointAsync(Func<string, Task<bool>>? onRestorePointPromptAsync, IProgress<string>? progress)
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
        progress?.Report("Creating a Windows restore point (this can take several minutes)...");
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
