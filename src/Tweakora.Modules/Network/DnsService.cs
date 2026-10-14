using System;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using Tweakora.Core.Interfaces;

namespace Tweakora.Modules.Network;

public sealed class DnsProviderDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string PrimaryDns { get; init; }
    public string SecondaryDns { get; init; } = string.Empty;
    public bool IsActive { get; set; }
    public int PingMs { get; set; } = -1;
}

public interface IDnsService
{
    IReadOnlyList<DnsProviderDefinition> GetProviders();
    Task<int> PingDnsAsync(string ip, CancellationToken cancellationToken = default);
    Task<bool> ApplyDnsAsync(DnsProviderDefinition provider, CancellationToken cancellationToken = default);
}

public sealed class DnsService : IDnsService
{
    private readonly IShellService _shell;

    public DnsService(IShellService shell)
    {
        _shell = shell;
    }

    public IReadOnlyList<DnsProviderDefinition> GetProviders()
    {
        return new List<DnsProviderDefinition>
        {
            new()
            {
                Id = "dns.cloudflare.standard",
                Name = "Cloudflare DNS",
                Description = "Fast, privacy-focused DNS resolver that never sells browsing data.",
                PrimaryDns = "1.1.1.1",
                SecondaryDns = "1.0.0.1",
                IsActive = true
            },
            new()
            {
                Id = "dns.cloudflare.security",
                Name = "Cloudflare Malware Blocking",
                Description = "Automatically blocks known malware, malicious domains, and phishing.",
                PrimaryDns = "1.1.1.2",
                SecondaryDns = "1.0.0.2"
            },
            new()
            {
                Id = "dns.google.public",
                Name = "Google Public DNS",
                Description = "High-performance global anycast network DNS resolver.",
                PrimaryDns = "8.8.8.8",
                SecondaryDns = "8.8.4.4"
            },
            new()
            {
                Id = "dns.quad9.secure",
                Name = "Quad9 DNS",
                Description = "Swiss-based privacy DNS that blocks malicious domains using threat intelligence.",
                PrimaryDns = "9.9.9.9",
                SecondaryDns = "149.112.112.112"
            },
            new()
            {
                Id = "dns.adguard.standard",
                Name = "AdGuard DNS",
                Description = "Blocks advertising servers, tracking domains, and analytical telemetry.",
                PrimaryDns = "94.140.14.14",
                SecondaryDns = "94.140.15.15"
            },
            new()
            {
                Id = "dns.dhcp.default",
                Name = "Automatic (DHCP)",
                Description = "Revert to your local router or Internet Service Provider default DNS.",
                PrimaryDns = "DHCP",
                SecondaryDns = "DHCP"
            }
        };
    }

    public async Task<int> PingDnsAsync(string ip, CancellationToken cancellationToken = default)
    {
        if (ip.Equals("DHCP", StringComparison.OrdinalIgnoreCase)) return 0;

        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(ip, 1200);
            if (reply.Status == IPStatus.Success)
            {
                return (int)reply.RoundtripTime;
            }
            return -1;
        }
        catch
        {
            return -1;
        }
    }

    public async Task<bool> ApplyDnsAsync(DnsProviderDefinition provider, CancellationToken cancellationToken = default)
    {
        string script;
        if (string.Equals(provider.PrimaryDns, "DHCP", StringComparison.OrdinalIgnoreCase))
        {
            script = "Get-NetAdapter | Where-Object Status -eq 'Up' | Set-DnsClientServerAddress -ResetServerAddresses";
        }
        else if (string.IsNullOrWhiteSpace(provider.SecondaryDns) || provider.SecondaryDns == "DHCP")
        {
            script = $"Get-NetAdapter | Where-Object Status -eq 'Up' | Set-DnsClientServerAddress -ServerAddresses '{provider.PrimaryDns}'";
        }
        else
        {
            script = $"Get-NetAdapter | Where-Object Status -eq 'Up' | Set-DnsClientServerAddress -ServerAddresses '{provider.PrimaryDns}', '{provider.SecondaryDns}'";
        }

        var res = await _shell.ExecutePowerShellAsync(script, 30);
        return res.Success;
    }
}
