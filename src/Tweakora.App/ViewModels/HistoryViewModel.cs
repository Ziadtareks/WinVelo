using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tweakora.App.Services;
using Tweakora.Core.Engine.Runners;
using Tweakora.Core.Interfaces;
using Tweakora.Core.Models;
using Tweakora.Core.Operations;
using Tweakora.Core.Safety;
using Tweakora.Modules.Tweaks;

namespace Tweakora.App.ViewModels;

public sealed class HistoryTweakItemModel
{
    public required string TweakId { get; init; }
    public required string TweakName { get; init; }
    public required int ActionCount { get; init; }
    public required bool IsReverted { get; init; }
    public string StatusText => IsReverted ? "Reverted" : "Active";
    public string StatusBg => IsReverted ? "#374151" : "#064E3B";
    public string StatusFg => IsReverted ? "#9CA3AF" : "#10B981";
    public required IReadOnlyList<JournalActionRecord> Records { get; init; }
}

public sealed class HistorySessionCardModel
{
    public required string SessionId { get; init; }
    public required DateTime StartedAtUtc { get; init; }
    public string StartedAtDisplay => StartedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
    public required bool IsCompleted { get; init; }
    public string TriggerSource { get; init; } = "Manual";
    public string? Mode { get; init; }
    public string? AppVersion { get; init; }
    public bool IsLegacy { get; init; }
    public required IReadOnlyList<JournalActionRecord> Records { get; init; }
    public required IReadOnlyList<HistoryTweakItemModel> Tweaks { get; init; }
    public int TotalActions => Records.Count;
    public int ActiveActions => Records.Count(r => !r.IsReverted);
    public int RevertedActions => Records.Count(r => r.IsReverted);
    public string StatusText => IsLegacy ? "Legacy (Read Only)" : (ActiveActions == 0 ? "Reverted" : (RevertedActions > 0 ? "Partially Reverted" : (IsCompleted ? "Active" : "Unfinished")));
    public string StatusBg => IsLegacy ? "#1F2937" : (ActiveActions == 0 ? "#374151" : (RevertedActions > 0 ? "#451A03" : "#064E3B"));
    public string StatusFg => IsLegacy ? "#9CA3AF" : (ActiveActions == 0 ? "#9CA3AF" : (RevertedActions > 0 ? "#FBBF24" : "#10B981"));
    public string DistinctTweaksSummary => string.Join(", ", Tweaks.Select(t => t.TweakName));
    public bool CanUndo => !IsLegacy;
    public Visibility UndoVisibility => IsLegacy ? Visibility.Collapsed : Visibility.Visible;
    public Visibility LegacyBannerVisibility => IsLegacy ? Visibility.Visible : Visibility.Collapsed;
}

public sealed partial class HistoryViewModel : ObservableObject
{
    private readonly ITweakService? _tweakService;
    private readonly ISystemRestoreService? _restoreService;
    private readonly IOperationRunner _runner;
    private readonly UnifiedExecutionDialogViewModel? _executionDialog;

    public ObservableCollection<HistorySessionCardModel> Sessions { get; } = new();
    public ObservableCollection<HistorySessionCardModel> LegacySessions { get; } = new();

    [ObservableProperty]
    private bool _isLegacySectionExpanded;

    public bool HasLegacySessions => LegacySessions.Count > 0;

    public ObservableCollection<RestorePointItem> RestorePoints { get; } = new();
    public ObservableCollection<BackupSnapshotInfo> Snapshots { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedSession))]
    private HistorySessionCardModel? _selectedSession;

    public bool HasSelectedSession => SelectedSession != null;

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

    public ObservableCollection<LogEntryItem> ActivityLogs { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTimelineView))]
    private bool _isActivityLogView;

    public bool IsTimelineView => !IsActivityLogView;

    [ObservableProperty]
    private string _activeHistoryView = "Sessions"; // "Sessions" or "ActivityLog"

    public HistoryViewModel(
        ITweakService? tweakService = null,
        ISystemRestoreService? restoreService = null,
        IOperationRunner? runner = null,
        UnifiedExecutionDialogViewModel? executionDialog = null)
    {
        _tweakService = tweakService;
        _restoreService = restoreService;
        _runner = runner ?? new OperationRunner();
        _executionDialog = executionDialog;

        _runner.OnLog += msg =>
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                ActivityLogs.Insert(0, new LogEntryItem
                {
                    Timestamp = DateTime.Now.ToString("HH:mm:ss"),
                    Level = "INFO",
                    Message = msg
                });
                if (ActivityLogs.Count > 300) ActivityLogs.RemoveAt(ActivityLogs.Count - 1);
            });
        };

        RefreshHistory();
        LoadActivityLogs();
    }

    [RelayCommand]
    public void SetHistoryView(string view)
    {
        ActiveHistoryView = view;
        IsActivityLogView = string.Equals(view, "ActivityLog", StringComparison.OrdinalIgnoreCase);
        if (IsActivityLogView)
        {
            LoadActivityLogs();
        }
    }

    [RelayCommand]
    public void RefreshActivityLogs()
    {
        LoadActivityLogs();
    }

    [RelayCommand]
    public void ClearActivityLogs()
    {
        ActivityLogs.Clear();
    }

    public void LoadActivityLogs()
    {
        ActivityLogs.Clear();
        try
        {
            string logsDir = WinVeloStoragePaths.GetLogsDirectory(TestModeManager.IsTestMode);
            if (Directory.Exists(logsDir))
            {
                var latestLog = Directory.GetFiles(logsDir, "*.log")
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault();

                if (latestLog != null)
                {
                    var lines = File.ReadAllLines(latestLog);
                    var recent = lines.TakeLast(100).Reverse();
                    foreach (var line in recent)
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        string level = "INFO";
                        if (line.Contains("[ERR]") || line.Contains("ERROR")) level = "ERR";
                        else if (line.Contains("[WARN]")) level = "WARN";
                        else if (line.Contains("[OK]")) level = "OK";

                        ActivityLogs.Add(new LogEntryItem
                        {
                            Timestamp = DateTime.Now.ToString("HH:mm:ss"),
                            Level = level,
                            Message = line
                        });
                    }
                }
            }
        }
        catch { }
    }

    [RelayCommand]
    public void RefreshHistory()
    {
        Sessions.Clear();
        LegacySessions.Clear();
        RestorePoints.Clear();
        Snapshots.Clear();

        if (_tweakService == null)
        {
            StatusMessage = "Tweak engine not available.";
            return;
        }

        try
        {
            var catalog = _tweakService.LoadCatalog();
            var tweakDict = catalog.ToDictionary(t => t.Id, t => t.Name, StringComparer.OrdinalIgnoreCase);

            var rawSessions = _tweakService.GetAllSessions()
                .OrderByDescending(s => s.StartedAtUtc)
                .ToList();

            foreach (var s in rawSessions)
            {
                string trigger = "Manual";
                if (s.SessionId.StartsWith("cli", StringComparison.OrdinalIgnoreCase) ||
                    s.SessionId.Contains("_cli", StringComparison.OrdinalIgnoreCase))
                {
                    trigger = "CLI";
                }
                else if (s.Records.Any(r => r.TweakId.Contains("gaming", StringComparison.OrdinalIgnoreCase)))
                {
                    trigger = "Preset (Gaming)";
                }
                else if (s.Records.Any(r => r.TweakId.Contains("privacy", StringComparison.OrdinalIgnoreCase)))
                {
                    trigger = "Preset (Privacy)";
                }
                else if (s.Records.Any(r => r.TweakId.Contains("recommended", StringComparison.OrdinalIgnoreCase)))
                {
                    trigger = "Preset (Recommended)";
                }

                var tweakGroups = s.Records
                    .GroupBy(r => r.TweakId)
                    .Select(g => new HistoryTweakItemModel
                    {
                        TweakId = g.Key,
                        TweakName = tweakDict.TryGetValue(g.Key, out var name) ? name : g.Key,
                        ActionCount = g.Count(),
                        IsReverted = g.All(r => r.IsReverted),
                        Records = g.ToList()
                    })
                    .ToList();

                bool isLegacy = !TestModeManager.IsTestMode && s.IsLegacy;

                var card = new HistorySessionCardModel
                {
                    SessionId = s.SessionId,
                    StartedAtUtc = s.StartedAtUtc,
                    IsCompleted = s.IsCompleted,
                    TriggerSource = trigger,
                    Mode = s.Mode ?? (isLegacy ? "legacy" : (TestModeManager.IsTestMode ? "test" : "real")),
                    AppVersion = s.AppVersion,
                    IsLegacy = isLegacy,
                    Records = s.Records.ToList(),
                    Tweaks = tweakGroups
                };

                if (isLegacy)
                {
                    LegacySessions.Add(card);
                }
                else
                {
                    Sessions.Add(card);
                }
            }

            if (Sessions.Count > 0)
            {
                SelectedSession = Sessions[0];
            }
            else
            {
                SelectedSession = null;
            }

            OnPropertyChanged(nameof(HasLegacySessions));

            if (_restoreService != null)
            {
                IsRestoreEnabled = _restoreService.IsSystemRestoreEnabled();
                RestorePointCount = _restoreService.GetRestorePointCount();
                var points = _restoreService.GetRestorePoints();
                foreach (var p in points)
                {
                    RestorePoints.Add(p);
                }
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

            StatusMessage = $"Loaded {Sessions.Count} active journal session(s)" +
                            (LegacySessions.Count > 0 ? $" and {LegacySessions.Count} legacy session(s)" : "") +
                            $" ({RestorePoints.Count} restore point(s)).";
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

        if (session.IsLegacy)
        {
            StatusMessage = "Legacy sessions are read-only and cannot be reverted automatically.";
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

        var catalog = _tweakService.LoadCatalog();
        var tweakIds = session.Records.Select(r => r.TweakId).Where(id => !string.IsNullOrEmpty(id)).Distinct();
        var sessionTweaks = catalog.Where(t => tweakIds.Contains(t.Id, StringComparer.OrdinalIgnoreCase)).ToList();

        if (_executionDialog != null && sessionTweaks.Count > 0)
        {
            await _executionDialog.OpenPreviewAsync(
                title: $"Undo Session — {session.SessionId}",
                description: $"Revert all {session.TotalActions} actions across {sessionTweaks.Count} tweak(s) recorded in this session.",
                notice: "All baseline registry values and service configurations from this session will be restored.",
                confirmText: "Confirm & Revert Session",
                tweaks: sessionTweaks,
                isUndo: true,
                onConfirmed: async (_) =>
                {
                    var underlying = new RollbackJournalSession
                    {
                        SessionId = session.SessionId,
                        StartedAtUtc = session.StartedAtUtc,
                        IsCompleted = session.IsCompleted,
                        Records = session.Records.ToList()
                    };
                    var result = await _tweakService.RollbackSessionAsync(underlying);
                    var healthReport = await _tweakService.CheckServicingHealthAsync();
                    var report = new OperationExecutionReport
                    {
                        Title = $"Undo Session {session.SessionId}",
                        StartTime = DateTime.UtcNow,
                        EndTime = DateTime.UtcNow,
                        Results = sessionTweaks.Select(t => new OperationItemResult
                        {
                            Id = t.Id,
                            Name = t.Name,
                            Status = result.Success ? OperationItemStatus.Restored : OperationItemStatus.Failed,
                            Reason = result.Message
                        }).ToList()
                    };
                    _executionDialog.ShowResult(sessionTweaks, report, healthReport, isUndo: true);
                    RefreshHistory();
                });
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
            StatusMessage = result.Success
                ? $"Session {session.SessionId} reverted successfully."
                : $"Session undo partially failed: {result.Message ?? "Some actions could not be reverted"}";

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

        var catalog = _tweakService.LoadCatalog();
        var tweak = catalog.FirstOrDefault(t => string.Equals(t.Id, tweakId, StringComparison.OrdinalIgnoreCase));
        if (tweak == null)
        {
            StatusMessage = $"Tweak '{tweakId}' not found in catalog.";
            return;
        }

        if (_executionDialog != null)
        {
            await _executionDialog.OpenPreviewAsync(
                title: $"Undo Tweak — {tweak.Name}",
                description: $"Revert '{tweak.Name}' to its pre-WinVelo baseline state.",
                notice: "WinVelo will restore the pre-application registry and service baseline values.",
                confirmText: "Confirm & Revert",
                tweaks: new[] { tweak },
                isUndo: true,
                onConfirmed: async (confirmedTweaks) =>
                {
                    await _executionDialog.ExecuteAndShowResultAsync(confirmedTweaks, isUndo: true);
                    RefreshHistory();
                });
            return;
        }

        IsBusy = true;
        StatusMessage = $"Undoing tweak {tweakId}...";
        await Task.Yield();

        try
        {
            var report = await _tweakService.UndoTweaksAsync(new[] { tweak }, _runner);
            StatusMessage = report.FailedCount == 0
                ? $"Tweak '{tweak.Name}' reverted successfully."
                : $"Undo failed for '{tweak.Name}': {report.Summary}";

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

        if (_executionDialog != null)
        {
            await _executionDialog.OpenPreviewAsync(
                title: "Undo All WinVelo Changes",
                description: $"Revert all {activeTweaks.Count} tweak(s) recorded in the WinVelo rollback journal.",
                notice: "This will revert all tweaks recorded in the WinVelo rollback journal. Changes made by other tools or Windows Settings are not covered.",
                confirmText: "Confirm & Revert All",
                tweaks: activeTweaks,
                isUndo: true,
                onConfirmed: async (confirmedTweaks) =>
                {
                    await _executionDialog.ExecuteAndShowResultAsync(confirmedTweaks, isUndo: true);
                    RefreshHistory();
                });
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
        sb.AppendLine($"Status:            {session.StatusText}");
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
            RestorePoints.Clear();
            foreach (var p in _restoreService.GetRestorePoints())
            {
                RestorePoints.Add(p);
            }
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
