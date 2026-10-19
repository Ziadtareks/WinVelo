using System;
using System.Threading.Tasks;
using WinVelo.Core.Models;
using WinVelo.Core.Safety;

namespace WinVelo.Core.Engine.Handlers;

public sealed class ServiceActionHandler : IActionHandler, IStateCaptureHandler
{
    public JournalActionRecord? CaptureCurrentState(ActionItem action, string tweakId, int actionIndex, ExecutionContext context)
    {
        if (string.IsNullOrWhiteSpace(action.ServiceName) || !context.Services.ServiceExists(action.ServiceName)) return null;

        return new JournalActionRecord
        {
            TweakId = tweakId,
            ActionIndex = actionIndex,
            ActionType = "Service",
            TargetIdentifier = action.ServiceName,
            ExistedBefore = true,
            PreviousValueKind = "ServiceConfig",
            PreviousData = $"{context.Services.GetStartupType(action.ServiceName)}|{context.Services.GetState(action.ServiceName)}",
            IsBaseline = true,
            AppliedAtUtc = DateTime.UtcNow
        };
    }

    public bool CanHandle(string actionType) =>
        actionType.Equals("Service", StringComparison.OrdinalIgnoreCase);

    public Task<ActionResult> ExecuteAsync(ActionItem action, string tweakId, int actionIndex, ExecutionContext context)
    {
        string? blockedByServicingGuard = ServicingGuard.CheckAction(action);
        if (blockedByServicingGuard != null)
        {
            return Task.FromResult(new ActionResult { Success = false, Message = "Blocked: " + blockedByServicingGuard });
        }

        if (string.IsNullOrWhiteSpace(action.ServiceName))
        {
            return Task.FromResult(new ActionResult { Success = false, Message = "Missing ServiceName." });
        }

        // Rule #1 (Upstream WinUtil #5140): If a Windows Service does not exist on the machine,
        // SKIP it silently and mark the action as Succeeded (Skipped - Service not installed).
        bool exists = context.Services.ServiceExists(action.ServiceName);
        if (!exists)
        {
            return Task.FromResult(new ActionResult
            {
                Success = true,
                Message = string.Format(ServiceMessages.NotInstalled, action.ServiceName)
            });
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
                    case "SetStartup":
                        if (!string.IsNullOrEmpty(action.StartupType))
                        {
                            try
                            {
                                context.Services.SetStartupType(action.ServiceName, action.StartupType);
                            }
                            catch (Exception)
                            {
                                // Rule #2 (Upstream WinUtil #5096): When sc.exe or ServiceController fails with Access Denied
                                // on protected services (e.g., WaaSMedicSvc, DiagTrack, Schedule, WinDefend),
                                // fall back to writing the Start DWORD directly in HKLM\SYSTEM\CurrentControlSet\Services\<ServiceName>.
                                int startDword = action.StartupType.ToLowerInvariant() switch
                                {
                                    "automatic" => 2,
                                    "manual" => 3,
                                    "disabled" => 4,
                                    _ => 3
                                };

                                try
                                {
                                    string svcKey = $@"SYSTEM\CurrentControlSet\Services\{action.ServiceName}";
                                    context.Registry.SetValue("HKLM", svcKey, "Start", startDword, "DWord");
                                }
                                catch (Exception regEx)
                                {
                                    // Blocked by TrustedInstaller / Tamper Protection: nothing changed, so this is a failure.
                                    // The runner rolls back the tweak's earlier steps.
                                    return Task.FromResult(new ActionResult
                                    {
                                        Success = false,
                                        Message = string.Format(ServiceMessages.ChangeFailed, action.ServiceName, regEx.Message)
                                    });
                                }
                            }
                        }
                        break;

                    case "Start":
                        try
                        {
                            context.Services.StartService(action.ServiceName);
                        }
                        catch (Exception ex)
                        {
                            return Task.FromResult(new ActionResult
                            {
                                Success = false,
                                Message = string.Format(ServiceMessages.StartFailed, action.ServiceName, ex.Message)
                            });
                        }
                        break;

                    case "Stop":
                        try
                        {
                            context.Services.StopService(action.ServiceName);
                        }
                        catch (Exception ex)
                        {
                            return Task.FromResult(new ActionResult
                            {
                                Success = false,
                                Message = string.Format(ServiceMessages.StopFailed, action.ServiceName, ex.Message)
                            });
                        }
                        break;

                    default:
                        return Task.FromResult(new ActionResult { Success = false, Message = $"Unknown service operation: {action.Operation}" });
                }
            }
            catch (Exception ex)
            {
                return Task.FromResult(new ActionResult
                {
                    Success = false,
                    Message = string.Format(ServiceMessages.ChangeFailed, action.ServiceName, ex.Message)
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
        if (!context.Services.ServiceExists(record.TargetIdentifier))
        {
            return Task.FromResult(new ActionResult
            {
                Success = true,
                Message = string.Format(ServiceMessages.NotInstalled, record.TargetIdentifier)
            });
        }

        string[] parts = (record.PreviousData ?? "Manual|Stopped").Split('|');
        string startup = parts[0];
        string state = parts.Length > 1 ? parts[1] : "Stopped";

        try
        {
            context.Services.SetStartupType(record.TargetIdentifier, startup);
        }
        catch
        {
            // Fallback to registry write on undo
            int startDword = startup.ToLowerInvariant() switch
            {
                "automatic" => 2,
                "manual" => 3,
                "disabled" => 4,
                _ => 3
            };
            try
            {
                string svcKey = $@"SYSTEM\CurrentControlSet\Services\{record.TargetIdentifier}";
                context.Registry.SetValue("HKLM", svcKey, "Start", startDword, "DWord");
            }
            catch (Exception ex)
            {
                // Not restored: report it, and the journal record stays so the undo can be retried.
                return Task.FromResult(new ActionResult { Success = false, Message = string.Format(ServiceMessages.RestoreFailed, record.TargetIdentifier, ex.Message) });
            }
        }

        try
        {
            if (state == "Running")
            {
                context.Services.StartService(record.TargetIdentifier);
            }
            else if (state == "Stopped")
            {
                context.Services.StopService(record.TargetIdentifier);
            }
        }
        catch (Exception ex)
        {
            return Task.FromResult(new ActionResult { Success = false, Message = string.Format(ServiceMessages.RestoreStateFailed, record.TargetIdentifier, ex.Message) });
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

/// <summary>
/// Result texts of service actions. They are fixed English patterns so the app can show them translated
/// (Lang-ar.json has the same patterns with {0} = service name, {1} = Windows error).
/// </summary>
public static class ServiceMessages
{
    public const string NotInstalled = "Service '{0}' is not installed on this edition of Windows (skipped).";
    public const string ChangeFailed = "Could not change service '{0}': {1}";
    public const string StartFailed = "Could not start service '{0}': {1}";
    public const string StopFailed = "Could not stop service '{0}': {1}";
    public const string RestoreFailed = "Could not restore the startup type of service '{0}': {1}";
    public const string RestoreStateFailed = "Could not restore the running state of service '{0}': {1}";
}
