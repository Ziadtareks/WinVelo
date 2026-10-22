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
    private readonly WinVelo.Core.Engine.RollbackJournalManager? _journal;

    public NetworkOptimizerService(IRegistryService registry, IShellService shell, WinVelo.Core.Engine.RollbackJournalManager? journal = null)
    {
        _registry = WinVelo.Core.Engine.ModuleJournal.Registry(registry, journal, "tools.network.nagle");
        _shell = shell;
        _journal = journal;
    }

    public async Task<IReadOnlyList<DnsBenchmarkResult>> BenchmarkDnsProvidersAsync(CancellationToken cancellationToken = default)
    {
        var dns = new DnsService(_shell, _journal);
        var results = new List<DnsBenchmarkResult>();
        foreach (var provider in dns.GetProviders().Where(p => p.BenchmarkEligible))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var samples = new List<int>();
            for (int i = 0; i < 3; i++)
            {
                int latency = await dns.PingDnsAsync(provider.PrimaryDns, cancellationToken);
                if (latency >= 0) samples.Add(latency);
            }
            results.Add(new DnsBenchmarkResult
            {
                Id = provider.Id,
                Name = provider.Name,
                Description = provider.Description,
                PrimaryDns = provider.PrimaryDns,
                SecondaryDns = provider.SecondaryDns,
                AveragePingMs = samples.Count > 0 ? (int)Math.Round(samples.Average()) : -1
            });
        }
        var fastest = results.Where(r => r.AveragePingMs >= 0).OrderBy(r => r.AveragePingMs).FirstOrDefault();
        if (fastest != null) fastest.IsFastest = true;
        return results;
    }

    public Task<bool> ApplyDnsAsync(string primaryDns, string secondaryDns, CancellationToken cancellationToken = default)
        => new DnsService(_shell, _journal).ApplyDnsAsync(new DnsProviderDefinition { Id = "custom", Name = "Custom DNS", Description = "Custom DNS", PrimaryDns = primaryDns, SecondaryDns = secondaryDns }, cancellationToken);

    public Task<bool> ResetDnsToDhcpAsync(CancellationToken cancellationToken = default)
        => new DnsService(_shell, _journal).ApplyDnsAsync(new DnsProviderDefinition { Id = "dns.dhcp.default", Name = "Automatic (DHCP)", Description = "Automatic DNS", PrimaryDns = "DHCP" }, cancellationToken);

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

            if (!isAck || !isNoDelay) return false;
        }

        return true;
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
