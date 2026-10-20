using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinVelo.Core.Catalog;
using WinVelo.Core.Utilities;
using WinVelo.Core.Interfaces;

namespace WinVelo.Modules.Network;

public sealed class DnsProviderDefinition : INotifyPropertyChanged
{
    private bool _isActive;
    private int _pingMs = -1;

    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string PrimaryDns { get; init; }
    public string SecondaryDns { get; init; } = string.Empty;
    public string PrimaryDns6 { get; init; } = string.Empty;
    public string SecondaryDns6 { get; init; } = string.Empty;

    /// <summary>The WinUtil dns.json key (for example Cloudflare_Malware), so WinUtil names work in the CLI too.</summary>
    public string UpstreamKey { get; init; } = string.Empty;

    /// <summary>
    /// True for plain resolvers that do not filter anything (WinUtil's BenchmarkEligible). Only these may be picked
    /// automatically by "Apply fastest", so a filtering resolver (ads, malware, adult) is never switched on by surprise.
    /// </summary>
    public bool IsNeutral { get; init; }

    /// <summary>Every server address of this provider, IPv4 first, then IPv6.</summary>
    public IReadOnlyList<string> AllAddresses =>
        new[] { PrimaryDns, SecondaryDns, PrimaryDns6, SecondaryDns6 }.Where(a => !string.IsNullOrWhiteSpace(a) && !a.Equals("DHCP", StringComparison.OrdinalIgnoreCase)).ToList();

    public bool IsActive
    {
        get => _isActive;
        set { if (_isActive != value) { _isActive = value; OnChanged(); } }
    }

    public int PingMs
    {
        get => _pingMs;
        set { if (_pingMs != value) { _pingMs = value; OnChanged(); } }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public interface IDnsService
{
    IReadOnlyList<DnsProviderDefinition> GetProviders();
    Task<int> PingDnsAsync(string ip, CancellationToken cancellationToken = default);
    Task<bool> ApplyDnsAsync(DnsProviderDefinition provider, CancellationToken cancellationToken = default);

    /// <summary>DNS server addresses currently set on the adapters that are up (empty if unknown or automatic).</summary>
    Task<IReadOnlyList<string>> GetCurrentServersAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// DNS providers come from Catalog/Config/dns.json (the same 8 providers as WinUtil, with IPv4 and IPv6 servers),
/// plus "Automatic (DHCP)". Applying a provider sets its IPv4 and IPv6 servers on every adapter that is up, so IPv6
/// lookups do not keep going to the old servers.
/// </summary>
public sealed class DnsService : IDnsService
{
    public const string DhcpId = "dns.dhcp.default";

    private readonly IShellService _shell;
    private readonly CatalogSource _catalog;

    /// <summary>WinVelo id, display name, description and whether it filters, per WinUtil dns.json key.</summary>
    private static readonly (string Key, string Id, string Name, string Description)[] Known =
    {
        ("Cloudflare", "dns.cloudflare.standard", "Cloudflare DNS", "Fast, privacy-focused DNS resolver that never sells browsing data."),
        ("Cloudflare_Malware", "dns.cloudflare.security", "Cloudflare Malware Blocking", "Automatically blocks known malware, malicious domains, and phishing."),
        ("Cloudflare_Malware_Adult", "dns.cloudflare.family", "Cloudflare for Families", "Blocks malware and adult content."),
        ("Google", "dns.google.public", "Google Public DNS", "High-performance global anycast network DNS resolver."),
        ("Quad9", "dns.quad9.secure", "Quad9 DNS", "Swiss-based privacy DNS that blocks malicious domains using threat intelligence."),
        ("Open_DNS", "dns.opendns", "OpenDNS (Cisco)", "Cisco's public DNS resolver."),
        ("AdGuard_Ads_Trackers", "dns.adguard.standard", "AdGuard DNS", "Blocks advertising servers, tracking domains, and analytical telemetry."),
        ("AdGuard_Ads_Trackers_Malware_Adult", "dns.adguard.family", "AdGuard Family", "Blocks ads, trackers, malware and adult content.")
    };

    public DnsService(IShellService shell) : this(shell, CatalogSource.Default)
    {
    }

    public DnsService(IShellService shell, CatalogSource catalog)
    {
        _shell = shell;
        _catalog = catalog;
    }

    public IReadOnlyList<DnsProviderDefinition> GetProviders()
    {
        var list = new List<DnsProviderDefinition>();
        using var doc = CatalogLoader.LoadDocument(_catalog, "Config/dns.json");
        if (doc != null && doc.RootElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                var provider = FromJson(prop.Name, prop.Value);
                if (provider != null) list.Add(provider);
            }
        }

        list.Add(new DnsProviderDefinition
        {
            Id = DhcpId,
            Name = "Automatic (DHCP)",
            Description = "Revert to your local router or Internet Service Provider default DNS.",
            PrimaryDns = "DHCP",
            SecondaryDns = "DHCP",
            PrimaryDns6 = "DHCP",
            UpstreamKey = "DHCP"
        });
        return list;
    }

    private static DnsProviderDefinition? FromJson(string key, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object) return null;
        string Get(string name) => value.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()!.Trim() : string.Empty;

        string primary = Get("Primary");
        string secondary = Get("Secondary");
        string primary6 = Get("Primary6");
        string secondary6 = Get("Secondary6");

        // Only well-formed addresses ever reach a command.
        if (!SafeCommands.IsValidIp(primary)) return null;
        if (secondary.Length > 0 && !SafeCommands.IsValidIp(secondary)) secondary = string.Empty;
        if (primary6.Length > 0 && !SafeCommands.IsValidIp(primary6)) primary6 = string.Empty;
        if (secondary6.Length > 0 && !SafeCommands.IsValidIp(secondary6)) secondary6 = string.Empty;

        var known = Known.FirstOrDefault(k => string.Equals(k.Key, key, StringComparison.OrdinalIgnoreCase));
        bool neutral = value.TryGetProperty("BenchmarkEligible", out var be) && be.ValueKind == JsonValueKind.True;

        return new DnsProviderDefinition
        {
            Id = known.Id ?? "dns." + key.ToLowerInvariant(),
            Name = known.Name ?? key.Replace('_', ' '),
            Description = known.Description ?? string.Empty,
            PrimaryDns = primary,
            SecondaryDns = secondary,
            PrimaryDns6 = primary6,
            SecondaryDns6 = secondary6,
            UpstreamKey = key,
            IsNeutral = neutral
        };
    }

    /// <summary>Finds a provider by WinVelo id, WinUtil key (Cloudflare_Malware) or display name.</summary>
    public static DnsProviderDefinition? Find(IEnumerable<DnsProviderDefinition> providers, string idOrName) =>
        providers.FirstOrDefault(p =>
            string.Equals(p.Id, idOrName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(p.UpstreamKey, idOrName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(p.Name, idOrName, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Measures how long a TCP connection to port 53 takes, like WinUtil's benchmark. Many networks block ping (ICMP)
    /// but not DNS, so ping is only used when the TCP probe fails. Returns -1 if the server does not answer.
    /// </summary>
    public async Task<int> PingDnsAsync(string ip, CancellationToken cancellationToken = default)
    {
        if (ip.Equals("DHCP", StringComparison.OrdinalIgnoreCase)) return 0;
        if (!IPAddress.TryParse(ip, out var address)) return -1;

        try
        {
            using var client = new TcpClient(address.AddressFamily);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(1500);
            var watch = Stopwatch.StartNew();
            await client.ConnectAsync(address, 53, timeout.Token);
            return (int)Math.Max(1, watch.ElapsedMilliseconds);
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException)
        {
        }

        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(address, 1200);
            return reply.Status == IPStatus.Success ? (int)Math.Max(1, reply.RoundtripTime) : -1;
        }
        catch (Exception ex) when (ex is PingException or InvalidOperationException)
        {
            return -1;
        }
    }

    public async Task<bool> ApplyDnsAsync(DnsProviderDefinition provider, CancellationToken cancellationToken = default)
    {
        string script;
        try
        {
            script = provider.Id == DhcpId || provider.PrimaryDns.Equals("DHCP", StringComparison.OrdinalIgnoreCase)
                ? SafeCommands.SetDnsServers("DHCP", null)
                : SafeCommands.SetDnsServers(provider.AllAddresses);
        }
        catch (ArgumentException)
        {
            return false;
        }

        var res = await _shell.ExecutePowerShellAsync(script, 30);
        return res.Success;
    }

    public async Task<IReadOnlyList<string>> GetCurrentServersAsync(CancellationToken cancellationToken = default)
    {
        const string script = "Get-NetAdapter | Where-Object Status -eq 'Up' | Get-DnsClientServerAddress | ForEach-Object { $_.ServerAddresses } | Sort-Object -Unique";
        var res = await _shell.ExecutePowerShellAsync(script, 20);
        if (!res.Success || string.IsNullOrWhiteSpace(res.StdOut)) return Array.Empty<string>();
        return res.StdOut
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(SafeCommands.IsValidIp)
            .ToList();
    }

    /// <summary>The provider whose primary IPv4 server is in use now, or null.</summary>
    public static DnsProviderDefinition? MatchActive(IEnumerable<DnsProviderDefinition> providers, IReadOnlyList<string> current)
    {
        if (current.Count == 0) return null;
        var set = new HashSet<string>(current.Select(c => IPAddress.TryParse(c, out var a) ? a.ToString() : c), StringComparer.OrdinalIgnoreCase);
        return providers.FirstOrDefault(p => p.Id != DhcpId && set.Contains(p.PrimaryDns));
    }
}
