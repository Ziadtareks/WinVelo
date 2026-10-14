using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tweakora.App.Services;
using Tweakora.Core.Engine.Runners;
using Tweakora.Core.Interfaces;
using Tweakora.Core.Models;
using Tweakora.Core.Operations;
using Tweakora.Modules.Tweaks;

namespace Tweakora.App.ViewModels;

public sealed class HistorySessionCardModel
{
    public required string SessionId { get; init; }
    public required DateTime StartedAtUtc { get; init; }
    public string StartedAtDisplay => StartedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
    public required bool IsCompleted { get; init; }
    public string TriggerSource { get; init; } = "WinVelo Engine";
    public required IReadOnlyList<JournalActionRecord> Records { get; init; }
    public int TotalActions => Records.Count;
    public int ActiveActions => Records.Count(r => !r.IsReverted);
    public int RevertedActions => Records.Count(r => r.IsReverted);
    public string DistinctTweaksSummary => string.Join(", ", Records.Select(r => r.TweakId).Where(id => !string.IsNullOrEmpty(id)).Distinct());
}

public sealed partial class HistoryViewModel : ObservableObject
{
    private readonly ITweakService? _tweakService;
    private readonly ISystemRestoreService? _restoreService;
    private readonly IOperationRunner _runner;

    public ObservableCollection<HistorySessionCardModel> Sessions { get; } = new();
    public ObservableCollection<BackupSnapshotInfo> Snapshots { get; } = new();

    [ObservableProperty]
    private HistorySessionCardModel? _selectedSession;

    [ObservableProperty]
    private BackupSnapshotInfo? _selectedSnapshot;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = "Review changes applied by WinVelo with rollback journal protection.";

    [ObservableProperty]
    private int _restorePointCount;

    [ObservableProperty]
    private bool _isRestoreEnabled;

    [ObservableProperty]
    private string _exportedLogContent = string.Empty;

    [ObservableProperty]
    private bool _isExportDialogOpen;

    public HistoryViewModel(
        ITweakService? tweakService = null,
        ISystemRestoreService? restoreService = null,
        IOperationRunner? runner = null)
    {
        _tweakService = tweakService;
        _restoreService = restoreService;
        _runner = runner ?? new OperationRunner();

        RefreshHistory();
    }

    [RelayCommand]
    public void RefreshHistory()
    {
        Sessions.Clear();
        Snapshots.Clear();

        if (_tweakService == null)
        {
            StatusMessage = "Tweak engine not available.";
            return;
        }

        try
        {
            var sessions = _tweakService.GetAllSessions();
            foreach (var s in sessions)
            {
                string trigger = "Manual Apply";
                if (s.Records.Any(r => r.TweakId.Contains("gaming", StringComparison.OrdinalIgnoreCase)))
                {
                    trigger = "Preset / Gaming";
                }
                else if (s.Records.Any(r => r.TweakId.Contains("privacy", StringComparison.OrdinalIgnoreCase)))
                {
                    trigger = "Preset / Privacy";
                }

                Sessions.Add(new HistorySessionCardModel
                {
                    SessionId = s.SessionId,
                    StartedAtUtc = s.StartedAtUtc,
                    IsCompleted = s.IsCompleted,
                    TriggerSource = trigger,
                    Records = s.Records.ToList()
                });
            }

            if (Sessions.Count > 0)
            {
                SelectedSession = Sessions[0];
            }

            var backups = _tweakService.BackupManager?.GetAvailableBackups() ?? Array.Empty<BackupSnapshotInfo>();
            foreach (var b in backups)
            {
                Snapshots.Add(b);
            }

            if (Snapshots.Count > 0)
            {
                SelectedSnapshot = Snapshots[0];
            }

            if (_restoreService != null)
            {
                IsRestoreEnabled = _restoreService.IsSystemRestoreEnabled();
                RestorePointCount = _restoreService.GetRestorePointCount();
            }

            StatusMessage = $"Loaded {Sessions.Count} journal session(s) and {Snapshots.Count} backup snapshot(s).";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load history: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task UndoSessionAsync(HistorySessionCardModel? session)
    {
        session ??= SelectedSession;
        if (session == null)
        {
            StatusMessage = "Please select a session to undo.";
            return;
        }

        if (!AppAdminGuard.EnsureAdmin("revert a journal session"))
        {
            StatusMessage = "Administrator privileges required to undo session changes.";
            return;
        }

        if (_tweakService == null)
        {
            StatusMessage = "Tweak engine not available.";
            return;
        }

        IsBusy = true;
        StatusMessage = $"Reverting changes from session {session.SessionId}...";
        await Task.Yield();

        try
        {
            var underlying = new RollbackJournalSession
            {
                SessionId = session.SessionId,
                StartedAtUtc = session.StartedAtUtc,
                IsCompleted = session.IsCompleted,
                Records = session.Records.ToList()
            };

            var result = await _tweakService.RollbackSessionAsync(underlying);
            if (result.Success)
            {
                StatusMessage = $"Session {session.SessionId} reverted successfully.";
            }
            else
            {
                StatusMessage = $"Session undo partially failed: {result.Message ?? "Some actions could not be reverted"}";
            }

            RefreshHistory();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Undo failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task UndoTweakAsync(string? tweakId)
    {
        if (string.IsNullOrWhiteSpace(tweakId)) return;

        if (!AppAdminGuard.EnsureAdmin($"revert tweak '{tweakId}'"))
        {
            StatusMessage = "Administrator privileges required to undo tweak.";
            return;
        }

        if (_tweakService == null)
        {
            StatusMessage = "Tweak engine not available.";
            return;
        }

        IsBusy = true;
        StatusMessage = $"Undoing tweak {tweakId}...";
        await Task.Yield();

        try
        {
            var catalog = _tweakService.LoadCatalog();
            var tweak = catalog.FirstOrDefault(t => string.Equals(t.Id, tweakId, StringComparison.OrdinalIgnoreCase));
            if (tweak == null)
            {
                StatusMessage = $"Tweak '{tweakId}' not found in catalog.";
                return;
            }

            var report = await _tweakService.UndoTweaksAsync(new[] { tweak }, _runner);
            if (report.FailedCount == 0)
            {
                StatusMessage = $"Tweak '{tweak.Name}' reverted successfully.";
            }
            else
            {
                StatusMessage = $"Undo failed for '{tweak.Name}': {report.Summary}";
            }

            RefreshHistory();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Undo error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task UndoAllChangesAsync()
    {
        if (!AppAdminGuard.EnsureAdmin("revert all WinVelo changes"))
        {
            StatusMessage = "Administrator privileges required to undo all changes.";
            return;
        }

        if (_tweakService == null)
        {
            StatusMessage = "Tweak engine not available.";
            return;
        }

        var activeTweaks = _tweakService.GetActiveJournalTweaks();
        if (activeTweaks.Count == 0)
        {
            StatusMessage = "No active WinVelo changes found in the journal.";
            return;
        }

        IsBusy = true;
        StatusMessage = $"Undoing all {activeTweaks.Count} active tweak(s)...";
        await Task.Yield();

        try
        {
            var report = await _tweakService.UndoTweaksAsync(activeTweaks, _runner);
            StatusMessage = report.FailedCount == 0
                ? $"Successfully reverted all {activeTweaks.Count} WinVelo tweaks."
                : $"Reverted with warnings: {report.Summary}";

            RefreshHistory();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to undo all changes: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public void ExportSessionLog(HistorySessionCardModel? session)
    {
        session ??= SelectedSession;
        if (session == null) return;

        var sb = new StringBuilder();
        sb.AppendLine("================================================================================");
        sb.AppendLine($" WINVELO JOURNAL SESSION EXPORT: {session.SessionId}");
        sb.AppendLine("================================================================================");
        sb.AppendLine($"Started (Local):   {session.StartedAtDisplay}");
        sb.AppendLine($"Trigger Source:    {session.TriggerSource}");
        sb.AppendLine($"Status:            {(session.IsCompleted ? "Completed" : "Interrupted / Unfinished")}");
        sb.AppendLine($"Total Actions:     {session.TotalActions} (Active: {session.ActiveActions}, Reverted: {session.RevertedActions})");
        sb.AppendLine($"Touched Tweaks:    {session.DistinctTweaksSummary}");
        sb.AppendLine("--------------------------------------------------------------------------------");
        sb.AppendLine("RECORDED ACTIONS (CHRONOLOGICAL ORDER):");
        sb.AppendLine("--------------------------------------------------------------------------------");

        int idx = 1;
        foreach (var r in session.Records)
        {
            string status = r.IsReverted ? "[REVERTED]" : "[ACTIVE]";
            string baseline = r.IsBaseline ? "(Baseline)" : "";
            sb.AppendLine($"{idx++:D2}. {status,-10} {r.ActionType,-12} | Tweak: {r.TweakId,-35} | Target: {r.TargetIdentifier} {baseline}");
        }

        sb.AppendLine("================================================================================");
        ExportedLogContent = sb.ToString();
        IsExportDialogOpen = true;

        try
        {
            string exportDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinVelo", "Exports");
            Directory.CreateDirectory(exportDir);
            string filePath = Path.Combine(exportDir, $"session-{session.SessionId}.txt");
            File.WriteAllText(filePath, ExportedLogContent);
            StatusMessage = $"Session log exported to: {filePath}";
        }
        catch
        {
            StatusMessage = "Session log exported to preview window.";
        }
    }

    [RelayCommand]
    public void CloseExportDialog()
    {
        IsExportDialogOpen = false;
    }

    [RelayCommand]
    public async Task CreateRestorePointAsync()
    {
        if (!AppAdminGuard.EnsureAdmin("create a Windows System Restore point"))
        {
            StatusMessage = "Administrator privileges required to create a restore point.";
            return;
        }

        if (_restoreService == null)
        {
            StatusMessage = "System Restore service not available.";
            return;
        }

        IsBusy = true;
        StatusMessage = "Creating Windows System Restore point...";
        await Task.Yield();

        try
        {
            var result = await Task.Run(() => _restoreService.CreateRestorePoint("WinVelo Manual Checkpoint"));
            StatusMessage = result.Success
                ? $"Restore point created successfully (Seq #{result.SequenceNumber})."
                : (result.WasThrottledOrSkipped
                    ? "Windows throttled restore point creation (24h limit). Existing point preserved."
                    : $"Failed to create restore point: {result.ErrorMessage ?? "Unknown error"}");

            IsRestoreEnabled = _restoreService.IsSystemRestoreEnabled();
            RestorePointCount = _restoreService.GetRestorePointCount();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Restore point error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task RestoreSnapshotAsync(BackupSnapshotInfo? snapshot)
    {
        snapshot ??= SelectedSnapshot;
        if (snapshot == null)
        {
            StatusMessage = "Please select a backup snapshot to restore.";
            return;
        }

        if (!AppAdminGuard.EnsureAdmin("restore a system backup snapshot"))
        {
            StatusMessage = "Administrator privileges required to restore snapshots.";
            return;
        }

        if (_tweakService?.BackupManager == null)
        {
            StatusMessage = "Backup manager not available.";
            return;
        }

        IsBusy = true;
        StatusMessage = $"Restoring backup snapshot {snapshot.Id}...";
        await Task.Yield();

        try
        {
            bool success = await Task.Run(async () => await _tweakService.BackupManager.RestoreBackupAsync(snapshot.DirectoryPath, msg =>
            {
                StatusMessage = msg;
            }));

            StatusMessage = success
                ? $"Backup snapshot {snapshot.Id} restored successfully."
                : "Failed to restore backup snapshot.";

            RefreshHistory();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Snapshot restore error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
