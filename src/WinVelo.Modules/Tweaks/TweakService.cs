using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinVelo.Core.Utilities;
using WinVelo.Core;
using WinVelo.Core.Catalog;
using WinVelo.Core.Engine;
using WinVelo.Core.Engine.Handlers;
using WinVelo.Core.Engine.Runners;
using WinVelo.Core.Interfaces;
using WinVelo.Core.Models;
using WinVelo.Core.Operations;
using WinVelo.Core.Safety;
using WinVelo.Platform;

namespace WinVelo.Modules.Tweaks;

public interface ITweakService
{
    IReadOnlyList<TweakDefinition> LoadCatalog();
    /// <summary>Problems found the last time the catalog was loaded (broken files or tweaks that were skipped).</summary>
    IReadOnlyList<CatalogIssue> CatalogIssues => Array.Empty<CatalogIssue>();
    IReadOnlyList<PresetDefinition> LoadPresets() => Array.Empty<PresetDefinition>();
    IReadOnlyList<TweakDefinition> GetActiveJournalTweaks() => Array.Empty<TweakDefinition>();
    bool IsRebootPending() => false;
    bool IsExplorerRestartPending() => false;
    Task<ServicingHealthReport> CheckServicingHealthAsync() => Task.FromResult(new ServicingHealthReport());
    Task<List<string>> RestoreServicingHealthAsync() => Task.FromResult(new List<string>());
    bool DetectState(TweakDefinition tweak);
    Task<string> GenerateDryRunDiffAsync(IEnumerable<TweakDefinition> tweaks);
    Task<OperationExecutionReport> ApplyTweaksAsync(
        IReadOnlyList<TweakDefinition> tweaks,
        IOperationRunner runner,
        CancellationToken cancellationToken = default);
    Task<OperationExecutionReport> UndoTweaksAsync(
        IReadOnlyList<TweakDefinition> tweaks,
        IOperationRunner runner,
        CancellationToken cancellationToken = default);
    Task<bool> SetPerformancePlanAsync(string planName);
    Task<bool> SetDnsAsync(string primary, string secondary);
    Task<bool> DetectStateAsync(TweakDefinition tweak, CancellationToken cancellationToken = default);
    Task<TweakState> DetectTweakStateAsync(TweakDefinition tweak, CancellationToken cancellationToken = default);
    BackupManager BackupManager { get; }
    IReadOnlyList<RollbackJournalSession> GetAllSessions() => Array.Empty<RollbackJournalSession>();
    IReadOnlyList<RollbackJournalSession> GetUnfinishedSessions() => Array.Empty<RollbackJournalSession>();
    Task<ActionResult> RollbackSessionAsync(RollbackJournalSession session) => Task.FromResult(new ActionResult { Success = true });
    Task<ActionResult> RollbackAllUnfinishedSessionsAsync() => Task.FromResult(new ActionResult { Success = true });
    void CompleteSession(RollbackJournalSession session) { }
    void KeepAndCompleteAllUnfinishedSessions() { }
}

public sealed class TweakService : ITweakService
{
    private readonly IRegistryService _registry;
    private readonly ISystemServicesService _services;
    private readonly ITaskSchedulerService _tasks;
    private readonly ISystemRestoreService _restore;
    private readonly IShellService _shell;
    private readonly RollbackJournalManager _journal;

    private readonly List<IActionHandler> _handlers;
    private readonly StateDetector _detector;
    private readonly TweakRunner _runner;
    private readonly DryRunEngine _dryRun;
    private readonly BackupManager _backupManager;
    private readonly object _issuesLock = new();
    private readonly List<CatalogIssue> _catalogIssues = new();
    private readonly Dictionary<string, List<CatalogIssue>> _catalogIssuesByArea = new();

    public BackupManager BackupManager => _backupManager;

    public TweakService(
        IRegistryService registry,
        ISystemServicesService services,
        ITaskSchedulerService tasks,
        ISystemRestoreService restore,
        IShellService shell,
        RollbackJournalManager journal)
    {
        _registry = registry;
        _services = services;
        _tasks = tasks;
        _restore = restore;
        _shell = shell;
        _journal = journal;

        _backupManager = new BackupManager(_registry, _services, _restore);

        _handlers = new List<IActionHandler>
        {
            new RegistryActionHandler(),
            new ServiceActionHandler(),
            new TaskActionHandler(),
            new FileActionHandler(),
            new PowerShellActionHandler()
        };

        var context = new Core.Engine.ExecutionContext
        {
            Registry = _registry,
            Services = _services,
            Tasks = _tasks,
            Shell = _shell,
            Journal = _journal,
            IsDryRun = false,
            BackupDirectory = WinVeloStoragePaths.GetRegistryBackupDirectory(TestModeManager.IsTestMode)
        };

        _detector = new StateDetector(_registry, _services, _tasks);
        _runner = new TweakRunner(_handlers, context, _restore, _detector);
        _dryRun = new DryRunEngine(_handlers, context);
    }

    public IReadOnlyList<CatalogIssue> CatalogIssues
    {
        get
        {
            lock (_issuesLock)
            {
                return _catalogIssues.ToList();
            }
        }
    }

    public IReadOnlyList<TweakDefinition> LoadCatalog()
    {
        var source = CatalogSource.Default;
        var result = CatalogLoader.LoadTweaks(source);
        CatalogTranslations.Apply(result.Items, source);
        RecordIssues("Tweaks", result.Issues);
        return result.Items;
    }

    public Task<ServicingHealthReport> CheckServicingHealthAsync()
    {
        return ServicingProtectionHelper.CheckServicingHealthAsync(_services, _registry);
    }

    public Task<List<string>> RestoreServicingHealthAsync()
    {
        return ServicingProtectionHelper.RestoreServicingComponentsAsync(_services, _registry);
    }

    public IReadOnlyList<PresetDefinition> LoadPresets()
    {
        var source = CatalogSource.Default;
        var result = CatalogLoader.LoadPresets(source);
        CatalogTranslations.Apply(result.Items, source);
        RecordIssues("Presets", result.Issues);
        return result.Items;
    }

    private void RecordIssues(string area, List<CatalogIssue> issues)
    {
        lock (_issuesLock)
        {
            _catalogIssuesByArea[area] = issues;
            _catalogIssues.Clear();
            foreach (var list in _catalogIssuesByArea.Values)
            {
                _catalogIssues.AddRange(list);
            }
        }
    }

    public IReadOnlyList<TweakDefinition> GetActiveJournalTweaks()
    {
        var catalog = LoadCatalog();
        var catalogMap = catalog.ToDictionary(t => t.Id, StringComparer.OrdinalIgnoreCase);

        var sessions = _journal.GetAllSessions();
        var orderedTweakIds = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var session in sessions)
        {
            // In real mode, exclude sessions without a marker (legacy sessions) from active changes / Undo-all
            if (!TestModeManager.IsTestMode && session.IsLegacy)
            {
                continue;
            }

            for (int i = session.Records.Count - 1; i >= 0; i--)
            {
                var r = session.Records[i];
                if (!r.IsReverted && !string.IsNullOrWhiteSpace(r.TweakId))
                {
                    if (seen.Add(r.TweakId))
                    {
                        orderedTweakIds.Add(r.TweakId);
                    }
                }
            }
        }

        var result = new List<TweakDefinition>();
        foreach (var id in orderedTweakIds)
        {
            if (catalogMap.TryGetValue(id, out var tweak))
            {
                result.Add(tweak);
            }
            else
            {
                result.Add(new TweakDefinition
                {
                    Id = id,
                    Name = id,
                    Category = "Custom",
                    Risk = "Safe"
                });
            }
        }

        return result;
    }

    public bool DetectState(TweakDefinition tweak)
    {
        return _detector.IsTweakActiveAsync(tweak, useCache: false).GetAwaiter().GetResult();
    }

    public Task<bool> DetectStateAsync(TweakDefinition tweak, CancellationToken cancellationToken = default)
    {
        return _detector.IsTweakActiveAsync(tweak, useCache: false);
    }

    public Task<TweakState> DetectTweakStateAsync(TweakDefinition tweak, CancellationToken cancellationToken = default)
    {
        return _detector.DetectTweakStateAsync(tweak, useCache: false);
    }

    public Task<string> GenerateDryRunDiffAsync(IEnumerable<TweakDefinition> tweaks)
    {
        var list = tweaks.ToList();
        if (list.Count == 0) return Task.FromResult("No tweaks selected for preview.");

        var previews = _dryRun.GeneratePreview(list);
        using var sw = new StringWriter();
        sw.WriteLine($"=== WINVELO DIFF PREVIEW ({list.Count} TWEAKS STAGED) ===");
        sw.WriteLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n");

        foreach (var p in previews)
        {
            sw.WriteLine($"[{p.Risk.ToUpperInvariant()}] {p.Name} ({p.TweakId})");
            foreach (var a in p.Actions)
            {
                sw.WriteLine($"  • {a.ActionType} {a.Target}");
                sw.WriteLine($"    {a.Operation} | Current: {a.CurrentState} -> Proposed: {a.ProposedState}");
            }
            sw.WriteLine();
        }

        return Task.FromResult(sw.ToString());
    }

    public async Task<OperationExecutionReport> ApplyTweaksAsync(
        IReadOnlyList<TweakDefinition> tweaks,
        IOperationRunner runner,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _backupManager.CreateSnapshotAsync(tweaks, "Automatic pre-tweak snapshot");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WARN] Could not create automatic backup snapshot before tweak application: {ex.Message}");
        }

        var report = await runner.RunAsync(
            "Apply Tweaks",
            tweaks,
            async (tweak, ct) =>
            {
                var batchReport = await _runner.ApplyBatchAsync(new[] { tweak }, cancellationToken: ct);
                var itemResult = batchReport.Results.FirstOrDefault();

                OperationItemStatus status = itemResult?.Status switch
                {
                    TweakExecutionStatus.Applied => OperationItemStatus.Applied,
                    TweakExecutionStatus.Skipped => OperationItemStatus.Skipped,
                    TweakExecutionStatus.FailedRolledBack => OperationItemStatus.FailedRolledBack,
                    TweakExecutionStatus.FailedRollbackIncomplete => OperationItemStatus.FailedRollbackIncomplete,
                    _ => OperationItemStatus.Failed
                };

                return new OperationItemResult
                {
                    Id = tweak.Id,
                    Name = tweak.Name,
                    Status = status,
                    Reason = itemResult?.Message ?? (status == OperationItemStatus.Applied ? "Applied successfully." : "Execution failed.")
                };
            },
            cancellationToken);

        _ = WindowsSettingNotifier.NotifySettingsChangedForKeysAsync(tweaks.SelectMany(t => t.Apply.Concat(t.Undo)).Select(a => a.Key ?? string.Empty));
        RestartExplorerIfNeeded(tweaks, report);
        return report;
    }

    public async Task<OperationExecutionReport> UndoTweaksAsync(
        IReadOnlyList<TweakDefinition> tweaks,
        IOperationRunner runner,
        CancellationToken cancellationToken = default)
    {
        var report = await runner.RunAsync(
            "Undo Tweaks",
            tweaks,
            async (tweak, ct) =>
            {
                var actionResult = await _runner.UndoTweakAsync(tweak);
                return new OperationItemResult
                {
                    Id = tweak.Id,
                    Name = tweak.Name,
                    Status = actionResult.Success ? OperationItemStatus.Restored : OperationItemStatus.Failed,
                    Reason = actionResult.Message ?? (actionResult.Success ? "Restored original state." : "Undo failed.")
                };
            },
            cancellationToken);

        _ = WindowsSettingNotifier.NotifySettingsChangedForKeysAsync(tweaks.SelectMany(t => t.Apply.Concat(t.Undo)).Select(a => a.Key ?? string.Empty));
        RestartExplorerIfNeeded(tweaks, report);
        return report;
    }

    /// <summary>
    /// Taskbar, Start and File Explorer settings only show up after Explorer reloads them.
    /// Restart Explorer once when a changed tweak is flagged requiresExplorerRestart and does
    /// not already restart Explorer in its own script.
    /// </summary>
    private void RestartExplorerIfNeeded(IReadOnlyList<TweakDefinition> tweaks, OperationExecutionReport report)
    {
        var changed = new HashSet<string>(
            report.Results
                .Where(r => r.Status is OperationItemStatus.Applied or OperationItemStatus.Restored or OperationItemStatus.Succeeded)
                .Select(r => r.Id),
            StringComparer.OrdinalIgnoreCase);

        bool needed = tweaks.Any(t =>
            t.RequiresExplorerRestart &&
            changed.Contains(t.Id) &&
            !t.Apply.Concat(t.Undo).Any(a => (a.Script ?? string.Empty).Contains("Stop-Process -Name explorer", StringComparison.OrdinalIgnoreCase)));

        if (!needed) return;

        // Run in the background: the restart waits for the shell to come back and must not freeze the UI.
        _ = Task.Run(() =>
        {
            try
            {
                _shell.RestartExplorer();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WARN] Explorer restart after tweak failed: {ex.Message}");
            }
        });
    }

    public async Task<bool> SetPerformancePlanAsync(string planName)
    {
        if (string.Equals(planName, "Ultimate Performance", StringComparison.OrdinalIgnoreCase))
        {
            string script = "powercfg -duplicatescheme e9a42b02-d5df-448d-aa00-03f14749eb61; powercfg /setactive e9a42b02-d5df-448d-aa00-03f14749eb61";
            var res = await _shell.ExecutePowerShellAsync(script, 30);
            return res.Success;
        }
        else
        {
            string script = "powercfg /setactive 381b4222-f694-41f0-9685-ff5bb260df2e";
            var res = await _shell.ExecutePowerShellAsync(script, 30);
            return res.Success;
        }
    }

    public async Task<bool> SetDnsAsync(string primary, string secondary)
    {
        string script;
        try
        {
            script = SafeCommands.SetDnsServers(primary, secondary);
        }
        catch (ArgumentException)
        {
            return false;
        }

        var res = await _shell.ExecutePowerShellAsync(script, 30);
        return res.Success;
    }

    public bool IsRebootPending()
    {
        try
        {
            var active = GetActiveJournalTweaks();
            return active.Any(t => t.RequiresReboot);
        }
        catch
        {
            return false;
        }
    }

    public bool IsExplorerRestartPending()
    {
        try
        {
            var active = GetActiveJournalTweaks();
            return active.Any(t => t.RequiresExplorerRestart);
        }
        catch
        {
            return false;
        }
    }

    public IReadOnlyList<RollbackJournalSession> GetAllSessions() => _journal.GetAllSessions();
    public IReadOnlyList<RollbackJournalSession> GetUnfinishedSessions() => _journal.GetUnfinishedSessions();
    public Task<ActionResult> RollbackSessionAsync(RollbackJournalSession session) => _runner.RollbackSessionAsync(session);
    public Task<ActionResult> RollbackAllUnfinishedSessionsAsync() => _runner.RollbackAllUnfinishedSessionsAsync();
    public void CompleteSession(RollbackJournalSession session) => _journal.CompleteSession(session);
    public void KeepAndCompleteAllUnfinishedSessions() => _runner.KeepAndCompleteAllUnfinishedSessions();
}
