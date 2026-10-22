using WinVelo.Core.Interfaces;
using WinVelo.Core.Safety;

namespace WinVelo.Platform;

public sealed class WindowsServicesService : ISystemServicesService
{
    private readonly IShellService _shell;
    public WindowsServicesService() { EnsureNotTestMode(); _shell = new WindowsShellService(); }
    private static void EnsureNotTestMode()
    {
        if (TestModeManager.IsTestMode) throw new InvalidOperationException("Real platform class operation blocked in TEST MODE.");
    }
    private string Checked(string exe, int timeout, params string[] args)
    {
        EnsureNotTestMode();
        return WindowsCommand.RequireSuccess(WindowsCommand.Run(_shell, exe, timeout, args), exe + " " + string.Join(' ', args));
    }
    public bool ServiceExists(string serviceName)
    {
        EnsureNotTestMode();
        var result = WindowsCommand.Run(_shell, "sc.exe", 15, "query", serviceName);
        if (result.ExitCode == 1060) return false;
        WindowsCommand.RequireSuccess(result, "sc query " + serviceName);
        return true;
    }
    public string GetStartupType(string serviceName)
    {
        string output = Checked("sc.exe", 15, "qc", serviceName);
        if (output.Contains("DEMAND_START", StringComparison.OrdinalIgnoreCase)) return "Manual";
        if (output.Contains("AUTO_START", StringComparison.OrdinalIgnoreCase)) return "Automatic";
        if (output.Contains("DISABLED", StringComparison.OrdinalIgnoreCase)) return "Disabled";
        throw new InvalidOperationException("Could not determine service startup type: " + serviceName);
    }
    public string GetState(string serviceName)
    {
        string output = Checked("sc.exe", 15, "query", serviceName);
        if (output.Contains("RUNNING", StringComparison.OrdinalIgnoreCase)) return "Running";
        if (output.Contains("STOPPED", StringComparison.OrdinalIgnoreCase)) return "Stopped";
        return "Unknown";
    }
    public void SetStartupType(string serviceName, string startupType)
    {
        string start = startupType.ToLowerInvariant() switch
        {
            "automatic" => "auto",
            "manual" => "demand",
            "disabled" => "disabled",
            _ => throw new ArgumentException("Unsupported startup type: " + startupType)
        };
        Checked("sc.exe", 30, "config", serviceName, "start=", start);
        if (!string.Equals(GetStartupType(serviceName), startupType, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Windows did not retain the requested startup type: " + serviceName);
    }
    public void StartService(string serviceName)
    {
        if (GetState(serviceName) == "Running") return;
        Checked("net.exe", 90, "start", serviceName);
        if (GetState(serviceName) != "Running") throw new InvalidOperationException("Service did not start: " + serviceName);
    }
    public void StopService(string serviceName)
    {
        if (GetState(serviceName) == "Stopped") return;
        Checked("net.exe", 90, "stop", serviceName, "/y");
        if (GetState(serviceName) != "Stopped") throw new InvalidOperationException("Service did not stop: " + serviceName);
    }
}
