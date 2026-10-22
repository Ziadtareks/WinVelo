using WinVelo.Core.Models;

namespace WinVelo.Core.Engine.Handlers;

public sealed class FileActionHandler : IActionHandler
{
    private static readonly string[] AllowedRoots =
    [
        "%TEMP%",
        "%LOCALAPPDATA%\\Temp",
        "%WINDIR%\\Temp",
        "%WINDIR%\\SoftwareDistribution\\Download"
    ];

    public bool CanHandle(string actionType) =>
        actionType.Equals("File", StringComparison.OrdinalIgnoreCase);

    public Task<ActionResult> ExecuteAsync(ActionItem action, string tweakId, int actionIndex, ExecutionContext context)
    {
        if (string.IsNullOrWhiteSpace(action.AllowedRoot) || !AllowedRoots.Contains(action.AllowedRoot, StringComparer.OrdinalIgnoreCase))
        {
            return Task.FromResult(new ActionResult { Success = false, Message = $"Security violation: Root '{action.AllowedRoot}' is not in the safe allow-list." });
        }

        if (context.IsDryRun || WinVelo.Core.Safety.TestModeManager.IsTestMode)
        {
            return Task.FromResult(new ActionResult { Success = true });
        }

        // Stop required services before cleanup if requested (e.g. wuauserv)
        if (action.StopServicesBefore != null)
        {
            foreach (string service in action.StopServicesBefore)
            {
                if (context.Services.ServiceExists(service))
                {
                    context.Services.StopService(service);
                }
            }
        }

        try
        {
            string expandedRoot = Environment.ExpandEnvironmentVariables(action.AllowedRoot);
            if (Directory.Exists(expandedRoot))
            {
                if (action.Operation == "EmptyFolder")
                {
                    DirectoryInfo di = new(expandedRoot);
                    foreach (FileInfo file in di.GetFiles())
                    {
                        try { file.Delete(); } catch { /* in-use files skipped */ }
                    }
                    foreach (DirectoryInfo dir in di.GetDirectories())
                    {
                        try { dir.Delete(true); } catch { /* in-use folders skipped */ }
                    }
                }
            }
        }
        finally
        {
            // Restart stopped services after cleanup
            if (action.StartServicesAfter != null)
            {
                foreach (string service in action.StartServicesAfter)
                {
                    if (context.Services.ServiceExists(service))
                    {
                        context.Services.StartService(service);
                    }
                }
            }
        }

        return Task.FromResult(new ActionResult { Success = true });
    }

    public Task<ActionResult> UndoAsync(JournalActionRecord record, ExecutionContext context)
    {
        // Temp file deletion cannot be reversed, but is safe and idempotent
        return Task.FromResult(new ActionResult { Success = true, Message = "Temp files cleaned (cannot restore deleted cache)." });
    }

    public ActionPreview Preview(ActionItem action, ExecutionContext context)
    {
        return new ActionPreview
        {
            ActionType = "File",
            Target = action.AllowedRoot ?? "",
            Operation = action.Operation,
            CurrentState = "Cached Files",
            ProposedState = "Cleaned"
        };
    }
}
