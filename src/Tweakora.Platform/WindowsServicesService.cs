using System.Diagnostics;
using Tweakora.Core.Interfaces;
using Tweakora.Core.Safety;

namespace Tweakora.Platform;

public sealed class WindowsServicesService : ISystemServicesService
{
    public WindowsServicesService()
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

    public bool ServiceExists(string serviceName)
    {
        EnsureNotTestMode();
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "sc.exe",
            Arguments = $"query \"{serviceName}\"",
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true
        });
        process?.WaitForExit(3000);
        return process?.ExitCode == 0;
    }

    public string GetStartupType(string serviceName)
    {
        EnsureNotTestMode();
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "sc.exe",
            Arguments = $"qc \"{serviceName}\"",
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true
        });
        string output = process?.StandardOutput.ReadToEnd() ?? "";
        process?.WaitForExit(3000);

        if (output.Contains("DEMAND_START", StringComparison.OrdinalIgnoreCase)) return "Manual";
        if (output.Contains("AUTO_START", StringComparison.OrdinalIgnoreCase)) return "Automatic";
        if (output.Contains("DISABLED", StringComparison.OrdinalIgnoreCase)) return "Disabled";
        return "Manual";
    }

    public string GetState(string serviceName)
    {
        EnsureNotTestMode();
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "sc.exe",
            Arguments = $"query \"{serviceName}\"",
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true
        });
        string output = process?.StandardOutput.ReadToEnd() ?? "";
        process?.WaitForExit(3000);

        if (output.Contains("RUNNING", StringComparison.OrdinalIgnoreCase)) return "Running";
        if (output.Contains("STOPPED", StringComparison.OrdinalIgnoreCase)) return "Stopped";
        return "Unknown";
    }

    public void SetStartupType(string serviceName, string startupType)
    {
        EnsureNotTestMode();
        string scStart = startupType.ToLowerInvariant() switch
        {
            "automatic" => "auto",
            "manual" => "demand",
            "disabled" => "disabled",
            _ => "demand"
        };

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "sc.exe",
            Arguments = $"config \"{serviceName}\" start= {scStart}",
            CreateNoWindow = true,
            UseShellExecute = false
        });
        process?.WaitForExit(5000);
    }

    public void StartService(string serviceName)
    {
        EnsureNotTestMode();
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "net.exe",
            Arguments = $"start \"{serviceName}\"",
            CreateNoWindow = true,
            UseShellExecute = false
        });
        process?.WaitForExit(10000);
    }

    public void StopService(string serviceName)
    {
        EnsureNotTestMode();
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "net.exe",
            Arguments = $"stop \"{serviceName}\" /y",
            CreateNoWindow = true,
            UseShellExecute = false
        });
        process?.WaitForExit(10000);
    }
}
