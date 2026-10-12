using Tweakora.Core.Interfaces;

namespace Tweakora.Modules.Uninstaller;

public sealed class InstalledAppItem
{
    public required string Name { get; init; }
    public string Publisher { get; init; } = "Unknown";
    public string Version { get; init; } = string.Empty;
    public string InstallDate { get; init; } = string.Empty;
    public string EstimatedSize { get; init; } = string.Empty;
    public string UninstallString { get; init; } = string.Empty;
    public bool IsStoreApp { get; init; }
}

public sealed class AppUninstaller
{
    private readonly IRegistryService _registry;
    public bool IsSimulationMode { get; set; }

    public AppUninstaller(IRegistryService registry, bool isSimulationMode = true)
    {
        _registry = registry;
        IsSimulationMode = isSimulationMode;
    }

    public List<InstalledAppItem> GetInstalledApps()
    {
        List<InstalledAppItem> apps = new();

        if (_registry is Tweakora.Core.Fakes.FakeRegistryService || IsSimulationMode)
        {
            // Standard simulated installed list for safe testing
            apps.Add(new InstalledAppItem
            {
                Name = "Mozilla Firefox (x64 en-US)",
                Publisher = "Mozilla",
                Version = "135.0.1",
                InstallDate = "2026-03-10",
                EstimatedSize = "220 MB",
                UninstallString = "C:\\Program Files\\Mozilla Firefox\\uninstall\\helper.exe"
            });
            apps.Add(new InstalledAppItem
            {
                Name = "Visual Studio Code",
                Publisher = "Microsoft Corporation",
                Version = "1.98.0",
                InstallDate = "2026-02-15",
                EstimatedSize = "350 MB",
                UninstallString = "C:\\Users\\User\\AppData\\Local\\Programs\\Microsoft VS Code\\unins000.exe"
            });
            apps.Add(new InstalledAppItem
            {
                Name = "7-Zip 24.09 (x64)",
                Publisher = "Igor Pavlov",
                Version = "24.09",
                InstallDate = "2026-01-20",
                EstimatedSize = "5.2 MB",
                UninstallString = "C:\\Program Files\\7-Zip\\Uninstall.exe"
            });
            apps.Add(new InstalledAppItem
            {
                Name = "Git",
                Publisher = "The Git Development Community",
                Version = "2.48.1",
                InstallDate = "2026-02-01",
                EstimatedSize = "310 MB",
                UninstallString = "C:\\Program Files\\Git\\unins000.exe"
            });
            apps.Add(new InstalledAppItem
            {
                Name = "Microsoft Teams",
                Publisher = "Microsoft Corporation",
                Version = "24300.0",
                InstallDate = "2026-03-01",
                EstimatedSize = "180 MB",
                UninstallString = "ms-settings:appsfeatures",
                IsStoreApp = true
            });
            return apps;
        }

        return apps;
    }

    public async Task<bool> UninstallAppAsync(InstalledAppItem app)
    {
        if (IsSimulationMode)
        {
            // Simulation: safe delay without touching machine
            await Task.Delay(200);
            return true;
        }

        if (string.IsNullOrWhiteSpace(app.UninstallString))
        {
            return false;
        }

        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c {app.UninstallString}",
                UseShellExecute = true
            };
            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc != null)
            {
                await proc.WaitForExitAsync();
                return proc.ExitCode == 0;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }
}
