using System;
using WinVelo.Core.Utilities;
using Xunit;

namespace WinVelo.Tests.SafetyTests;

/// <summary>Values from the catalog or the user can never add their own PowerShell commands.</summary>
public sealed class SafeCommandsTests
{
    [Theory]
    [InlineData("1.1.1.1", "1.0.0.1", "'1.1.1.1', '1.0.0.1'")]
    [InlineData(" 9.9.9.9 ", null, "'9.9.9.9'")]
    [InlineData("2606:4700:4700::1111", "", "'2606:4700:4700::1111'")]
    public void Dns_ValidAddresses_AreQuotedCanonically(string primary, string? secondary, string expected)
    {
        Assert.EndsWith("-ServerAddresses " + expected, SafeCommands.SetDnsServers(primary, secondary));
    }

    [Theory]
    [InlineData("DHCP")]
    [InlineData("Automatic")]
    [InlineData("")]
    public void Dns_Automatic_ResetsServers(string primary)
    {
        Assert.EndsWith("-ResetServerAddresses", SafeCommands.SetDnsServers(primary, null));
    }

    [Theory]
    [InlineData("1.1.1.1'; Remove-Item C:\\ -Recurse; '", null)]
    [InlineData("1.1.1.1", "8.8.8.8$(calc)")]
    [InlineData("dns.google", null)]
    public void Dns_Injection_IsRejected(string primary, string? secondary)
    {
        Assert.Throws<ArgumentException>(() => SafeCommands.SetDnsServers(primary, secondary));
    }

    [Fact]
    public void OptionalFeature_OnlyPlainNames()
    {
        Assert.Contains("-FeatureName 'Microsoft-Windows-Subsystem-Linux'", SafeCommands.OptionalFeature(true, "Microsoft-Windows-Subsystem-Linux"));
        Assert.Contains("Disable-WindowsOptionalFeature", SafeCommands.OptionalFeature(false, "NetFx3"));
        Assert.Throws<ArgumentException>(() => SafeCommands.OptionalFeature(true, "NetFx3'; calc; '"));
        Assert.Throws<ArgumentException>(() => SafeCommands.OptionalFeature(true, "a b"));
    }
}
