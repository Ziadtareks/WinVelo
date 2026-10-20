using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using WinVelo.Core.Utilities;
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

    /// <summary>Plain resolver that filters nothing (only these are picked automatically).</summary>
    public bool IsNeutral { get; init; }
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

    /// <summary>
    /// Times every provider from Catalog/Config/dns.json (3 probes each, TCP port 53 like WinUtil, ping as fallback).
    /// IsFastest marks the fastest NEUTRAL provider only, so "Apply fastest" never picks a filtering resolver.
    /// </summary>
    public async Task<IReadOnlyList<DnsBenchmarkResult>> BenchmarkDnsProvidersAsync(CancellationToken cancellationToken = default)
    {
        var dns = new DnsService(_shell);
        var providers = dns.GetProviders()
            .Where(p => p.Id != DnsService.DhcpId)
            .Select(p => new DnsBenchmarkResult
            {
                Id = p.Id,
                Name = p.Name,
                PrimaryDns = p.PrimaryDns,
                SecondaryDns = p.SecondaryDns,
                Description = p.Description,
                IsNeutral = p.IsNeutral
            })
            .ToList();

        if (WinVelo.Core.Safety.TestModeManager.IsTestMode)
        {
            // Simulation: no network traffic. Fixed, plausible numbers.
            int fake = 12;
            foreach (var p in providers) { p.AveragePingMs = fake; fake += 4; }
        }
        else
        {
            foreach (var p in providers)
            {
                if (cancellationToken.IsCancellationRequested) break;
                int sum = 0, ok = 0;
                for (int i = 0; i < 3 && !cancellationToken.IsCancellationRequested; i++)
                {
                    int ms = await dns.PingDnsAsync(p.PrimaryDns, cancellationToken);
                    if (ms > 0) { sum += ms; ok++; }
                }
                p.AveragePingMs = ok > 0 ? (int)Math.Round((double)sum / ok) : -1;
            }
        }

        var fastest = providers.Where(p => p.IsNeutral && p.AveragePingMs > 0).OrderBy(p => p.AveragePingMs).FirstOrDefault();
        if (fastest != null)
        {
            fastest.IsFastest = true;
        }

        return providers;
    }

    public async Task<bool> ApplyDnsAsync(string primaryDns, string secondaryDns, CancellationToken cancellationToken = default)
    {
        string script;
        try
        {
            script = SafeCommands.SetDnsServers(primaryDns, secondaryDns);
        }
        catch (ArgumentException)
        {
            return false;
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
