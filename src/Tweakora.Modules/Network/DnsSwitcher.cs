using System.Net.NetworkInformation;

namespace Tweakora.Modules.Network;

public sealed class DnsProvider
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string PrimaryDns { get; init; }
    public string SecondaryDns { get; init; } = string.Empty;
    public bool IsActive { get; set; }
    public int PingMs { get; set; } = -1;
}

public sealed class DnsSwitcher
{
    public bool IsSimulationMode { get; set; }

    public DnsSwitcher(bool isSimulationMode = true)
    {
        IsSimulationMode = isSimulationMode;
    }

    public List<DnsProvider> GetProviders()
    {
        return new List<DnsProvider>
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

    public async Task<int> PingDnsAsync(string ip)
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

    public async Task<bool> ApplyDnsAsync(DnsProvider provider)
    {
        if (IsSimulationMode)
        {
            // Simulation mode: safe execution delay without changing host adapter settings
            await Task.Delay(200);
            return true;
        }

        // In VM real execution: configure network adapter DNS via powershell or netsh
        await Task.CompletedTask;
        return true;
    }
}
