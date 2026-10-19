using System.Net;
using System.Text.RegularExpressions;

namespace WinVelo.Core.Utilities;

/// <summary>
/// Builds the few PowerShell commands that contain values from the catalog or from the user. Values are checked
/// against a strict format first and only their canonical form goes into the command, so a value can never add
/// its own commands (for example a DNS server "1.1.1.1'; Remove-Item ...").
/// </summary>
public static partial class SafeCommands
{
    private const string UpAdapters = "Get-NetAdapter | Where-Object Status -eq 'Up' | ";

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$")]
    private static partial Regex FeatureNameRegex();

    /// <summary>"DHCP"/"Automatic" (or an empty primary) resets to automatic DNS; otherwise one or two IP addresses.</summary>
    /// <exception cref="ArgumentException">An address is not a valid IPv4 or IPv6 address.</exception>
    public static string SetDnsServers(string? primary, string? secondary)
    {
        if (IsAutomatic(primary))
        {
            return UpAdapters + "Set-DnsClientServerAddress -ResetServerAddresses";
        }

        string first = CanonicalIp(primary!);
        if (IsAutomatic(secondary))
        {
            return UpAdapters + $"Set-DnsClientServerAddress -ServerAddresses '{first}'";
        }

        return UpAdapters + $"Set-DnsClientServerAddress -ServerAddresses '{first}', '{CanonicalIp(secondary!)}'";
    }

    /// <exception cref="ArgumentException">The name is not a plain Windows optional feature name.</exception>
    public static string OptionalFeature(bool enable, string featureName)
    {
        if (string.IsNullOrWhiteSpace(featureName) || !FeatureNameRegex().IsMatch(featureName))
        {
            throw new ArgumentException($"'{featureName}' is not a valid Windows feature name.", nameof(featureName));
        }

        return enable
            ? $"Enable-WindowsOptionalFeature -Online -FeatureName '{featureName}' -All -NoRestart"
            : $"Disable-WindowsOptionalFeature -Online -FeatureName '{featureName}' -NoRestart";
    }

    public static bool IsValidIp(string? value) => !string.IsNullOrWhiteSpace(value) && IPAddress.TryParse(value.Trim(), out _);

    private static bool IsAutomatic(string? value) =>
        string.IsNullOrWhiteSpace(value) ||
        value.Equals("DHCP", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("Automatic", StringComparison.OrdinalIgnoreCase);

    private static string CanonicalIp(string value)
    {
        if (!IPAddress.TryParse(value.Trim(), out IPAddress? address))
        {
            throw new ArgumentException($"'{value}' is not a valid IP address.", nameof(value));
        }
        return address.ToString();
    }
}
