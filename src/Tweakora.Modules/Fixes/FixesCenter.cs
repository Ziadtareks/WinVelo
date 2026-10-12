namespace Tweakora.Modules.Fixes;

public sealed class SystemFix
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required string Category { get; init; }
    public string Severity { get; init; } = "Common";
    public required string Script { get; init; }
}

public sealed class FixesCenter
{
    public bool IsSimulationMode { get; set; }

    public FixesCenter(bool isSimulationMode = true)
    {
        IsSimulationMode = isSimulationMode;
    }

    public List<SystemFix> GetAvailableFixes()
    {
        return new List<SystemFix>
        {
            new()
            {
                Id = "fix.dns.flush",
                Title = "Flush DNS Resolver Cache",
                Description = "Clears the local DNS cache to resolve domain name resolution errors and connection drops.",
                Category = "Network",
                Severity = "Common",
                Script = "ipconfig /flushdns; Clear-DnsClientCache"
            },
            new()
            {
                Id = "fix.network.winsock-reset",
                Title = "Reset TCP/IP & Winsock Stack",
                Description = "Restores network adapter sockets and TCP/IP protocol configurations back to clean defaults.",
                Category = "Network",
                Severity = "Advanced",
                Script = "netsh winsock reset; netsh int ip reset"
            },
            new()
            {
                Id = "fix.updates.reset-cache",
                Title = "Reset Windows Update Download Cache",
                Description = "Stops the update service, cleans downloaded update files, and restarts services to fix stuck downloads.",
                Category = "Windows Update",
                Severity = "Maintenance",
                Script = "Stop-Service -Name wuauserv -Force -ErrorAction SilentlyContinue; Remove-Item -Path \"$env:WINDIR\\SoftwareDistribution\\Download\\*\" -Recurse -Force -ErrorAction SilentlyContinue; Start-Service -Name wuauserv"
            },
            new()
            {
                Id = "fix.shell.rebuild-icon-cache",
                Title = "Rebuild Icon & Thumbnail Cache",
                Description = "Fixes corrupted, blank, or outdated desktop and file icons by rebuilding cache databases.",
                Category = "Shell",
                Severity = "Common",
                Script = "Stop-Process -Name explorer -Force; Remove-Item -Path \"$env:LOCALAPPDATA\\IconCache.db\" -Force -ErrorAction SilentlyContinue; Start-Process explorer"
            },
            new()
            {
                Id = "fix.store.wsreset",
                Title = "Reset Microsoft Store",
                Description = "Clears and resets the Microsoft Store cache without changing account settings or installed apps.",
                Category = "Store",
                Severity = "Common",
                Script = "wsreset.exe -i"
            },
            new()
            {
                Id = "fix.audio.restart-service",
                Title = "Restart Windows Audio Subsystem",
                Description = "Restarts Windows Audio services to resolve sound stuttering or missing audio output devices.",
                Category = "System",
                Severity = "Common",
                Script = "Restart-Service -Name AudioSrv, AudioEndpointBuilder -Force"
            },
            new()
            {
                Id = "fix.system.sfc-dism",
                Title = "System Health Check (SFC & DISM)",
                Description = "Scans system files for corruption and repairs missing or damaged system components.",
                Category = "System",
                Severity = "Advanced",
                Script = "DISM.exe /Online /Cleanup-Image /RestoreHealth; sfc /scannow"
            }
        };
    }

    public async Task<bool> RunFixAsync(SystemFix fix)
    {
        if (IsSimulationMode)
        {
            // Simulation mode: safe execution delay without touching user PC
            await Task.Delay(300);
            return true;
        }

        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{fix.Script}\"",
                UseShellExecute = false,
                CreateNoWindow = true
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
