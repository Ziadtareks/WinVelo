using System.Diagnostics;
using WinVelo.Core.Interfaces;
using WinVelo.Core.Safety;

namespace WinVelo.Platform;

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

    public bool EnableSystemRestore(string drive = "C:\\")
    {
        EnsureNotTestMode();
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -NonInteractive -Command \"Enable-ComputerRestore -Drive '{drive}'\"",
                CreateNoWindow = true,
                UseShellExecute = false
            });
            process?.WaitForExit(15000);
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

            // The description is a PowerShell single-quoted literal: '' is the only escape it needs.
            string safeDescription = (description ?? "WinVelo").Replace("'", "''").Replace("\r", " ").Replace("\n", " ");
            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-NonInteractive");
            startInfo.ArgumentList.Add("-Command");
            startInfo.ArgumentList.Add($"Checkpoint-Computer -Description '{safeDescription}' -RestorePointType 'MODIFY_SETTINGS'");

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                return new RestorePointResult { Success = false, ErrorMessage = "PowerShell could not be started.", SequenceNumber = beforeCount };
            }

            // Read both streams while waiting, so a full output pipe can never block the process.
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();

            // Creating a restore point can take a few minutes on a busy disk.
            if (!process.WaitForExit(TimeSpan.FromMinutes(5)))
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                return new RestorePointResult
                {
                    Success = false,
                    ErrorMessage = "Windows did not finish creating the restore point within 5 minutes.",
                    SequenceNumber = beforeCount
                };
            }

            string error = stderr.GetAwaiter().GetResult().Trim();
            _ = stdout.GetAwaiter().GetResult();
            int afterCount = GetRestorePointCount();
            bool reallyCreated = afterCount > beforeCount;

            // Exit code 0 without a new point: Windows skipped it because one was created in the last 24 hours
            // (SystemRestorePointCreationFrequency). That is not an error; the newest point still protects the PC.
            bool throttled = !reallyCreated && process.ExitCode == 0;
            return new RestorePointResult
            {
                Success = reallyCreated,
                WasThrottledOrSkipped = throttled,
                ErrorMessage = reallyCreated ? null
                    : throttled ? "Windows creates at most one restore point every 24 hours; the most recent one is kept."
                    : (string.IsNullOrWhiteSpace(error) ? $"Restore point not created (exit code {process.ExitCode})." : error),
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

    public IReadOnlyList<RestorePointItem> GetRestorePoints()
    {
        EnsureNotTestMode();
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -NonInteractive -Command \"@(Get-ComputerRestorePoint | Select-Object SequenceNumber, Description, @{Name='CreationTimeString';Expression={$_.CreationTime.ToString('o')}}, RestorePointType) | ConvertTo-Json -Compress\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true
            });
            string output = process?.StandardOutput.ReadToEnd()?.Trim() ?? string.Empty;
            process?.WaitForExit(5000);

            if (string.IsNullOrWhiteSpace(output) || output == "null")
            {
                return Array.Empty<RestorePointItem>();
            }

            using var doc = System.Text.Json.JsonDocument.Parse(output);
            var list = new List<RestorePointItem>();

            if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    list.Add(ParseRestorePointElement(el));
                }
            }
            else if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                list.Add(ParseRestorePointElement(doc.RootElement));
            }

            return list.OrderByDescending(r => r.CreationTime ?? DateTime.MinValue).ToList();
        }
        catch
        {
            return Array.Empty<RestorePointItem>();
        }
    }

    private static RestorePointItem ParseRestorePointElement(System.Text.Json.JsonElement el)
    {
        long seq = 0;
        if (el.TryGetProperty("SequenceNumber", out var seqProp) && seqProp.TryGetInt64(out var s))
        {
            seq = s;
        }

        string desc = el.TryGetProperty("Description", out var descProp) ? (descProp.GetString() ?? string.Empty) : string.Empty;
        string type = el.TryGetProperty("RestorePointType", out var typeProp) ? (typeProp.GetString() ?? "MODIFY_SETTINGS") : "MODIFY_SETTINGS";

        DateTime? dt = null;
        if (el.TryGetProperty("CreationTimeString", out var dtProp) && DateTime.TryParse(dtProp.GetString(), out var parsedDt))
        {
            dt = parsedDt;
        }

        return new RestorePointItem
        {
            SequenceNumber = seq,
            Description = desc,
            CreationTime = dt,
            RestorePointType = type
        };
    }
}
