using System.Diagnostics;
using Tweakora.Core.Interfaces;
using Tweakora.Core.Safety;

namespace Tweakora.Platform;

public sealed class WindowsRestoreService : ISystemRestoreService
{
    public WindowsRestoreService()
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

    public bool IsSystemRestoreEnabled()
    {
        EnsureNotTestMode();
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -NonInteractive -Command \"(Get-ComputerRestorePoint).Count\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true
            });
            process?.WaitForExit(5000);
            return process?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public int GetRestorePointCount()
    {
        EnsureNotTestMode();
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -NonInteractive -Command \"@(Get-ComputerRestorePoint).Count\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true
            });
            string output = process?.StandardOutput.ReadToEnd()?.Trim() ?? "0";
            process?.WaitForExit(5000);
            return int.TryParse(output, out int c) ? c : 0;
        }
        catch
        {
            return 0;
        }
    }

    public DateTime? GetLatestRestorePointDate()
    {
        EnsureNotTestMode();
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -NonInteractive -Command \"(Get-ComputerRestorePoint | Sort-Object CreationTime -Descending | Select-Object -First 1).CreationTime\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true
            });
            string? output = process?.StandardOutput.ReadToEnd()?.Trim();
            process?.WaitForExit(5000);
            if (!string.IsNullOrEmpty(output) && DateTime.TryParse(output, out var dt))
            {
                return dt;
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    public RestorePointResult CreateRestorePoint(string description)
    {
        EnsureNotTestMode();
        try
        {
            int beforeCount = GetRestorePointCount();
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -NonInteractive -Command \"Checkpoint-Computer -Description '{description}' -RestorePointType 'MODIFY_SETTINGS'\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            process?.WaitForExit(30000);
            int afterCount = GetRestorePointCount();

            bool reallyCreated = afterCount > beforeCount;
            return new RestorePointResult
            {
                Success = reallyCreated,
                WasThrottledOrSkipped = !reallyCreated && process?.ExitCode == 0,
                ErrorMessage = reallyCreated ? null : (process?.StandardError.ReadToEnd() ?? "Restore point not created (throttled by Windows 24h limit)"),
                SequenceNumber = afterCount
            };
        }
        catch (Exception ex)
        {
            return new RestorePointResult
            {
                Success = false,
                WasThrottledOrSkipped = false,
                ErrorMessage = ex.Message,
                SequenceNumber = 0
            };
        }
    }
}
