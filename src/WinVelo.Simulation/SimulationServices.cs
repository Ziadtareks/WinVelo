using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WinVelo.Core.Interfaces;
using WinVelo.Core.Models;

namespace WinVelo.Simulation;

public sealed class FakeRegistryService : IRegistryService
{
    public Dictionary<string, Dictionary<string, (object Data, string Kind)>> Keys { get; } = new(StringComparer.OrdinalIgnoreCase);

    public FakeRegistryService()
    {
        SeedDefaultStartupApps();
    }

    public void SeedDefaultStartupApps()
    {
        byte[] enabledBytes = new byte[] { 0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        byte[] disabledBytes = new byte[] { 0x03, 0, 0, 0, 0x50, 0x4B, 0x1A, 0x6E, 0x9B, 0xDB, 0xD9, 0x01 };

        // 1. HKCU Run
        SetValue("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Run", "Discord", "\"C:\\Users\\Test\\AppData\\Local\\Discord\\app-1.0.9000\\Discord.exe\" --processStart Discord.exe", "String");
        SetValue("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Run", "Steam", "\"C:\\Program Files (x86)\\Steam\\steam.exe\" -silent", "String");
        SetValue("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Run", "Spotify", "\"C:\\Users\\Test\\AppData\\Roaming\\Spotify\\Spotify.exe\"", "String");
        SetValue("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Run", "Telegram", "\"C:\\Users\\Test\\AppData\\Roaming\\Telegram Desktop\\Telegram.exe\" -autostart", "String");

        // Seed StartupApproved for HKCU Run
        SetValue("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run", "Discord", enabledBytes, "Binary");
        SetValue("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run", "Steam", enabledBytes, "Binary");
        SetValue("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run", "Spotify", enabledBytes, "Binary");
        SetValue("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run", "Telegram", disabledBytes, "Binary");

        // 2. HKLM Run (64-bit)
        SetValue("HKLM", @"Software\Microsoft\Windows\CurrentVersion\Run", "SecurityHealth", "%windir%\\system32\\SecurityHealthSystray.exe", "String");
        SetValue("HKLM", @"Software\Microsoft\Windows\CurrentVersion\Run", "RtkAudUService", "\"C:\\Program Files\\Realtek\\Audio\\HDA\\RtkAudUService64.exe\" -background", "String");
        SetValue("HKLM", @"Software\Microsoft\Windows\CurrentVersion\Run", "OneDrive", "\"C:\\Program Files\\Microsoft OneDrive\\OneDrive.exe\" /background", "String");

        SetValue("HKLM", @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run", "SecurityHealth", enabledBytes, "Binary");
        SetValue("HKLM", @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run", "RtkAudUService", enabledBytes, "Binary");
        SetValue("HKLM", @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run", "OneDrive", enabledBytes, "Binary");

        // 3. HKLM WOW6432Node Run (32-bit)
        SetValue("HKLM", @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", "Adobe Creative Cloud", "\"C:\\Program Files (x86)\\Adobe\\Adobe Creative Cloud\\ACC\\Creative Cloud.exe\" --showwindow=false", "String");
        SetValue("HKLM", @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32", "Adobe Creative Cloud", enabledBytes, "Binary");

        // 4. User Startup Folder (tracked via StartupApproved)
        SetValue("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder", "Microsoft Teams.lnk", enabledBytes, "Binary");

        // 5. Common Startup Folder (tracked via StartupApproved)
        SetValue("HKLM", @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder", "7-Zip Shell Helper.lnk", enabledBytes, "Binary");
    }

    private static string GetFullKey(string hive, string key)
    {
        var (h, k) = WinVelo.Core.Utilities.RegistryPathHelper.Normalize(hive, key);
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
        // Same as the real registry (DeleteSubKeyTree): the key and everything below it.
        string fullKey = GetFullKey(hive, key);
        string prefix = fullKey + "\\";
        foreach (string k in Keys.Keys.Where(k => string.Equals(k, fullKey, StringComparison.OrdinalIgnoreCase) || k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            Keys.Remove(k);
        }
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
        var (h, k) = WinVelo.Core.Utilities.RegistryPathHelper.Normalize(hive, key);
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
    public DateTime? LatestRestorePointDate { get; set; } = new DateTime(2026, 10, 14, 12, 0, 0);

    public List<RestorePointItem> FakeRestorePoints { get; } = new()
    {
        new RestorePointItem
        {
            SequenceNumber = 101,
            Description = "Windows Update Checkpoint",
            CreationTime = new DateTime(2026, 10, 12, 10, 30, 0),
            RestorePointType = "APPLICATION_INSTALL"
        },
        new RestorePointItem
        {
            SequenceNumber = 102,
            Description = "Driver Installation Checkpoint",
            CreationTime = new DateTime(2026, 10, 13, 15, 45, 0),
            RestorePointType = "DEVICE_DRIVER_INSTALL"
        },
        new RestorePointItem
        {
            SequenceNumber = 103,
            Description = "WinVelo Baseline Checkpoint",
            CreationTime = new DateTime(2026, 10, 14, 12, 0, 0),
            RestorePointType = "MODIFY_SETTINGS"
        }
    };

    public bool IsSystemRestoreEnabled() => IsEnabled;

    public bool EnableSystemRestore(string drive = "C:\\")
    {
        IsEnabled = true;
        return true;
    }

    public int GetRestorePointCount() => FakeRestorePoints.Count;

    public DateTime? GetLatestRestorePointDate() => FakeRestorePoints.OrderByDescending(r => r.CreationTime).FirstOrDefault()?.CreationTime ?? LatestRestorePointDate;

    public IReadOnlyList<RestorePointItem> GetRestorePoints() => FakeRestorePoints.OrderByDescending(r => r.CreationTime ?? DateTime.MinValue).ToList();

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
        var newPoint = new RestorePointItem
        {
            SequenceNumber = RestorePointsCount + 100,
            Description = description,
            CreationTime = DateTime.UtcNow,
            RestorePointType = "MODIFY_SETTINGS"
        };
        FakeRestorePoints.Add(newPoint);
        LatestRestorePointDate = newPoint.CreationTime;

        return new RestorePointResult { Success = true, SequenceNumber = newPoint.SequenceNumber };
    }
}

public sealed class FakeShellService : IShellService
{
    public bool SupportsSystemQueries => false;
    public bool ExplorerRestarted { get; private set; }
    public bool EnvironmentRefreshed { get; private set; }
    public List<string> ExecutedScripts { get; } = new();
    public List<string> OpenedUrls { get; } = new();
    public List<string> LaunchedApplets { get; } = new();
    public Func<string, int, PowerShellResult>? ScriptResultFactory { get; set; }

    public void RestartExplorer() => ExplorerRestarted = true;
    public void RefreshShellEnvironment() => EnvironmentRefreshed = true;

    public Task<PowerShellResult> ExecutePowerShellAsync(string script, int timeoutSeconds = 60)
    {
        ExecutedScripts.Add(script);
        if (ScriptResultFactory != null)
        {
            return Task.FromResult(ScriptResultFactory(script, timeoutSeconds));
        }

        return Task.FromResult(new PowerShellResult
        {
            ExitCode = 0,
            StdOut = $"Simulated execution of script ({script.Length} chars)",
            StdErr = string.Empty,
            TimedOut = false,
            Duration = TimeSpan.FromMilliseconds(10)
        });
    }

    /// <summary>Every program run through <see cref="RunProcessAsync"/>: the file name followed by its arguments.</summary>
    public List<IReadOnlyList<string>> ExecutedProcesses { get; } = new();
    public Func<string, IReadOnlyList<string>, PowerShellResult>? ProcessResultFactory { get; set; }

    public Task<PowerShellResult> RunProcessAsync(string fileName, IReadOnlyList<string> arguments, int timeoutSeconds = 60, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ExecutedProcesses.Add(new[] { fileName }.Concat(arguments).ToList());
        if (ProcessResultFactory != null)
        {
            return Task.FromResult(ProcessResultFactory(fileName, arguments));
        }

        return Task.FromResult(new PowerShellResult
        {
            ExitCode = 0,
            StdOut = $"Simulated run of {fileName}",
            StdErr = string.Empty,
            TimedOut = false,
            Duration = TimeSpan.FromMilliseconds(10)
        });
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

public sealed class FakeAppSettingsService : IAppSettingsService
{
    public UserSettings Settings { get; set; } = new();
    public UserSettings GetSettings() => Settings;
    public void SaveSettings(UserSettings settings) => Settings = settings;
}
