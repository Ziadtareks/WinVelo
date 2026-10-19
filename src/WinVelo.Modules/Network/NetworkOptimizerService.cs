using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using WinVelo.Core.Interfaces;

namespace WinVelo.Modules.Network;

public sealed class DnsBenchmarkResult
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string PrimaryDns { get; init; }
    public required string SecondaryDns { get; init; }
    public required string Description { get; init; }
    public int AveragePingMs { get; set; } = -1;
    public bool IsFastest { get; set; }
    public bool IsActive { get; set; }
}

public interface INetworkOptimizerService
{
    Task<IReadOnlyList<DnsBenchmarkResult>> BenchmarkDnsProvidersAsync(CancellationToken cancellationToken = default);
    Task<bool> ApplyDnsAsync(string primaryDns, string secondaryDns, CancellationToken cancellationToken = default);
    Task<bool> ResetDnsToDhcpAsync(CancellationToken cancellationToken = default);
    Task<bool> DetectNagleAlgorithmDisabledAsync();
    Task<bool> SetNagleAlgorithmStateAsync(bool disableNagle, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetActiveNetworkAdapterGuidsAsync();
}

public sealed class NetworkOptimizerService : INetworkOptimizerService
{
    private readonly IRegistryService _registry;
    private readonly IShellService _shell;

    public NetworkOptimizerService(IRegistryService registry, IShellService shell)
    {
        _registry = registry;
        _shell = shell;
    }

    public async Task<IReadOnlyList<DnsBenchmarkResult>> BenchmarkDnsProvidersAsync(CancellationToken cancellationToken = default)
    {
        if (WinVelo.Core.Safety.TestModeManager.IsTestMode)
        {
            return new List<DnsBenchmarkResult>
            {
                new() { Id = "cloudflare", Name = "Cloudflare DNS", PrimaryDns = "1.1.1.1", SecondaryDns = "1.0.0.1", Description = "Fastest global anycast network with zero logging.", AveragePingMs = 12, IsFastest = true },
                new() { Id = "google", Name = "Google Public DNS", PrimaryDns = "8.8.8.8", SecondaryDns = "8.8.4.4", Description = "High-availability global DNS infrastructure.", AveragePingMs = 18 },
                new() { Id = "quad9", Name = "Quad9 Security", PrimaryDns = "9.9.9.9", SecondaryDns = "149.112.112.112", Description = "Swiss-based privacy DNS blocking malicious domains.", AveragePingMs = 24 },
                new() { Id = "opendns", Name = "OpenDNS (Cisco)", PrimaryDns = "208.67.222.222", SecondaryDns = "208.67.220.220", Description = "Enterprise-grade filtering with high uptime.", AveragePingMs = 28 },
                new() { Id = "adguard", Name = "AdGuard DNS", PrimaryDns = "94.140.14.14", SecondaryDns = "94.140.15.15", Description = "Blocks advertising servers, trackers, and telemetry.", AveragePingMs = 32 }
            };
        }

        var providers = new List<DnsBenchmarkResult>
        {
            new()
            {
                Id = "cloudflare",
                Name = "Cloudflare DNS",
                PrimaryDns = "1.1.1.1",
                SecondaryDns = "1.0.0.1",
                Description = "Fastest global anycast network with zero logging."
            },
            new()
            {
                Id = "google",
                Name = "Google Public DNS",
                PrimaryDns = "8.8.8.8",
                SecondaryDns = "8.8.4.4",
                Description = "High-availability global DNS infrastructure."
            },
            new()
            {
                Id = "quad9",
                Name = "Quad9 Security",
                PrimaryDns = "9.9.9.9",
                SecondaryDns = "149.112.112.112",
                Description = "Swiss-based privacy DNS blocking malicious domains."
            },
            new()
            {
                Id = "opendns",
                Name = "OpenDNS (Cisco)",
                PrimaryDns = "208.67.222.222",
                SecondaryDns = "208.67.220.220",
                Description = "Enterprise-grade filtering with high uptime."
            },
            new()
            {
                Id = "adguard",
                Name = "AdGuard DNS",
                PrimaryDns = "94.140.14.14",
                SecondaryDns = "94.140.15.15",
                Description = "Blocks advertising servers, trackers, and telemetry."
            }
        };

        foreach (var p in providers)
        {
            if (cancellationToken.IsCancellationRequested) break;
            p.AveragePingMs = await MeasureAveragePingAsync(p.PrimaryDns, 3, cancellationToken);
        }

        var responsive = providers.Where(p => p.AveragePingMs > 0).ToList();
        if (responsive.Count > 0)
        {
            int minPing = responsive.Min(p => p.AveragePingMs);
            var fastest = responsive.FirstOrDefault(p => p.AveragePingMs == minPing);
            if (fastest != null)
            {
                fastest.IsFastest = true;
            }
        }

        return providers;
    }

    private static async Task<int> MeasureAveragePingAsync(string ip, int attempts, CancellationToken ct)
    {
        int sum = 0;
        int successful = 0;

        using var ping = new Ping();
        for (int i = 0; i < attempts; i++)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                var reply = await ping.SendPingAsync(ip, 800);
                if (reply.Status == IPStatus.Success)
                {
                    sum += (int)reply.RoundtripTime;
                    successful++;
                }
            }
            catch { }
        }

        return successful > 0 ? (int)Math.Round((double)sum / successful) : -1;
    }

    public async Task<bool> ApplyDnsAsync(string primaryDns, string secondaryDns, CancellationToken cancellationToken = default)
    {
        string script;
        if (string.IsNullOrWhiteSpace(secondaryDns))
        {
            script = $"Get-NetAdapter | Where-Object Status -eq 'Up' | Set-DnsClientServerAddress -ServerAddresses '{primaryDns}'";
        }
        else
        {
            script = $"Get-NetAdapter | Where-Object Status -eq 'Up' | Set-DnsClientServerAddress -ServerAddresses '{primaryDns}', '{secondaryDns}'";
        }

        var res = await _shell.ExecutePowerShellAsync(script, 30);
        return res.Success;
    }

    public async Task<bool> ResetDnsToDhcpAsync(CancellationToken cancellationToken = default)
    {
        string script = "Get-NetAdapter | Where-Object Status -eq 'Up' | Set-DnsClientServerAddress -ResetServerAddresses";
        var res = await _shell.ExecutePowerShellAsync(script, 30);
        return res.Success;
    }

    public Task<IReadOnlyList<string>> GetActiveNetworkAdapterGuidsAsync()
    {
        return Task.Run(() =>
        {
            var activeGuids = new List<string>();
            try
            {
                var interfaces = NetworkInterface.GetAllNetworkInterfaces();
                foreach (var nic in interfaces)
                {
                    if (nic.OperationalStatus == OperationalStatus.Up &&
                        nic.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                        nic.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                    {
                        activeGuids.Add(nic.Id);
                    }
                }
            }
            catch { }

            // If empty, fallback to inspecting registry directly
            if (activeGuids.Count == 0)
            {
                try
                {
                    var subkeys = _registry.GetSubKeyNames("HKLM", @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces");
                    foreach (var guid in subkeys)
                    {
                        string path = $@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\{guid}";
                        var ip = _registry.GetValue("HKLM", path, "IPAddress")?.ToString() ??
                                 _registry.GetValue("HKLM", path, "DhcpIPAddress")?.ToString();

                        if (!string.IsNullOrWhiteSpace(ip) && ip != "0.0.0.0")
                        {
                            activeGuids.Add(guid);
                        }
                    }
                }
                catch { }
            }

            return (IReadOnlyList<string>)activeGuids;
        });
    }

    /// <summary>
    /// Detects whether Nagle's algorithm has been disabled across active network interfaces.
    /// Note: Disabling Nagle (TCPNoDelay) is a situational tweak. Most multiplayer games use UDP or
    /// set TCP_NODELAY at the socket level. Global disabling can increase TCP packet overhead for general traffic.
    /// </summary>
    public async Task<bool> DetectNagleAlgorithmDisabledAsync()
    {
        var guids = await GetActiveNetworkAdapterGuidsAsync();
        if (guids.Count == 0) return false;

        foreach (var guid in guids)
        {
            string keyPath = $@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\{guid}";
            var ack = _registry.GetValue("HKLM", keyPath, "TcpAckFrequency");
            var delay = _registry.GetValue("HKLM", keyPath, "TCPNoDelay");

            bool isAck = ack is int ackI ? ackI == 1 : (ack?.ToString() == "1");
            bool isNoDelay = delay is int delI ? delI == 1 : (delay?.ToString() == "1");

            if (isAck && isNoDelay) return true;
        }

        return false;
    }

    /// <summary>
    /// Configures or restores Nagle's algorithm on active network interfaces.
    /// </summary>
    public async Task<bool> SetNagleAlgorithmStateAsync(bool disableNagle, CancellationToken cancellationToken = default)
    {
        var guids = await GetActiveNetworkAdapterGuidsAsync();
        if (guids.Count == 0) return false;

        foreach (var guid in guids)
        {
            string keyPath = $@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\{guid}";
            if (disableNagle)
            {
                _registry.SetValue("HKLM", keyPath, "TcpAckFrequency", 1, "DWord");
                _registry.SetValue("HKLM", keyPath, "TCPNoDelay", 1, "DWord");
                _registry.SetValue("HKLM", keyPath, "TcpDelAckTicks", 0, "DWord");
            }
            else
            {
                _registry.DeleteValue("HKLM", keyPath, "TcpAckFrequency");
                _registry.DeleteValue("HKLM", keyPath, "TCPNoDelay");
                _registry.DeleteValue("HKLM", keyPath, "TcpDelAckTicks");
            }
        }

        return true;
    }
}
