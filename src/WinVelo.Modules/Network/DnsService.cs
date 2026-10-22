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

public interface IDnsService
{
    IReadOnlyList<DnsProviderDefinition> GetProviders();
    Task<string?> GetCurrentProviderIdAsync(int adapterIndex, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
    Task<int> PingDnsAsync(string ip, CancellationToken cancellationToken = default);
    Task<bool> ApplyDnsAsync(DnsProviderDefinition provider, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DnsAdapter>> GetAdaptersAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DnsAdapter>>(Array.Empty<DnsAdapter>());
    Task<bool> ApplyDnsAsync(DnsProviderDefinition provider, int adapterIndex, bool useDoh, CancellationToken cancellationToken = default) => ApplyDnsAsync(provider, cancellationToken);
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

    public async Task<string?> GetCurrentProviderIdAsync(int adapterIndex, CancellationToken cancellationToken = default)
    {
        if (adapterIndex <= 0 || !_shell.SupportsSystemQueries) return null;
        string script = $"$a=Get-NetAdapter | Where-Object ifIndex -eq {adapterIndex} | Select-Object -First 1; if(-not $a){{throw 'Adapter is unavailable.'}}; $guid=([guid]$a.InterfaceGuid).ToString('B'); $static=@('Tcpip','Tcpip6') | ForEach-Object {{ [string](Get-ItemProperty -LiteralPath \"HKLM:\\SYSTEM\\CurrentControlSet\\Services\\$_\\Parameters\\Interfaces\\$guid\" -Name NameServer -ErrorAction SilentlyContinue).NameServer }}; [pscustomobject]@{{Dhcp=(-not ($static | Where-Object {{-not [string]::IsNullOrWhiteSpace($_)}})); Servers=@((Get-DnsClientServerAddress -InterfaceIndex {adapterIndex} -ErrorAction Stop).ServerAddresses)}} | ConvertTo-Json -Compress";
        var result = await _shell.ExecutePowerShellAsync(script, 30, cancellationToken);
        if (!result.Success) return null;
        try
        {
            using var doc = JsonDocument.Parse(result.StdOut);
            if (doc.RootElement.GetProperty("Dhcp").GetBoolean()) return "dns.dhcp.default";
            var servers = doc.RootElement.GetProperty("Servers").EnumerateArray().Select(v => IPAddress.Parse(v.GetString()!).ToString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            return GetProviders().FirstOrDefault(p => p.PrimaryDns != "DHCP" && servers.SetEquals(new[] { p.PrimaryDns, p.SecondaryDns, p.Primary6Dns, p.Secondary6Dns }.Where(ip => !string.IsNullOrWhiteSpace(ip)).Select(ip => IPAddress.Parse(ip).ToString())))?.Id;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or FormatException or InvalidOperationException) { return null; }
    }

    public async Task<IReadOnlyList<DnsAdapter>> GetAdaptersAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (TestModeManager.IsTestMode) return new[] { new DnsAdapter(1, "Simulation adapter", true) };
        var result = await _shell.ExecutePowerShellAsync("@(Get-NetIPConfiguration | Where-Object { $_.NetAdapter.Status -eq 'Up' } | ForEach-Object { [pscustomobject]@{Index=$_.InterfaceIndex; Name=$_.InterfaceAlias; IsDefault=[bool]($_.IPv4DefaultGateway -or $_.IPv6DefaultGateway)} }) | ConvertTo-Json -Compress", 30);
        if (!result.Success) return Array.Empty<DnsAdapter>();
        try
        {
            using var doc = JsonDocument.Parse(result.StdOut);
            var items = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.EnumerateArray().ToArray() : new[] { doc.RootElement };
            return items.Select(v => new DnsAdapter(v.GetProperty("Index").GetInt32(), v.GetProperty("Name").GetString() ?? "", v.GetProperty("IsDefault").GetBoolean())).OrderByDescending(a => a.IsDefault).ToList();
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException) { return Array.Empty<DnsAdapter>(); }
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
    {
        var adapters = await GetAdaptersAsync(cancellationToken);
        var defaults = adapters.Where(a => a.IsDefault).ToList();
        var adapter = defaults.Count == 1 ? defaults[0] : adapters.Count == 1 ? adapters[0] : null;
        return adapter != null && await ApplyDnsAsync(provider, adapter.Index, false, cancellationToken);
    }

    public static string BuildApplyScript(DnsProviderDefinition provider, int adapterIndex, bool useDoh)
    {
        if (adapterIndex <= 0) throw new ArgumentException("Select a network adapter.");
        string prefix = $"$index={adapterIndex}; ";
        if (provider.PrimaryDns.Equals("DHCP", StringComparison.OrdinalIgnoreCase))
            return prefix + "Get-DnsClientServerAddress -InterfaceIndex $index | Set-DnsClientServerAddress -ResetServerAddresses -ErrorAction Stop";
        string[] v4 = new[] { provider.PrimaryDns, provider.SecondaryDns }.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
        string[] v6 = new[] { provider.Primary6Dns, provider.Secondary6Dns }.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
        if (v4.Concat(v6).Any(ip => !SafeCommands.IsValidIp(ip))) throw new ArgumentException("Invalid DNS server address.");
        string quoted(IEnumerable<string> addresses) => string.Join(',', addresses.Select(ip => "'" + IPAddress.Parse(ip).ToString() + "'"));
        if (useDoh)
        {
            if (!Uri.TryCreate(provider.DohTemplate, UriKind.Absolute, out var uri) || uri.Scheme != "https") throw new ArgumentException("This provider has no HTTPS DNS template.");
            prefix += "if (-not (Get-Command Add-DnsClientDohServerAddress -ErrorAction SilentlyContinue)) { throw 'DNS over HTTPS is not supported on this Windows version.' }; ";
            foreach (string ip in v4.Concat(v6)) prefix += $"if (Get-DnsClientDohServerAddress -ServerAddress '{ip}' -ErrorAction SilentlyContinue) {{ Set-DnsClientDohServerAddress -ServerAddress '{ip}' -DohTemplate '{uri.AbsoluteUri.Replace("'", "''")}' -AutoUpgrade $true -AllowFallbackToUdp $false -ErrorAction Stop }} else {{ Add-DnsClientDohServerAddress -ServerAddress '{ip}' -DohTemplate '{uri.AbsoluteUri.Replace("'", "''")}' -AutoUpgrade $true -AllowFallbackToUdp $false -ErrorAction Stop }}; ";
        }
        prefix += $"Get-DnsClientServerAddress -InterfaceIndex $index -AddressFamily IPv4 | Set-DnsClientServerAddress -ServerAddresses @({quoted(v4)}) -ErrorAction Stop; ";
        if (v6.Length > 0) prefix += $"Get-DnsClientServerAddress -InterfaceIndex $index -AddressFamily IPv6 | Set-DnsClientServerAddress -ServerAddresses @({quoted(v6)}) -ErrorAction Stop; ";
        return prefix;
    }

    public async Task<bool> ApplyDnsAsync(DnsProviderDefinition provider, int adapterIndex, bool useDoh, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string script;
        try { script = BuildApplyScript(provider, adapterIndex, useDoh); } catch (ArgumentException) { return false; }
        var undo = new List<ActionItem>();
        if (_journal != null && !TestModeManager.IsTestMode)
        {
            string servers = string.Join(',', new[] { provider.PrimaryDns, provider.SecondaryDns, provider.Primary6Dns, provider.Secondary6Dns }.Where(SafeCommands.IsValidIp).Select(s => "'" + IPAddress.Parse(s) + "'"));
            string capture = $"$a=Get-NetAdapter | Where-Object ifIndex -eq {adapterIndex} | Select-Object -First 1; if(-not $a){{throw 'Network adapter is unavailable.'}}; $guid=([guid]$a.InterfaceGuid).ToString('B'); $families=@('IPv4','IPv6'); @($families | ForEach-Object {{ $family=$_; $stack=if($family -eq 'IPv4'){{'Tcpip'}}else{{'Tcpip6'}}; $key=\"HKLM:\\SYSTEM\\CurrentControlSet\\Services\\$stack\\Parameters\\Interfaces\\$guid\"; [pscustomobject]@{{Family=$family; Static=[string](Get-ItemProperty -LiteralPath $key -Name NameServer -ErrorAction SilentlyContinue).NameServer; Servers=@((Get-DnsClientServerAddress -InterfaceIndex {adapterIndex} -AddressFamily $family).ServerAddresses)}} }}) | ConvertTo-Json -Compress -Depth 5";
            var snapshot = await _shell.ExecutePowerShellAsync(capture, 30, cancellationToken);
            if (!snapshot.Success) return false;
            try
            {
                using var doc = JsonDocument.Parse(snapshot.StdOut);
                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    string family = item.GetProperty("Family").GetString() == "IPv4" ? "IPv4" : "IPv6";
                    string restore = $"Get-DnsClientServerAddress -InterfaceIndex {adapterIndex} -AddressFamily {family} | Set-DnsClientServerAddress ";
                    if (string.IsNullOrWhiteSpace(item.GetProperty("Static").GetString())) restore += "-ResetServerAddresses -ErrorAction Stop";
                    else
                    {
                        var addresses = item.GetProperty("Servers").EnumerateArray().Select(v => v.GetString() ?? "").ToArray();
                        if (addresses.Length == 0 || addresses.Any(ip => !SafeCommands.IsValidIp(ip))) return false;
                        restore += "-ServerAddresses @(" + string.Join(',', addresses.Select(ip => "'" + IPAddress.Parse(ip) + "'")) + ") -ErrorAction Stop";
                    }
                    undo.Add(ModuleJournal.UndoScript(restore));
                }
                if (useDoh && servers.Length > 0)
                {
                    var dohSnapshot = await _shell.ExecutePowerShellAsync($"@(@({servers}) | ForEach-Object {{ $ip=$_; $entry=Get-DnsClientDohServerAddress -ServerAddress $ip -ErrorAction SilentlyContinue; [pscustomobject]@{{IP=$ip; Exists=[bool]$entry; Template=[string]$entry.DohTemplate; Auto=[bool]$entry.AutoUpgrade; Fallback=[bool]$entry.AllowFallbackToUdp}} }}) | ConvertTo-Json -Compress", 30);
                    if (!dohSnapshot.Success) return false;
                    using var doh = JsonDocument.Parse(dohSnapshot.StdOut);
                    foreach (var item in doh.RootElement.EnumerateArray())
                    {
                        string ip = IPAddress.Parse(item.GetProperty("IP").GetString()!).ToString();
                        string restore = item.GetProperty("Exists").GetBoolean()
                            ? $"Set-DnsClientDohServerAddress -ServerAddress '{ip}' -DohTemplate '{(item.GetProperty("Template").GetString() ?? "").Replace("'", "''")}' -AutoUpgrade ${item.GetProperty("Auto").GetBoolean().ToString().ToLowerInvariant()} -AllowFallbackToUdp ${item.GetProperty("Fallback").GetBoolean().ToString().ToLowerInvariant()} -ErrorAction Stop"
                            : $"Remove-DnsClientDohServerAddress -ServerAddress '{ip}' -ErrorAction Stop";
                        undo.Add(ModuleJournal.UndoScript(restore));
                    }
                }
            }
            catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException or KeyNotFoundException) { return false; }
        }
        if (TestModeManager.IsTestMode) undo.Add(ModuleJournal.UndoScript("# Simulated DNS baseline"));
        var result = await ModuleJournal.RunScriptAsync(_shell, _journal, $"tools.dns.adapter-{adapterIndex}", script, undo, 60, cancellationToken);
        if (!result.Success || !_shell.SupportsSystemQueries) return result.Success;
        string? actual = await GetCurrentProviderIdAsync(adapterIndex, cancellationToken);
        return actual == provider.Id || (provider.Id == "custom" && result.Success);
    }
}
