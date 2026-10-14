using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tweakora.App.Services;
using Tweakora.Core.Engine.Runners;
using Tweakora.Modules.Tweaks;

namespace Tweakora.App.ViewModels;

public sealed partial class BackupsViewModel : ObservableObject
{
    private readonly ITweakService _tweakService;

    public ObservableCollection<BackupSnapshotInfo> Backups { get; } = new();

    [ObservableProperty]
    private BackupSnapshotInfo? _selectedBackup;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = "Select a backup snapshot to restore or inspect.";

    public BackupsViewModel(ITweakService tweakService)
    {
        _tweakService = tweakService;
        RefreshBackups();
    }

    [RelayCommand]
    public void RefreshBackups()
    {
        Backups.Clear();
        var list = _tweakService.BackupManager.GetAvailableBackups();
        foreach (var b in list)
        {
            Backups.Add(b);
        }

        if (Backups.Count > 0)
        {
            SelectedBackup = Backups[0];
            StatusMessage = $"Loaded {Backups.Count} backup snapshot(s).";
        }
        else
        {
            StatusMessage = "No backup snapshots found. Snapshots are created automatically before applying tweaks.";
        }
    }

    [RelayCommand]
    public async Task RestoreSelectedBackupAsync()
    {
        if (SelectedBackup == null)
        {
            StatusMessage = "Please select a backup snapshot from the list.";
            return;
        }

        if (!AppAdminGuard.EnsureAdmin("restore a system backup snapshot"))
        {
            StatusMessage = "Administrator privileges required to restore system backups.";
            return;
        }

        IsBusy = true;
        StatusMessage = $"Restoring backup snapshot {SelectedBackup.Id}...";
        await Task.Yield();

        try
        {
            string backupDir = SelectedBackup.DirectoryPath;
            bool success = await Task.Run(async () => await _tweakService.BackupManager.RestoreBackupAsync(backupDir, msg =>
            {
                StatusMessage = msg;
            }));

            StatusMessage = success
                ? $"Backup snapshot {SelectedBackup.Id} restored successfully."
                : "Failed to restore backup snapshot.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Restore error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task CreateInstantBackupAsync()
    {
        IsBusy = true;
        StatusMessage = "Creating manual snapshot of all tweaks...";
        await Task.Yield();

        try
        {
            string dir = await Task.Run(async () =>
            {
                var tweaks = _tweakService.LoadCatalog();
                return await _tweakService.BackupManager.CreateSnapshotAsync(tweaks, "Manual full-state snapshot");
            });
            RefreshBackups();
            StatusMessage = $"Instant snapshot created at: {Path.GetFileName(dir)}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Snapshot error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public void OpenBackupsFolder()
    {
        string dir = _tweakService.BackupManager.BackupsRootDirectory;
        Directory.CreateDirectory(dir);
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true
            });
        }
        catch { }
    }
}
