using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Tweakora.Core.Interfaces;

namespace Tweakora.Simulation;

public sealed class FakeRegistryService : IRegistryService
{
    public Dictionary<string, Dictionary<string, (object Data, string Kind)>> Keys { get; } = new(StringComparer.OrdinalIgnoreCase);

    private static string GetFullKey(string hive, string key)
    {
        var (h, k) = Tweakora.Core.Utilities.RegistryPathHelper.Normalize(hive, key);
        return $"{h}\\{k}";
    }

    public bool KeyExists(string hive, string key)
    {
        string fullKey = GetFullKey(hive, key);
        return Keys.ContainsKey(fullKey);
    }

    public bool ValueExists(string hive, string key, string valueName)
    {
        string fullKey = GetFullKey(hive, key);
        return Keys.TryGetValue(fullKey, out var vals) && vals.ContainsKey(valueName);
    }

    public object? GetValue(string hive, string key, string valueName)
    {
        string fullKey = GetFullKey(hive, key);
        if (Keys.TryGetValue(fullKey, out var vals) && vals.TryGetValue(valueName, out var v))
        {
            return v.Data;
        }
        return null;
    }

    public string? GetValueKind(string hive, string key, string valueName)
    {
        string fullKey = GetFullKey(hive, key);
        if (Keys.TryGetValue(fullKey, out var vals) && vals.TryGetValue(valueName, out var v))
        {
            return v.Kind;
        }
        return null;
    }

    public void SetValue(string hive, string key, string valueName, object data, string valueKind)
    {
        string fullKey = GetFullKey(hive, key);
        if (!Keys.TryGetValue(fullKey, out var vals))
        {
            vals = new Dictionary<string, (object, string)>(StringComparer.OrdinalIgnoreCase);
            Keys[fullKey] = vals;
        }
        vals[valueName] = (data, valueKind);
    }

    public void DeleteValue(string hive, string key, string valueName)
    {
        string fullKey = GetFullKey(hive, key);
        if (Keys.TryGetValue(fullKey, out var vals))
        {
            vals.Remove(valueName);
        }
    }

    public void DeleteKey(string hive, string key)
    {
        string fullKey = GetFullKey(hive, key);
        Keys.Remove(fullKey);
    }

    public void CreateKey(string hive, string key)
    {
        string fullKey = GetFullKey(hive, key);
        if (!Keys.ContainsKey(fullKey))
        {
            Keys[fullKey] = new Dictionary<string, (object, string)>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public string ExportRegBackup(string hive, string key, string backupFolder)
    {
        var (h, k) = Tweakora.Core.Utilities.RegistryPathHelper.Normalize(hive, key);
        return $"fake-backup-{h}-{k.Replace('\\', '_')}.reg";
    }

    public IEnumerable<string> GetValueNames(string hive, string key)
    {
        string fullKey = GetFullKey(hive, key);
        return Keys.TryGetValue(fullKey, out var vals) ? vals.Keys.ToList() : Enumerable.Empty<string>();
    }

    public IEnumerable<string> GetSubKeyNames(string hive, string key)
    {
        string fullKey = GetFullKey(hive, key);
        string prefix = $"{fullKey}\\";
        return Keys.Keys
            .Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(k => k.Substring(prefix.Length).Split('\\')[0])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

public sealed class FakeServicesService : ISystemServicesService
{
    public Dictionary<string, (string Startup, string State)> Services { get; } = new(StringComparer.OrdinalIgnoreCase);
    public bool AutoRegisterServices { get; set; } = false;

    public FakeServicesService()
    {
        var standardServices = new[]
        {
            "lfsvc", "CscService", "DiagTrack", "MapsBroker", "StorSvc", "SharedAccess",
            "WSearch", "Spooler", "SysMain", "BITS", "wuauserv", "WaaSMedicSvc", "wermgr"
        };
        foreach (var s in standardServices)
        {
            Services[s] = ("Automatic", "Running");
        }
    }

    public bool ServiceExists(string serviceName)
    {
        if (Services.ContainsKey(serviceName)) return true;
        if (AutoRegisterServices)
        {
            Services[serviceName] = ("Automatic", "Running");
            return true;
        }
        return false;
    }

    public string GetStartupType(string serviceName)
    {
        if (!Services.ContainsKey(serviceName) && AutoRegisterServices)
        {
            Services[serviceName] = ("Automatic", "Running");
        }
        return Services.TryGetValue(serviceName, out var s) ? s.Startup : "Automatic";
    }

    public string GetState(string serviceName)
    {
        if (!Services.ContainsKey(serviceName) && AutoRegisterServices)
        {
            Services[serviceName] = ("Automatic", "Running");
        }
        return Services.TryGetValue(serviceName, out var s) ? s.State : "Running";
    }

    public void SetStartupType(string serviceName, string startupType)
    {
        if (Services.TryGetValue(serviceName, out var s))
        {
            Services[serviceName] = (startupType, s.State);
        }
        else
        {
            Services[serviceName] = (startupType, "Running");
        }
    }

    public void StartService(string serviceName)
    {
        if (Services.TryGetValue(serviceName, out var s))
        {
            Services[serviceName] = (s.Startup, "Running");
        }
        else
        {
            Services[serviceName] = ("Automatic", "Running");
        }
    }

    public void StopService(string serviceName)
    {
        if (Services.TryGetValue(serviceName, out var s))
        {
            Services[serviceName] = (s.Startup, "Stopped");
        }
        else
        {
            Services[serviceName] = ("Automatic", "Stopped");
        }
    }

    public void RegisterService(string serviceName, string startup, string state)
    {
        Services[serviceName] = (startup, state);
    }
}

public sealed class FakeTaskSchedulerService : ITaskSchedulerService
{
    public Dictionary<string, bool> Tasks { get; } = new(StringComparer.OrdinalIgnoreCase);
    public bool AutoRegisterTasks { get; set; } = false;

    public FakeTaskSchedulerService()
    {
        var standardTasks = new[]
        {
            (@"\Microsoft\Windows\Customer Experience Improvement Program", "Consolidator"),
            (@"\Microsoft\Windows\Customer Experience Improvement Program", "UsbCeip"),
            (@"\Microsoft\Windows\Application Experience", "Microsoft Compatibility Appraiser"),
            (@"\Microsoft\Windows\Application Experience", "ProgramDataUpdater"),
            (@"\Microsoft\Windows\Autochk", "Proxy"),
            (@"\Microsoft\Windows\DiskDiagnostic", "Microsoft-Windows-DiskDiagnosticDataCollector")
        };
        foreach (var (p, n) in standardTasks)
        {
            RegisterTask(p, n, true);
        }
    }

    public void RegisterTask(string path, string taskName, bool isEnabled = true)
    {
        Tasks[$"{path}\\{taskName}"] = isEnabled;
    }

    public bool TaskExists(string path, string taskName)
    {
        string full = $"{path}\\{taskName}";
        if (Tasks.ContainsKey(full)) return true;
        if (AutoRegisterTasks)
        {
            Tasks[full] = true;
            return true;
        }
        return false;
    }

    public bool IsTaskEnabled(string path, string taskName)
    {
        string full = $"{path}\\{taskName}";
        if (!Tasks.ContainsKey(full) && AutoRegisterTasks)
        {
            Tasks[full] = true;
        }
        return Tasks.TryGetValue(full, out bool enabled) && enabled;
    }

    public void EnableTask(string path, string taskName) => Tasks[$"{path}\\{taskName}"] = true;

    public void DisableTask(string path, string taskName) => Tasks[$"{path}\\{taskName}"] = false;

    public void DeleteTask(string path, string taskName) => Tasks.Remove($"{path}\\{taskName}");
}

public sealed class FakeRestoreService : ISystemRestoreService
{
    public bool IsEnabled { get; set; } = true;
    public bool SimulateThrottled { get; set; } = false;
    public bool SimulateFailure { get; set; } = false;
    public int RestorePointsCount { get; set; } = 3;

    public bool IsSystemRestoreEnabled() => IsEnabled;

    public int GetRestorePointCount() => RestorePointsCount;

    public RestorePointResult CreateRestorePoint(string description)
    {
        if (!IsEnabled || SimulateFailure)
        {
            return new RestorePointResult { Success = false, ErrorMessage = "Restore disabled or error occurred." };
        }

        if (SimulateThrottled)
        {
            return new RestorePointResult { Success = false, WasThrottledOrSkipped = true };
        }

        RestorePointsCount++;
        return new RestorePointResult { Success = true, SequenceNumber = RestorePointsCount };
    }
}

public sealed class FakeShellService : IShellService
{
    public bool ExplorerRestarted { get; private set; }
    public bool EnvironmentRefreshed { get; private set; }
    public List<string> ExecutedScripts { get; } = new();
    public List<string> OpenedUrls { get; } = new();
    public List<string> LaunchedApplets { get; } = new();

    public void RestartExplorer() => ExplorerRestarted = true;
    public void RefreshShellEnvironment() => EnvironmentRefreshed = true;

    public Task<string> ExecutePowerShellAsync(string script, int timeoutSeconds = 60)
    {
        ExecutedScripts.Add(script);
        return Task.FromResult($"Simulated execution of script ({script.Length} chars)");
    }

    public void OpenUrl(string url)
    {
        OpenedUrls.Add(url);
    }

    public void LaunchApplet(string appletCommand)
    {
        LaunchedApplets.Add(appletCommand);
    }
}
