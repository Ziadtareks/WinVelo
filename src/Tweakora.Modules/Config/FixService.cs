using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Tweakora.Core.Interfaces;
using Tweakora.Core.Operations;

namespace Tweakora.Modules.Config;

public sealed class SystemFixDefinition
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required string Category { get; init; }
    public string Severity { get; init; } = "Common";
    public required string Script { get; init; }
}

public sealed class LegacyAppletDefinition
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    public required string Command { get; init; }
    public string Icon { get; init; } = "Apps24";
}

public interface IFixService
{
    IReadOnlyList<SystemFixDefinition> GetAvailableFixes();
    IReadOnlyList<LegacyAppletDefinition> GetLegacyApplets();
    Task<OperationExecutionReport> RunFixAsync(string fixId, IOperationRunner runner, CancellationToken cancellationToken = default);
    void LaunchApplet(string command);
    void OpenUrl(string url);
}

public sealed class FixService : IFixService
{
    private readonly IShellService _shell;

    public FixService(IShellService shell)
    {
        _shell = shell;
    }

    public IReadOnlyList<SystemFixDefinition> GetAvailableFixes()
    {
        return new List<SystemFixDefinition>
        {
            new()
            {
                Id = "dns_flush",
                Title = "Flush DNS Resolver Cache",
                Description = "Clears the local DNS cache to resolve domain name resolution errors and connection drops.",
                Category = "Network",
                Severity = "Common",
                Script = "ipconfig /flushdns; Clear-DnsClientCache"
            },
            new()
            {
                Id = "network_reset",
                Title = "Reset TCP/IP & Winsock Stack",
                Description = "Restores network adapter sockets and TCP/IP protocol configurations back to clean defaults.",
                Category = "Network",
                Severity = "Advanced",
                Script = "netsh winsock reset; netsh int ip reset; ipconfig /flushdns"
            },
            new()
            {
                Id = "update_reset",
                Title = "Reset Windows Update Components",
                Description = "Stops update services, cleans downloaded update files, and restarts services to fix stuck downloads.",
                Category = "Windows Update",
                Severity = "Maintenance",
                Script = "Stop-Service -Name wuauserv, bits, cryptsvc -Force -ErrorAction SilentlyContinue; Remove-Item -Path \"$env:WINDIR\\SoftwareDistribution\\Download\\*\" -Recurse -Force -ErrorAction SilentlyContinue; Start-Service -Name wuauserv, bits, cryptsvc"
            },
            new()
            {
                Id = "dism_sfc",
                Title = "System Health Check (SFC & DISM)",
                Description = "Scans system files for corruption and repairs missing or damaged system components.",
                Category = "System",
                Severity = "Advanced",
                Script = "sfc /scannow; DISM /Online /Cleanup-Image /RestoreHealth"
            },
            new()
            {
                Id = "winget_reinstall",
                Title = "Re-register WinGet Package Manager",
                Description = "Re-registers the Microsoft App Installer package to repair corrupted WinGet CLI installations.",
                Category = "System",
                Severity = "Common",
                Script = "Add-AppxPackage -RegisterByFamilyName -MainPackage Microsoft.DesktopAppInstaller_8wekyb3d8bbwe"
            },
            new()
            {
                Id = "sync_time",
                Title = "Synchronize System Clock with NTP",
                Description = "Configures Windows Time to synchronize against pool.ntp.org and updates the clock immediately.",
                Category = "System",
                Severity = "Common",
                Script = "w32tm /config /syncfromflags:manual /manualpeerlist:pool.ntp.org; w32tm /config /update; Restart-Service w32time; w32tm /resync"
            },
            new()
            {
                Id = "shell_rebuild_icon_cache",
                Title = "Rebuild Icon & Thumbnail Cache",
                Description = "Fixes corrupted, blank, or outdated desktop and file icons by rebuilding cache databases.",
                Category = "Shell",
                Severity = "Common",
                Script = "Stop-Process -Name explorer -Force -ErrorAction SilentlyContinue; Remove-Item -Path \"$env:LOCALAPPDATA\\IconCache.db\" -Force -ErrorAction SilentlyContinue; Start-Process explorer"
            },
            new()
            {
                Id = "store_wsreset",
                Title = "Reset Microsoft Store",
                Description = "Clears and resets the Microsoft Store cache without changing account settings or installed apps.",
                Category = "Store",
                Severity = "Common",
                Script = "wsreset.exe -i"
            },
            new()
            {
                Id = "audio_restart",
                Title = "Restart Windows Audio Subsystem",
                Description = "Restarts Windows Audio services to resolve sound stuttering or missing audio output devices.",
                Category = "System",
                Severity = "Common",
                Script = "Restart-Service -Name AudioSrv, AudioEndpointBuilder -Force"
            }
        };
    }

    public IReadOnlyList<LegacyAppletDefinition> GetLegacyApplets()
    {
        return new List<LegacyAppletDefinition>
        {
            new() { Key = "WPFPanelControl", Name = "Control Panel", Command = "control.exe", Icon = "Settings24" },
            new() { Key = "WPFPanelComputer", Name = "Computer Management", Command = "compmgmt.msc", Icon = "Desktop24" },
            new() { Key = "WPFPanelNetwork", Name = "Network Connections", Command = "ncpa.cpl", Icon = "Globe24" },
            new() { Key = "WPFPanelPower", Name = "Power Options", Command = "powercfg.cpl", Icon = "BatteryCharge24" },
            new() { Key = "WPFPanelPrinter", Name = "Printers & Devices", Command = "control printers", Icon = "Print24" },
            new() { Key = "WPFPanelPrograms", Name = "Programs and Features", Command = "appwiz.cpl", Icon = "Apps24" },
            new() { Key = "WPFPanelRegion", Name = "Region Settings", Command = "intl.cpl", Icon = "LocalLanguage24" },
            new() { Key = "WPFPanelSecurity", Name = "Security & Maintenance", Command = "wscui.cpl", Icon = "Shield24" },
            new() { Key = "WPFPanelSound", Name = "Sound Settings", Command = "mmsys.cpl", Icon = "Speaker224" },
            new() { Key = "WPFPanelSystem", Name = "System Properties", Command = "sysdm.cpl", Icon = "Info24" },
            new() { Key = "WPFPanelTimedate", Name = "Time and Date", Command = "timedate.cpl", Icon = "Clock24" },
            new() { Key = "WPFPanelFirewall", Name = "Windows Firewall", Command = "firewall.cpl", Icon = "LockClosed24" },
            new() { Key = "WPFPanelRestore", Name = "System Restore", Command = "rstrui.exe", Icon = "History24" },
            new() { Key = "WPFPanelMouse", Name = "Mouse Properties", Command = "main.cpl", Icon = "Cursor24" }
        };
    }

    public async Task<OperationExecutionReport> RunFixAsync(
        string fixId,
        IOperationRunner runner,
        CancellationToken cancellationToken = default)
    {
        var fixes = GetAvailableFixes();
        var targetFix = fixes.FirstOrDefault(f => f.Id.Equals(fixId, StringComparison.OrdinalIgnoreCase));

        if (targetFix == null)
        {
            return await runner.RunAsync<SystemFixDefinition>(
                $"Execute Fix ({fixId})",
                Array.Empty<SystemFixDefinition>(),
                (item, ct) => Task.FromResult(new OperationItemResult
                {
                    Id = fixId,
                    Name = fixId,
                    Status = OperationItemStatus.Failed,
                    Reason = $"Fix '{fixId}' not found."
                }),
                cancellationToken);
        }

        return await runner.RunAsync(
            $"Execute Fix: {targetFix.Title}",
            new[] { targetFix },
            async (fix, ct) =>
            {
                string output = await _shell.ExecutePowerShellAsync(fix.Script, 300);
                bool success = !output.Contains("Error", StringComparison.OrdinalIgnoreCase);

                return new OperationItemResult
                {
                    Id = fix.Id,
                    Name = fix.Title,
                    Status = success ? OperationItemStatus.Succeeded : OperationItemStatus.Failed,
                    Reason = success ? "Fix executed successfully." : output.Trim()
                };
            },
            cancellationToken);
    }

    public void LaunchApplet(string command)
    {
        _shell.LaunchApplet(command);
    }

    public void OpenUrl(string url)
    {
        _shell.OpenUrl(url);
    }
}
