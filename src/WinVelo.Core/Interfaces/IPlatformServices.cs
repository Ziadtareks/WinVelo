using WinVelo.Core.Models;

namespace WinVelo.Core.Interfaces;

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
    string? ExportTaskDefinition(string path, string taskName) => null;
    void RegisterTaskDefinition(string path, string taskName, string xml) => throw new NotSupportedException("Restoring task definitions is not supported by this provider.");
}

public sealed class RestorePointResult
{
    public bool Success { get; init; }
    public bool WasThrottledOrSkipped { get; init; }
    public string? ErrorMessage { get; init; }
    public long SequenceNumber { get; init; }
}

public sealed class RestorePointItem
{
    public long SequenceNumber { get; init; }
    public string Description { get; init; } = string.Empty;
    public DateTime? CreationTime { get; init; }
    public string CreationTimeDisplay => CreationTime?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "Unknown";
    public string RestorePointType { get; init; } = "MODIFY_SETTINGS";
}

public interface ISystemRestoreService
{
    bool IsSystemRestoreEnabled();
    bool EnableSystemRestore(string drive = "C:\\");
    int GetRestorePointCount();
    DateTime? GetLatestRestorePointDate();
    RestorePointResult CreateRestorePoint(string description);
    IReadOnlyList<RestorePointItem> GetRestorePoints();
}

public interface IShellService
{
    bool SupportsSystemQueries => true;
    void RestartExplorer();
    void RefreshShellEnvironment();
    Task<PowerShellResult> ExecutePowerShellAsync(string script, int timeoutSeconds = 60);
    Task<PowerShellResult> ExecutePowerShellAsync(string script, int timeoutSeconds, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ExecutePowerShellAsync(script, timeoutSeconds);
    }

    /// <summary>
    /// Runs a program with each argument passed separately (no shell, no command string), so values such as
    /// package ids can never be interpreted as commands. Cancelling kills the process tree.
    /// </summary>
    Task<PowerShellResult> RunProcessAsync(string fileName, IReadOnlyList<string> arguments, int timeoutSeconds = 60, CancellationToken cancellationToken = default);

    /// <summary>Opens an http(s) link in the default browser. Anything else is ignored.</summary>
    void OpenUrl(string url);
    void LaunchApplet(string appletCommand);
}
