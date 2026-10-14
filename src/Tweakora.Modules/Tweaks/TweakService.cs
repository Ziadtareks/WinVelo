using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Tweakora.Core.Engine;
using Tweakora.Core.Engine.Handlers;
using Tweakora.Core.Engine.Runners;
using Tweakora.Core.Interfaces;
using Tweakora.Core.Models;
using Tweakora.Core.Operations;
using Tweakora.Platform;

namespace Tweakora.Modules.Tweaks;

public interface ITweakService
{
    IReadOnlyList<TweakDefinition> LoadCatalog();
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
            BackupDirectory = Path.Combine(Path.GetTempPath(), "WinVelo_Backups")
        };

        _detector = new StateDetector(_registry, _services, _tasks);
        _runner = new TweakRunner(_handlers, context, _restore, _detector);
        _dryRun = new DryRunEngine(_handlers, context);
    }

    public IReadOnlyList<TweakDefinition> LoadCatalog()
    {
        string rootDir = AppContext.BaseDirectory;
        string tweaksDir = Path.Combine(rootDir, "Catalog", "Tweaks");

        if (!Directory.Exists(tweaksDir))
        {
            string? parent = Directory.GetParent(rootDir)?.FullName;
            while (parent != null)
            {
                string candidate = Path.Combine(parent, "Catalog", "Tweaks");
                if (Directory.Exists(candidate))
                {
                    tweaksDir = candidate;
                    break;
                }
                parent = Directory.GetParent(parent)?.FullName;
            }
        }

        List<TweakDefinition> list = new();
        if (Directory.Exists(tweaksDir))
        {
            foreach (string file in Directory.GetFiles(tweaksDir, "*.json", SearchOption.AllDirectories))
            {
                try
                {
                    string json = File.ReadAllText(file);
                    var items = JsonSerializer.Deserialize<List<TweakDefinition>>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    if (items != null) list.AddRange(items);
                }
                catch { }
            }
        }

        return list;
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

        _ = WindowsSettingNotifier.NotifySettingsChangedAsync();
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

        _ = WindowsSettingNotifier.NotifySettingsChangedAsync();
        return report;
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
        if (string.Equals(primary, "DHCP", StringComparison.OrdinalIgnoreCase) || string.Equals(primary, "Automatic", StringComparison.OrdinalIgnoreCase))
        {
            script = "Get-NetAdapter | Where-Object Status -eq 'Up' | Set-DnsClientServerAddress -ResetServerAddresses";
        }
        else if (string.IsNullOrWhiteSpace(secondary))
        {
            script = $"Get-NetAdapter | Where-Object Status -eq 'Up' | Set-DnsClientServerAddress -ServerAddresses '{primary}'";
        }
        else
        {
            script = $"Get-NetAdapter | Where-Object Status -eq 'Up' | Set-DnsClientServerAddress -ServerAddresses '{primary}', '{secondary}'";
        }

        var res = await _shell.ExecutePowerShellAsync(script, 30);
        return res.Success;
    }
}
