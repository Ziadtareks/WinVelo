using System.Diagnostics;
using Tweakora.Core.Interfaces;
using Tweakora.Core.Safety;

namespace Tweakora.Platform;

public sealed class WindowsTaskSchedulerService : ITaskSchedulerService
{
    public WindowsTaskSchedulerService()
    {
        EnsureNotTestMode();
    }

    private static void EnsureNotTestMode()
    {
        if (TestModeManager.IsTestMode)
        {
            throw new InvalidOperationException("Real platform class operation blocked in TEST MODE.");
        }
    }

    private static string CombineTaskPath(string path, string taskName)
    {
        string p = path.Trim('\\').Replace('/', '\\');
        return string.IsNullOrEmpty(p) ? $"\\{taskName}" : $"\\{p}\\{taskName}";
    }

    public bool TaskExists(string path, string taskName)
    {
        EnsureNotTestMode();
        string full = CombineTaskPath(path, taskName);
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "schtasks.exe",
            Arguments = $"/query /tn \"{full}\"",
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        });
        process?.WaitForExit(3000);
        return process?.ExitCode == 0;
    }

    public bool IsTaskEnabled(string path, string taskName)
    {
        EnsureNotTestMode();
        string full = CombineTaskPath(path, taskName);
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "schtasks.exe",
            Arguments = $"/query /tn \"{full}\" /v /fo list",
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true
        });
        string output = process?.StandardOutput.ReadToEnd() ?? "";
        process?.WaitForExit(3000);

        return !output.Contains("Status: Disabled", StringComparison.OrdinalIgnoreCase) &&
               !output.Contains("Scheduled Task State: Disabled", StringComparison.OrdinalIgnoreCase);
    }

    public void EnableTask(string path, string taskName)
    {
        EnsureNotTestMode();
        string full = CombineTaskPath(path, taskName);
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "schtasks.exe",
            Arguments = $"/change /tn \"{full}\" /enable",
            CreateNoWindow = true,
            UseShellExecute = false
        });
        process?.WaitForExit(5000);
    }

    public void DisableTask(string path, string taskName)
    {
        EnsureNotTestMode();
        string full = CombineTaskPath(path, taskName);
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "schtasks.exe",
            Arguments = $"/change /tn \"{full}\" /disable",
            CreateNoWindow = true,
            UseShellExecute = false
        });
        process?.WaitForExit(5000);
    }

    public void DeleteTask(string path, string taskName)
    {
        EnsureNotTestMode();
        string full = CombineTaskPath(path, taskName);
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "schtasks.exe",
            Arguments = $"/delete /tn \"{full}\" /f",
            CreateNoWindow = true,
            UseShellExecute = false
        });
        process?.WaitForExit(5000);
    }
}
