using WinVelo.Core.Interfaces;
using WinVelo.Core.Models;

namespace WinVelo.Core.Engine;

public sealed class ExecutionContext
{
    public required IRegistryService Registry { get; init; }
    public required ISystemServicesService Services { get; init; }
    public required ITaskSchedulerService Tasks { get; init; }
    public required IShellService Shell { get; init; }
    public required RollbackJournalManager Journal { get; set; }
    public bool IsDryRun { get; init; }
    public bool IsUndoOperation { get; set; }
    public List<ActionItem>? PendingScriptUndo { get; set; }
    public CancellationToken CancellationToken { get; set; }
    public string BackupDirectory { get; init; } = string.Empty;
}

public sealed class ActionResult
{
    public bool Success { get; init; }
    public string? Message { get; init; }
    public JournalActionRecord? JournalRecord { get; init; }
    public List<string> ExecutedSteps { get; init; } = new();
    public List<string> FailedSteps { get; init; } = new();

    public static ActionResult Ok(string? message = null) => new() { Success = true, Message = message };
    public static ActionResult Fail(string? message = null) => new() { Success = false, Message = message };
}

public sealed class ActionPreview
{
    public string ActionType { get; init; } = string.Empty;
    public string Target { get; init; } = string.Empty;
    public string Operation { get; init; } = string.Empty;
    public string? CurrentState { get; init; }
    public string? ProposedState { get; init; }
}

/// <summary>
/// A handler that can record the current state of an action's target without changing it. Used for tweaks that
/// are already active when applied, so their undo puts back exactly what was there instead of running the
/// catalog undo (which could switch off the same setting of another tweak).
/// </summary>
public interface IStateCaptureHandler
{
    JournalActionRecord? CaptureCurrentState(ActionItem action, string tweakId, int actionIndex, ExecutionContext context);
}

public interface IActionHandler
{
    bool CanHandle(string actionType);
    Task<ActionResult> ExecuteAsync(ActionItem action, string tweakId, int actionIndex, ExecutionContext context);
    Task<ActionResult> UndoAsync(JournalActionRecord record, ExecutionContext context);
    ActionPreview Preview(ActionItem action, ExecutionContext context);
}
