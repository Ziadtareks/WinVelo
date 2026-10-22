using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using WinVelo.Core.Interfaces;
using WinVelo.Core.Models;
using WinVelo.Core.Engine.Handlers;
using WinVelo.Core.Safety;

namespace WinVelo.Core.Engine.Runners;

public sealed class BackupSnapshotItem
{
    public string Type { get; set; } = string.Empty; // "Registry", "Service"
    public string Target { get; set; } = string.Empty; // e.g. "HKLM\Software\..." or "wuauserv"
    public string? ValueName { get; set; }
    public string? ValueKind { get; set; }
    public object? Data { get; set; }
    public bool Existed { get; set; }
}

public sealed class BackupSnapshotMetadata
{
    public string Id { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string Description { get; set; } = string.Empty;
    public List<string> TweakIds { get; set; } = new();
    public List<BackupSnapshotItem> Items { get; set; } = new();
    public List<string> RegFiles { get; set; } = new();
}

public sealed class BackupSnapshotInfo
{
    public required string Id { get; init; }
    public required DateTime CreatedAt { get; init; }
    public required string DirectoryPath { get; init; }
    public required string Description { get; init; }
    public required int ItemCount { get; init; }
    public required int RegFileCount { get; init; }
}

public sealed class BackupManager
{
    private readonly IRegistryService _registry;
    private readonly ISystemServicesService _services;
    private readonly ISystemRestoreService _restoreService;
    private readonly string _backupsRoot;

    public BackupManager(
        IRegistryService registry,
        ISystemServicesService services,
        ISystemRestoreService restoreService,
        string? customBackupsDir = null)
    {
        _registry = registry;
        _services = services;
        _restoreService = restoreService;
        _backupsRoot = customBackupsDir ?? WinVeloStoragePaths.GetRegistryBackupDirectory(TestModeManager.IsTestMode);
    }

    public string BackupsRootDirectory => _backupsRoot;

    public Task<string> CreateSnapshotAsync(IEnumerable<TweakDefinition> tweaks, string? description = null)
    {
        Directory.CreateDirectory(_backupsRoot);
        string folderName = DateTime.Now.ToString("yyyy-MM-dd_HHmmss_") + Guid.NewGuid().ToString("N")[..8];
        string backupDir = Path.Combine(_backupsRoot, folderName);
        Directory.CreateDirectory(backupDir);

        var regDir = Path.Combine(backupDir, "reg");
        Directory.CreateDirectory(regDir);

        var tweakList = tweaks.ToList();
        var metadata = new BackupSnapshotMetadata
        {
            Id = folderName,
            CreatedAtUtc = DateTime.UtcNow,
            Description = description ?? $"Pre-tweak snapshot for {tweakList.Count} tweak(s)",
            TweakIds = tweakList.Select(t => t.Id).ToList()
        };

        var touchedKeys = new HashSet<(string Hive, string Key)>();

        foreach (var tweak in tweakList)
        {
            foreach (var action in tweak.Apply)
            {
                if (action.Type.Equals("Registry", StringComparison.OrdinalIgnoreCase) ||
                    action.Type.Equals("Policy", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrEmpty(action.Hive) && !string.IsNullOrEmpty(action.Key))
                    {
                        touchedKeys.Add((action.Hive, action.Key));

                        bool keyExisted = _registry.KeyExists(action.Hive, action.Key);
                        bool valExisted = keyExisted && _registry.ValueExists(action.Hive, action.Key, action.ValueName ?? string.Empty);
                        object? curData = valExisted ? _registry.GetValue(action.Hive, action.Key, action.ValueName!) : null;
                        string? curKind = valExisted ? _registry.GetValueKind(action.Hive, action.Key, action.ValueName!) : null;

                        metadata.Items.Add(new BackupSnapshotItem
                        {
                            Type = "Registry",
                            Target = $"{action.Hive}\\{action.Key}",
                            ValueName = action.ValueName,
                            ValueKind = curKind,
                            Data = curData,
                            Existed = valExisted
                        });
                    }
                }
                else if (action.Type.Equals("Service", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrEmpty(action.ServiceName))
                    {
                        bool exists = _services.ServiceExists(action.ServiceName);
                        string startup = exists ? _services.GetStartupType(action.ServiceName) : "NotFound";
                        string state = exists ? _services.GetState(action.ServiceName) : "NotFound";

                        metadata.Items.Add(new BackupSnapshotItem
                        {
                            Type = "Service",
                            Target = action.ServiceName,
                            ValueKind = startup,
                            Data = state,
                            Existed = exists
                        });
                    }
                }
            }
        }

        // Export real .reg files for existing registry keys
        foreach (var (hive, key) in touchedKeys)
        {
            if (_registry.KeyExists(hive, key))
            {
                try
                {
                    string exported = _registry.ExportRegBackup(hive, key, regDir);
                    if (File.Exists(exported))
                    {
                        metadata.RegFiles.Add(Path.GetFileName(exported));
                    }
                }
                catch { }
            }
        }

        string jsonPath = Path.Combine(backupDir, "snapshot.json");
        File.WriteAllText(jsonPath, JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));

        return Task.FromResult(backupDir);
    }

    public Task<RestorePointResult> CreateSystemRestorePointAsync(string description)
    {
        return Task.Run(() => _restoreService.CreateRestorePoint(description));
    }

    public IReadOnlyList<BackupSnapshotInfo> GetAvailableBackups()
    {
        var list = new List<BackupSnapshotInfo>();
        if (!Directory.Exists(_backupsRoot)) return list;

        foreach (var dir in Directory.GetDirectories(_backupsRoot))
        {
            string jsonPath = Path.Combine(dir, "snapshot.json");
            if (File.Exists(jsonPath))
            {
                try
                {
                    var meta = JsonSerializer.Deserialize<BackupSnapshotMetadata>(File.ReadAllText(jsonPath));
                    if (meta != null)
                    {
                        list.Add(new BackupSnapshotInfo
                        {
                            Id = meta.Id,
                            CreatedAt = meta.CreatedAtUtc.ToLocalTime(),
                            DirectoryPath = dir,
                            Description = meta.Description,
                            ItemCount = meta.Items.Count,
                            RegFileCount = meta.RegFiles.Count
                        });
                    }
                }
                catch { }
            }
        }

        return list.OrderByDescending(b => b.CreatedAt).ToList();
    }

    public Task<bool> RestoreBackupAsync(string backupId, Action<string>? onProgress = null)
    {
        return Task.Run(() =>
        {
            string dir = Path.Combine(_backupsRoot, backupId);
            if (!Directory.Exists(dir))
            {
                dir = backupId;
                if (!Directory.Exists(dir)) return false;
            }

            string jsonPath = Path.Combine(dir, "snapshot.json");
            if (!File.Exists(jsonPath)) return false;

            var meta = JsonSerializer.Deserialize<BackupSnapshotMetadata>(File.ReadAllText(jsonPath));
            if (meta == null) return false;

            onProgress?.Invoke($"Restoring backup snapshot {meta.Id} ({meta.Items.Count} items)...");
            bool succeeded = true;

            // 1. Restore registry items
            foreach (var item in meta.Items.Where(i => i.Type == "Registry"))
            {
                string[] parts = item.Target.Split('\\', 2);
                if (parts.Length < 2) { succeeded = false; continue; }
                string hive = parts[0];
                string key = parts[1];

                try
                {
                    if (!item.Existed)
                    {
                        if (_registry.ValueExists(hive, key, item.ValueName ?? string.Empty))
                        {
                            _registry.DeleteValue(hive, key, item.ValueName ?? string.Empty);
                            onProgress?.Invoke($"[OK] Removed created value {hive}\\{key}\\{item.ValueName}");
                        }
                    }
                    else if (item.Data != null)
                    {
                        if (!_registry.KeyExists(hive, key)) _registry.CreateKey(hive, key);
                        string? serialized = item.Data is JsonElement json
                            ? json.ValueKind == JsonValueKind.String ? json.GetString() : json.GetRawText()
                            : RegistryActionHandler.SerializeData(item.Data);
                        object data = RegistryActionHandler.DeserializeData(serialized, item.ValueKind ?? "DWord");
                        _registry.SetValue(hive, key, item.ValueName ?? string.Empty, data, item.ValueKind ?? "DWord");
                        onProgress?.Invoke($"[OK] Restored {hive}\\{key}\\{item.ValueName} = {item.Data}");
                    }
                }
                catch (Exception ex)
                {
                    succeeded = false;
                    onProgress?.Invoke($"[WARN] Could not restore {item.Target}: {ex.Message}");
                }
            }

            // 2. Restore services
            foreach (var item in meta.Items.Where(i => i.Type == "Service"))
            {
                if (item.Existed && !string.IsNullOrEmpty(item.ValueKind) && item.ValueKind != "NotFound")
                {
                    try
                    {
                        _services.SetStartupType(item.Target, item.ValueKind);
                        string? state = item.Data is JsonElement json ? json.GetString() : item.Data?.ToString();
                        if (state == "Running") _services.StartService(item.Target);
                        else if (state == "Stopped") _services.StopService(item.Target);
                        onProgress?.Invoke($"[OK] Restored service {item.Target} startup to {item.ValueKind}");
                    }
                    catch (Exception ex)
                    {
                        succeeded = false;
                        onProgress?.Invoke($"[WARN] Could not restore service {item.Target}: {ex.Message}");
                    }
                }
            }

            onProgress?.Invoke(succeeded ? $"[OK] Snapshot {meta.Id} restored successfully." : $"[WARN] Snapshot {meta.Id} was only partially restored.");
            return succeeded;
        });
    }
}
