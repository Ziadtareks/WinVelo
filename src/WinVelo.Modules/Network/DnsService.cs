using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using WinVelo.Core.Catalog;
using WinVelo.Core.Engine;
using WinVelo.Core.Interfaces;
using WinVelo.Core.Models;
using WinVelo.Core.Safety;
using WinVelo.Core.Utilities;

namespace WinVelo.Modules.Network;

public sealed class DnsProviderDefinition : ObservableObject
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string PrimaryDns { get; init; }
    public string SecondaryDns { get; init; } = "";
    public string Primary6Dns { get; init; } = "";
    public string Secondary6Dns { get; init; } = "";
    public string? DohTemplate { get; init; }
    public bool BenchmarkEligible { get; init; }
    private bool _active;
    public bool IsActive { get => _active; set => SetProperty(ref _active, value); }
    private int _ping = -1;
    public int PingMs { get => _ping; set => SetProperty(ref _ping, value); }
}

public sealed record DnsAdapter(int Index, string Name, bool IsDefault);

public sealed record DnsChangeResult(bool Success, string Message);

public sealed record DnsAdapterState(bool IPv4Enabled, bool IPv6Enabled, bool IPv4Automatic, bool IPv6Automatic,
    IReadOnlyList<string> IPv4Servers, IReadOnlyList<string> IPv6Servers)
{
    public bool Matches(DnsProviderDefinition provider)
    {
        if (!IPv4Enabled && !IPv6Enabled) return false;
        if (provider.PrimaryDns.Equals("DHCP", StringComparison.OrdinalIgnoreCase))
            return (!IPv4Enabled || IPv4Automatic) && (!IPv6Enabled || IPv6Automatic);
        static string[] Normalize(IEnumerable<string> addresses) => addresses.Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => IPAddress.Parse(a).ToString()).ToArray();
        var v4 = Normalize(new[] { provider.PrimaryDns, provider.SecondaryDns });
        var v6 = Normalize(new[] { provider.Primary6Dns, provider.Secondary6Dns });
        return (IPv4Enabled && v4.Length > 0 || IPv6Enabled && v6.Length > 0)
            && (!IPv4Enabled || v4.Length == 0 || Normalize(IPv4Servers).SequenceEqual(v4, StringComparer.OrdinalIgnoreCase))
            && (!IPv6Enabled || v6.Length == 0 || Normalize(IPv6Servers).SequenceEqual(v6, StringComparer.OrdinalIgnoreCase));
    }
}

public interface IDnsService
{
    IReadOnlyList<DnsProviderDefinition> GetProviders();
    Task<string?> GetCurrentProviderIdAsync(int adapterIndex, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
    Task<int> PingDnsAsync(string ip, CancellationToken cancellationToken = default);
    Task<bool> ApplyDnsAsync(DnsProviderDefinition provider, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DnsAdapter>> GetAdaptersAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DnsAdapter>>(Array.Empty<DnsAdapter>());
    Task<bool> ApplyDnsAsync(DnsProviderDefinition provider, int adapterIndex, bool useDoh, CancellationToken cancellationToken = default) => ApplyDnsAsync(provider, cancellationToken);
    async Task<DnsChangeResult> ApplyDnsWithResultAsync(DnsProviderDefinition provider, int adapterIndex, bool useDoh, CancellationToken cancellationToken = default)
    {
        bool ok = await ApplyDnsAsync(provider, adapterIndex, useDoh, cancellationToken);
        return new(ok, ok ? "DNS change completed." : "The DNS operation failed.");
    }
}

public sealed class DnsService : IDnsService
{
    private readonly IShellService _shell;
    private readonly RollbackJournalManager? _journal;
    public DnsService(IShellService shell, RollbackJournalManager? journal = null) { _shell = shell; _journal = journal; }

    public IReadOnlyList<DnsProviderDefinition> GetProviders()
    {
        var result = new List<DnsProviderDefinition>();
        using var doc = CatalogLoader.LoadDocument(CatalogSource.Default, "Config/dns.json");
        if (doc != null) foreach (var item in doc.RootElement.EnumerateObject())
        {
            string Get(string key) => item.Value.TryGetProperty(key, out var value) ? value.GetString() ?? "" : "";
            string id = item.Name switch
            {
                "Cloudflare" => "dns.cloudflare.standard",
                "Cloudflare_Malware" => "dns.cloudflare.security",
                "Cloudflare_Malware_Adult" => "dns.cloudflare.family",
                "Google" => "dns.google.public",
                "Quad9" => "dns.quad9.secure",
                "Open_DNS" => "dns.opendns.standard",
                "AdGuard_Ads_Trackers" => "dns.adguard.standard",
                "AdGuard_Ads_Trackers_Malware_Adult" => "dns.adguard.family",
                _ => item.Name
            };
            result.Add(new()
            {
                Id = id,
                Name = item.Name.Replace('_', ' '),
                Description = item.Name.Replace('_', ' '),
                PrimaryDns = Get("Primary"),
                SecondaryDns = Get("Secondary"),
                Primary6Dns = Get("Primary6"),
                Secondary6Dns = Get("Secondary6"),
                DohTemplate = Get("DohTemplate"),
                BenchmarkEligible = item.Value.TryGetProperty("BenchmarkEligible", out var eligible) && eligible.GetBoolean()
            });
        }
        result.Add(new() { Id = "dns.dhcp.default", Name = "Automatic (DHCP)", Description = "Use the DNS servers supplied by this network.", PrimaryDns = "DHCP", SecondaryDns = "DHCP" });
        return result;
    }

    private static string AdapterPrefix(int adapterIndex)
    {
        if (adapterIndex <= 0) throw new ArgumentException("Select a network adapter.");
        return $"$index={adapterIndex}; $a=Get-NetAdapter -IncludeHidden | Where-Object ifIndex -eq $index | Select-Object -First 1; if(-not $a){{throw 'Network adapter is unavailable.'}}; " +
            "$name=[System.Management.Automation.WildcardPattern]::Escape($a.Name); " +
            "$v4=[bool](Get-NetAdapterBinding -Name $name -ComponentID ms_tcpip -IncludeHidden -ErrorAction Stop).Enabled; " +
            "$v6=[bool](Get-NetAdapterBinding -Name $name -ComponentID ms_tcpip6 -IncludeHidden -ErrorAction Stop).Enabled; " +
            "if(-not ($v4 -or $v6)){throw 'No enabled IP protocol was found on this adapter.'}; ";
    }

    public static string BuildReadScript(int adapterIndex) => AdapterPrefix(adapterIndex) +
        "$guid=([guid]$a.InterfaceGuid).ToString('B'); " +
        "$static4=[string](Get-ItemProperty -LiteralPath \"HKLM:\\SYSTEM\\CurrentControlSet\\Services\\Tcpip\\Parameters\\Interfaces\\$guid\" -Name NameServer -ErrorAction SilentlyContinue).NameServer; " +
        "$static6=[string](Get-ItemProperty -LiteralPath \"HKLM:\\SYSTEM\\CurrentControlSet\\Services\\Tcpip6\\Parameters\\Interfaces\\$guid\" -Name NameServer -ErrorAction SilentlyContinue).NameServer; " +
        "[pscustomobject]@{IPv4Enabled=$v4; IPv6Enabled=$v6; IPv4Automatic=[string]::IsNullOrWhiteSpace($static4); IPv6Automatic=[string]::IsNullOrWhiteSpace($static6); " +
        "IPv4Servers=@(if($v4){(Get-DnsClientServerAddress -InterfaceIndex $index -AddressFamily IPv4 -ErrorAction Stop).ServerAddresses}); " +
        "IPv6Servers=@(if($v6){(Get-DnsClientServerAddress -InterfaceIndex $index -AddressFamily IPv6 -ErrorAction Stop).ServerAddresses})} | ConvertTo-Json -Compress";

    public async Task<DnsAdapterState> ReadAdapterStateAsync(int adapterIndex, CancellationToken cancellationToken = default)
    {
        var result = await _shell.ExecutePowerShellAsync(BuildReadScript(adapterIndex), 30, cancellationToken);
        if (!result.Success) throw new InvalidOperationException("Could not read DNS settings: " + Describe(result));
        using var doc = JsonDocument.Parse(result.StdOut);
        var root = doc.RootElement;
        string[] Read(string name) => root.GetProperty(name).EnumerateArray().Select(v => IPAddress.Parse(v.GetString()!).ToString()).ToArray();
        return new(root.GetProperty("IPv4Enabled").GetBoolean(), root.GetProperty("IPv6Enabled").GetBoolean(),
            root.GetProperty("IPv4Automatic").GetBoolean(), root.GetProperty("IPv6Automatic").GetBoolean(), Read("IPv4Servers"), Read("IPv6Servers"));
    }

    public async Task<string?> GetCurrentProviderIdAsync(int adapterIndex, CancellationToken cancellationToken = default)
    {
        if (adapterIndex <= 0 || !_shell.SupportsSystemQueries) return null;
        var state = await ReadAdapterStateAsync(adapterIndex, cancellationToken);
        var automatic = GetProviders().First(p => p.PrimaryDns == "DHCP");
        return state.Matches(automatic) ? automatic.Id : GetProviders().FirstOrDefault(p => p.PrimaryDns != "DHCP" && state.Matches(p))?.Id;
    }

    public async Task<IReadOnlyList<DnsAdapter>> GetAdaptersAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (TestModeManager.IsTestMode || !_shell.SupportsSystemQueries) return new[] { new DnsAdapter(1, "Simulation adapter", true) };
        var result = await _shell.ExecutePowerShellAsync("@(Get-NetIPConfiguration | Where-Object { $_.NetAdapter.Status -eq 'Up' } | ForEach-Object { [pscustomobject]@{Index=$_.InterfaceIndex; Name=$_.InterfaceAlias; IsDefault=[bool]($_.IPv4DefaultGateway -or $_.IPv6DefaultGateway)} }) | ConvertTo-Json -Compress", 30, cancellationToken);
        if (!result.Success) throw new InvalidOperationException("Could not list network adapters: " + Describe(result));
        if (string.IsNullOrWhiteSpace(result.StdOut) || result.StdOut.Trim() == "null") return Array.Empty<DnsAdapter>();
        try
        {
            using var doc = JsonDocument.Parse(result.StdOut);
            var items = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.EnumerateArray().ToArray() : new[] { doc.RootElement };
            return items.Select(v => new DnsAdapter(v.GetProperty("Index").GetInt32(), v.GetProperty("Name").GetString() ?? "", v.GetProperty("IsDefault").GetBoolean())).OrderByDescending(a => a.IsDefault).ToList();
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException) { throw new InvalidOperationException("Invalid network adapter response: " + ex.Message, ex); }
    }

    /// <summary>Measures an actual UDP DNS query round trip; does not measure ICMP reachability.</summary>
    public async Task<int> PingDnsAsync(string ip, CancellationToken cancellationToken = default)
    {
        if (!IPAddress.TryParse(ip, out var address)) return -1;
        if (TestModeManager.IsTestMode) return 12;
        try
        {
            using var udp = new UdpClient(address.AddressFamily);
            udp.Connect(address, 53);
            byte[] query = { 0, 0, 1, 0, 0, 1, 0, 0, 0, 0, 0, 0, 7, 101, 120, 97, 109, 112, 108, 101, 3, 99, 111, 109, 0, 0, 1, 0, 1 };
            int id = RandomNumberGenerator.GetInt32(65536); query[0] = (byte)(id >> 8); query[1] = (byte)id;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(1500);
            var watch = Stopwatch.StartNew();
            await udp.SendAsync(query, timeout.Token);
            var reply = await udp.ReceiveAsync(timeout.Token);
            if (reply.Buffer.Length < 12 || reply.Buffer[0] != query[0] || reply.Buffer[1] != query[1] || (reply.Buffer[2] & 128) == 0 || (reply.Buffer[3] & 15) != 0) return -1;
            return (int)watch.ElapsedMilliseconds;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return -1; }
        catch (SocketException) { return -1; }
    }

    public async Task<bool> ApplyDnsAsync(DnsProviderDefinition provider, CancellationToken cancellationToken = default)
        => (await ApplyDnsWithResultAsync(provider, cancellationToken)).Success;

    public Task<DnsChangeResult> ApplyDnsWithResultAsync(DnsProviderDefinition provider, CancellationToken cancellationToken = default)
        => ApplyDnsWithResultAsync(provider, false, cancellationToken);

    public async Task<DnsChangeResult> ApplyDnsWithResultAsync(DnsProviderDefinition provider, bool useDoh, CancellationToken cancellationToken = default)
    {
        try
        {
            var adapters = await GetAdaptersAsync(cancellationToken);
            var defaults = adapters.Where(a => a.IsDefault).ToList();
            var adapter = defaults.Count == 1 ? defaults[0] : adapters.Count == 1 ? adapters[0] : null;
            return adapter == null ? new(false, "Select a network adapter; no unique default adapter was found.")
                : await ApplyDnsWithResultAsync(provider, adapter.Index, useDoh, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { return new(false, ex.Message); }
    }

    private static string[] Addresses(DnsProviderDefinition provider, bool ipv6) =>
        (ipv6 ? new[] { provider.Primary6Dns, provider.Secondary6Dns } : new[] { provider.PrimaryDns, provider.SecondaryDns })
        .Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => IPAddress.Parse(a).ToString()).ToArray();
    private static string Quote(IEnumerable<string> addresses) => string.Join(',', addresses.Select(a => "'" + IPAddress.Parse(a).ToString() + "'"));

    public static string BuildApplyScript(DnsProviderDefinition provider, int adapterIndex, bool useDoh)
    {
        string prefix = AdapterPrefix(adapterIndex);
        if (provider.PrimaryDns.Equals("DHCP", StringComparison.OrdinalIgnoreCase))
        {
            if (useDoh) throw new ArgumentException("DNS over HTTPS cannot be configured for automatic DHCP DNS.");
            return prefix + "if($v4){Get-DnsClientServerAddress -InterfaceIndex $index -AddressFamily IPv4 | Set-DnsClientServerAddress -ResetServerAddresses -ErrorAction Stop}; " +
                "if($v6){Get-DnsClientServerAddress -InterfaceIndex $index -AddressFamily IPv6 | Set-DnsClientServerAddress -ResetServerAddresses -ErrorAction Stop}";
        }
        string[] v4; string[] v6;
        try { v4 = Addresses(provider, false); v6 = Addresses(provider, true); }
        catch (FormatException) { throw new ArgumentException("Invalid DNS server address."); }
        if (v4.Length + v6.Length == 0 || v4.Any(a => IPAddress.Parse(a).AddressFamily != AddressFamily.InterNetwork) || v6.Any(a => IPAddress.Parse(a).AddressFamily != AddressFamily.InterNetworkV6))
            throw new ArgumentException("Invalid DNS server address family.");
        prefix += $"$ips=@(if($v4){{@({Quote(v4)})}}; if($v6){{@({Quote(v6)})}}); if($ips.Count -eq 0){{throw 'This DNS provider has no addresses for the enabled IP protocols.'}}; ";
        if (useDoh)
        {
            if (!Uri.TryCreate(provider.DohTemplate, UriKind.Absolute, out var uri) || uri.Scheme != "https") throw new ArgumentException("This provider has no HTTPS DNS template.");
            prefix += "if(-not(Get-Command Add-DnsClientDohServerAddress -ErrorAction SilentlyContinue)){throw 'DNS over HTTPS is not supported on this Windows version.'}; ";
            string template = uri.AbsoluteUri.Replace("'", "''");
            prefix += $"foreach($ip in $ips){{if(Get-DnsClientDohServerAddress -ServerAddress $ip -ErrorAction SilentlyContinue){{Set-DnsClientDohServerAddress -ServerAddress $ip -DohTemplate '{template}' -AutoUpgrade $true -AllowFallbackToUdp $false -ErrorAction Stop}}else{{Add-DnsClientDohServerAddress -ServerAddress $ip -DohTemplate '{template}' -AutoUpgrade $true -AllowFallbackToUdp $false -ErrorAction Stop}}}}; ";
            prefix += $"foreach($ip in $ips){{$entry=Get-DnsClientDohServerAddress -ServerAddress $ip -ErrorAction Stop; if(-not $entry -or $entry.DohTemplate -ne '{template}' -or -not $entry.AutoUpgrade -or $entry.AllowFallbackToUdp){{throw 'Windows did not retain the requested DNS over HTTPS settings.'}}}}; ";
        }
        if (v4.Length > 0) prefix += $"if($v4){{Get-DnsClientServerAddress -InterfaceIndex $index -AddressFamily IPv4 | Set-DnsClientServerAddress -ServerAddresses @({Quote(v4)}) -ErrorAction Stop}}; ";
        if (v6.Length > 0) prefix += $"if($v6){{Get-DnsClientServerAddress -InterfaceIndex $index -AddressFamily IPv6 | Set-DnsClientServerAddress -ServerAddresses @({Quote(v6)}) -ErrorAction Stop}}; ";
        return prefix;
    }

    public async Task<bool> ApplyDnsAsync(DnsProviderDefinition provider, int adapterIndex, bool useDoh, CancellationToken cancellationToken = default)
        => (await ApplyDnsWithResultAsync(provider, adapterIndex, useDoh, cancellationToken)).Success;

    private static List<ActionItem> CreateUndo(DnsAdapterState state, DnsProviderDefinition provider, int index)
    {
        var undo = new List<ActionItem>();
        bool automatic = provider.PrimaryDns == "DHCP";
        void Add(string family, bool enabled, bool wasAutomatic, IReadOnlyList<string> servers, bool changesFamily)
        {
            if (!enabled || !changesFamily) return;
            if (!wasAutomatic && servers.Count == 0) throw new InvalidOperationException("Cannot capture a nonempty static DNS baseline for " + family);
            string action = wasAutomatic ? "-ResetServerAddresses" : "-ServerAddresses @(" + Quote(servers) + ")";
            undo.Add(ModuleJournal.UndoScript($"Get-DnsClientServerAddress -InterfaceIndex {index} -AddressFamily {family} | Set-DnsClientServerAddress {action} -ErrorAction Stop"));
        }
        Add("IPv4", state.IPv4Enabled, state.IPv4Automatic, state.IPv4Servers, automatic || Addresses(provider, false).Length > 0);
        Add("IPv6", state.IPv6Enabled, state.IPv6Automatic, state.IPv6Servers, automatic || Addresses(provider, true).Length > 0);
        return undo;
    }

    private static string Describe(PowerShellResult result) => result.Cancelled ? "Operation cancelled." : result.TimedOut ? "Windows command timed out." :
        !string.IsNullOrWhiteSpace(result.StdErr) ? result.StdErr.Trim() : $"Windows command failed (exit {result.ExitCode}). {result.StdOut.Trim()}";

    public async Task<DnsChangeResult> ApplyDnsWithResultAsync(DnsProviderDefinition provider, int adapterIndex, bool useDoh, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string script;
        try { script = BuildApplyScript(provider, adapterIndex, useDoh); }
        catch (ArgumentException ex) { return new(false, ex.Message); }
        if (_journal != null) await _journal.OperationGate.WaitAsync(cancellationToken);
        var undo = new List<ActionItem>();
        JournalActionRecord? record = null;
        bool started = false;
        DnsAdapterState? before = null;
        try
        {
            if (_shell.SupportsSystemQueries && !TestModeManager.IsTestMode)
                before = await ReadAdapterStateAsync(adapterIndex, cancellationToken);
            if (before != null)
            {
                undo = CreateUndo(before, provider, adapterIndex);
                if (undo.Count == 0) return new(false, "This DNS provider has no addresses for the enabled IP protocols.");
                if (useDoh)
                {
                    string ips = Quote((before.IPv4Enabled ? Addresses(provider, false) : []).Concat(before.IPv6Enabled ? Addresses(provider, true) : []));
                    var capture = await _shell.ExecutePowerShellAsync($"if(-not(Get-Command Get-DnsClientDohServerAddress -ErrorAction SilentlyContinue)){{throw 'DNS over HTTPS is not supported on this Windows version.'}}; @(@({ips}) | ForEach-Object {{ $ip=$_; $e=Get-DnsClientDohServerAddress -ServerAddress $ip -ErrorAction SilentlyContinue; [pscustomobject]@{{IP=$ip; Exists=[bool]$e; Template=[string]$e.DohTemplate; Auto=[bool]$e.AutoUpgrade; Fallback=[bool]$e.AllowFallbackToUdp}} }}) | ConvertTo-Json -Compress", 30, cancellationToken);
                    if (!capture.Success) return new(false, "Could not capture DNS over HTTPS settings: " + Describe(capture));
                    using var doh = JsonDocument.Parse(capture.StdOut);
                    foreach (var item in doh.RootElement.ValueKind == JsonValueKind.Array ? doh.RootElement.EnumerateArray().ToArray() : new[] { doh.RootElement })
                    {
                        string ip = IPAddress.Parse(item.GetProperty("IP").GetString()!).ToString();
                        string restore = item.GetProperty("Exists").GetBoolean()
                            ? $"Set-DnsClientDohServerAddress -ServerAddress '{ip}' -DohTemplate '{(item.GetProperty("Template").GetString() ?? "").Replace("'", "''")}' -AutoUpgrade ${item.GetProperty("Auto").GetBoolean().ToString().ToLowerInvariant()} -AllowFallbackToUdp ${item.GetProperty("Fallback").GetBoolean().ToString().ToLowerInvariant()} -ErrorAction Stop"
                            : $"if(Get-DnsClientDohServerAddress -ServerAddress '{ip}' -ErrorAction SilentlyContinue){{Remove-DnsClientDohServerAddress -ServerAddress '{ip}' -ErrorAction Stop}}; if(Get-DnsClientDohServerAddress -ServerAddress '{ip}' -ErrorAction SilentlyContinue){{throw 'DNS over HTTPS rollback verification failed.'}}";
                        undo.Add(ModuleJournal.UndoScript(restore));
                    }
                }
            }
            else undo.Add(ModuleJournal.UndoScript("# Simulated DNS baseline"));
            if (_journal != null)
            {
                _journal.StartSession();
                record = new JournalActionRecord { TweakId = $"tools.dns.adapter-{adapterIndex}", ActionType = "PowerShell", TargetIdentifier = "powershell://dns/" + adapterIndex, UndoActions = undo };
                _journal.RecordActionWriteAhead(record);
            }
            started = true;
            var result = await _shell.ExecutePowerShellAsync(script, 60, cancellationToken);
            if (!result.Success) throw new InvalidOperationException(Describe(result));
            if (before != null)
            {
                var actual = await ReadAdapterStateAsync(adapterIndex, cancellationToken);
                if (!actual.Matches(provider)) throw new InvalidOperationException("Windows did not retain the requested DNS settings. A policy or network service may override them.");
            }
            _journal?.CompleteSession();
            return new(true, before == null ? "Simulation completed; no network settings were changed." : before is { IPv4Enabled: true, IPv6Enabled: false } ? "IPv6 is disabled on this adapter; only IPv4 DNS was updated." : "DNS settings verified successfully.");
        }
        catch (Exception ex)
        {
            if (!started) return new(false, "DNS preflight failed: " + ex.Message);
            bool restored = before != null && undo.Count > 0;
            if (before != null) foreach (var action in undo.AsEnumerable().Reverse())
            {
                try { restored &= (await _shell.ExecutePowerShellAsync(action.Script!, 30, CancellationToken.None)).Success; }
                catch { restored = false; }
            }
            if (restored && before != null)
            {
                try
                {
                    var actual = await ReadAdapterStateAsync(adapterIndex, CancellationToken.None);
                    restored = actual.IPv4Enabled == before.IPv4Enabled && actual.IPv6Enabled == before.IPv6Enabled
                        && (!before.IPv4Enabled || actual.IPv4Automatic == before.IPv4Automatic && (before.IPv4Automatic || actual.IPv4Servers.SequenceEqual(before.IPv4Servers, StringComparer.OrdinalIgnoreCase)))
                        && (!before.IPv6Enabled || actual.IPv6Automatic == before.IPv6Automatic && (before.IPv6Automatic || actual.IPv6Servers.SequenceEqual(before.IPv6Servers, StringComparer.OrdinalIgnoreCase)));
                }
                catch { restored = false; }
            }
            if (restored && record != null) { _journal!.MarkRecordsReverted([record]); _journal.CompleteSession(); }
            return new(false, "DNS change failed: " + ex.Message + (restored ? " Previous settings restored." : before != null ? " Rollback is incomplete; inspect History before retrying." : ""));
        }
        finally { _journal?.OperationGate.Release(); }
    }
}
