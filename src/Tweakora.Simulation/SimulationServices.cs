using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Tweakora.Core.Interfaces;

namespace Tweakora.Simulation;

public sealed class FakeRegistryService : IRegistryService
{
    public Dictionary<string, Dictionary<string, (object Data, string Kind)>> Keys { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool KeyExists(string hive, string key)
    {
        string fullKey = $"{hive}\\{key}";
        return Keys.ContainsKey(fullKey);
    }

    public bool ValueExists(string hive, string key, string valueName)
    {
        string fullKey = $"{hive}\\{key}";
        return Keys.TryGetValue(fullKey, out var vals) && vals.ContainsKey(valueName);
    }

    public object? GetValue(string hive, string key, string valueName)
    {
        string fullKey = $"{hive}\\{key}";
        if (Keys.TryGetValue(fullKey, out var vals) && vals.TryGetValue(valueName, out var v))
        {
            return v.Data;
        }
        return null;
    }

    public string? GetValueKind(string hive, string key, string valueName)
    {
        string fullKey = $"{hive}\\{key}";
        if (Keys.TryGetValue(fullKey, out var vals) && vals.TryGetValue(valueName, out var v))
        {
            return v.Kind;
        }
        return null;
    }

    public void SetValue(string hive, string key, string valueName, object data, string valueKind)
    {
        string fullKey = $"{hive}\\{key}";
        if (!Keys.TryGetValue(fullKey, out var vals))
        {
            vals = new Dictionary<string, (object, string)>(StringComparer.OrdinalIgnoreCase);
            Keys[fullKey] = vals;
        }
        vals[valueName] = (data, valueKind);
    }

    public void DeleteValue(string hive, string key, string valueName)
    {
        string fullKey = $"{hive}\\{key}";
        if (Keys.TryGetValue(fullKey, out var vals))
        {
            vals.Remove(valueName);
        }
    }

    public void DeleteKey(string hive, string key)
    {
        string fullKey = $"{hive}\\{key}";
        Keys.Remove(fullKey);
    }

    public void CreateKey(string hive, string key)
    {
        string fullKey = $"{hive}\\{key}";
        if (!Keys.ContainsKey(fullKey))
        {
            Keys[fullKey] = new Dictionary<string, (object, string)>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public string ExportRegBackup(string hive, string key, string backupFolder)
    {
        return $"fake-backup-{hive}-{key.Replace('\\', '_')}.reg";
    }

    public IEnumerable<string> GetValueNames(string hive, string key)
    {
        string fullKey = $"{hive}\\{key}";
        return Keys.TryGetValue(fullKey, out var vals) ? vals.Keys.ToList() : Enumerable.Empty<string>();
    }

    public IEnumerable<string> GetSubKeyNames(string hive, string key)
    {
        string prefix = $"{hive}\\{key}\\";
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

    public bool ServiceExists(string serviceName) => Services.ContainsKey(serviceName);

    public string GetStartupType(string serviceName) =>
        Services.TryGetValue(serviceName, out var s) ? s.Startup : "Disabled";

    public string GetState(string serviceName) =>
        Services.TryGetValue(serviceName, out var s) ? s.State : "Stopped";

    public void SetStartupType(string serviceName, string startupType)
    {
        if (Services.TryGetValue(serviceName, out var s))
        {
            Services[serviceName] = (startupType, s.State);
        }
    }

    public void StartService(string serviceName)
    {
        if (Services.TryGetValue(serviceName, out var s))
        {
            Services[serviceName] = (s.Startup, "Running");
        }
    }

    public void StopService(string serviceName)
    {
        if (Services.TryGetValue(serviceName, out var s))
        {
            Services[serviceName] = (s.Startup, "Stopped");
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

    public void RegisterTask(string path, string taskName, bool isEnabled = true)
    {
        Tasks[$"{path}\\{taskName}"] = isEnabled;
    }

    public bool TaskExists(string path, string taskName) => Tasks.ContainsKey($"{path}\\{taskName}");

    public bool IsTaskEnabled(string path, string taskName) =>
        Tasks.TryGetValue($"{path}\\{taskName}", out bool enabled) && enabled;

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
