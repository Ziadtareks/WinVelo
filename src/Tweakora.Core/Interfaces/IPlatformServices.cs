using Tweakora.Core.Models;

namespace Tweakora.Core.Interfaces;

public interface IRegistryService
{
    bool KeyExists(string hive, string key);
    bool ValueExists(string hive, string key, string valueName);
    object? GetValue(string hive, string key, string valueName);
    string? GetValueKind(string hive, string key, string valueName);
    void SetValue(string hive, string key, string valueName, object data, string valueKind);
    void DeleteValue(string hive, string key, string valueName);
    void DeleteKey(string hive, string key);
    void CreateKey(string hive, string key);
    string ExportRegBackup(string hive, string key, string backupFolder);
    IEnumerable<string> GetValueNames(string hive, string key);
    IEnumerable<string> GetSubKeyNames(string hive, string key);
}

public interface ISystemServicesService
{
    bool ServiceExists(string serviceName);
    string GetStartupType(string serviceName);
    string GetState(string serviceName);
    void SetStartupType(string serviceName, string startupType);
    void StartService(string serviceName);
    void StopService(string serviceName);
}

public interface ITaskSchedulerService
{
    bool TaskExists(string path, string taskName);
    bool IsTaskEnabled(string path, string taskName);
    void EnableTask(string path, string taskName);
    void DisableTask(string path, string taskName);
    void DeleteTask(string path, string taskName);
}

public sealed class RestorePointResult
{
    public bool Success { get; init; }
    public bool WasThrottledOrSkipped { get; init; }
    public string? ErrorMessage { get; init; }
    public long SequenceNumber { get; init; }
}

public interface ISystemRestoreService
{
    bool IsSystemRestoreEnabled();
    int GetRestorePointCount();
    RestorePointResult CreateRestorePoint(string description);
}

public interface IShellService
{
    void RestartExplorer();
    void RefreshShellEnvironment();
    Task<PowerShellResult> ExecutePowerShellAsync(string script, int timeoutSeconds = 60);
    void OpenUrl(string url);
    void LaunchApplet(string appletCommand);
}
