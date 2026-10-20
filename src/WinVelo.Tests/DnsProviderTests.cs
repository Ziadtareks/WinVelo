using System.Linq;
using System.Threading.Tasks;
using WinVelo.Core.Utilities;
using WinVelo.Modules.Network;
using WinVelo.Simulation;
using Xunit;

namespace WinVelo.Tests;

/// <summary>DNS providers come from Catalog/Config/dns.json (WinUtil's 8) and are set for IPv4 and IPv6.</summary>
public sealed class DnsProviderTests
{
    [Fact]
    public void AllEightWinUtilProviders_PlusDhcp_AreOffered()
    {
        var providers = new DnsService(new FakeShellService()).GetProviders();
        string[] winutil = { "Google", "Cloudflare", "Cloudflare_Malware", "Cloudflare_Malware_Adult", "Open_DNS", "Quad9", "AdGuard_Ads_Trackers", "AdGuard_Ads_Trackers_Malware_Adult" };

        foreach (string key in winutil)
        {
            var p = DnsService.Find(providers, key);
            Assert.NotNull(p);
            Assert.True(SafeCommands.IsValidIp(p!.PrimaryDns6), $"{key} has no IPv6 server");
        }
        Assert.NotNull(DnsService.Find(providers, "dns.cloudflare.standard")); // old CLI ids keep working
        Assert.NotNull(DnsService.Find(providers, DnsService.DhcpId));
    }

    [Fact]
    public async Task Apply_SetsIpv4AndIpv6Servers()
    {
        var shell = new FakeShellService();
        var service = new DnsService(shell);
        var quad9 = DnsService.Find(service.GetProviders(), "Quad9")!;

        Assert.True(await service.ApplyDnsAsync(quad9));

        string script = shell.ExecutedScripts.Last();
        Assert.Contains("'9.9.9.9'", script);
        Assert.Contains("'149.112.112.112'", script);
        Assert.Contains("'2620:fe::fe'", script);
        Assert.Contains("'2620:fe::9'", script);
    }

    [Fact]
    public async Task Dhcp_ResetsTheServers()
    {
        var shell = new FakeShellService();
        var service = new DnsService(shell);
        Assert.True(await service.ApplyDnsAsync(DnsService.Find(service.GetProviders(), DnsService.DhcpId)!));
        Assert.Contains("-ResetServerAddresses", shell.ExecutedScripts.Last());
    }

    [Fact]
    public void OnlyNeutralResolvers_CanBePickedAutomatically()
    {
        var providers = new DnsService(new FakeShellService()).GetProviders();
        var neutral = providers.Where(p => p.IsNeutral).Select(p => p.UpstreamKey).OrderBy(k => k).ToArray();
        Assert.Equal(new[] { "Cloudflare", "Google" }, neutral);
    }

    [Fact]
    public void MatchActive_FindsTheProviderInUse()
    {
        var providers = new DnsService(new FakeShellService()).GetProviders();
        Assert.Equal("dns.quad9.secure", DnsService.MatchActive(providers, new[] { "9.9.9.9", "2620:fe::fe" })!.Id);
        Assert.Null(DnsService.MatchActive(providers, new[] { "192.168.1.1" }));
    }

    [Theory]
    [InlineData("1.1.1.1'; Remove-Item C:\\ -Recurse")]
    [InlineData("not-an-ip")]
    public void BadAddresses_NeverReachACommand(string bad)
    {
        Assert.Throws<System.ArgumentException>(() => SafeCommands.SetDnsServers(new[] { "1.1.1.1", bad }));
    }
}
