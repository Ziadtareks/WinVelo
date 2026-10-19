using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WinVelo.Core.Interfaces;

namespace WinVelo.Core.Safety;

public static class ServicingProtectionConstants
{
    public static readonly IReadOnlyList<string> ProtectedServices = new[]
    {
        "wuauserv",              // Windows Update Service
        "BITS",                  // Background Intelligent Transfer Service
        "UsoSvc",                // Update Orchestrator Service
        "WaaSMedicSvc",          // Windows Update Medic Service
        "DoSvc",                 // Delivery Optimization Service
        "TrustedInstaller",      // Windows Modules Installer
        "CryptSvc",              // Cryptographic Services
        "AppXSvc",               // AppX Deployment Service
        "ClipSVC",               // Client License Service
        "LicenseManager",        // Windows License Manager Service
        "InstallService",        // Microsoft Store Install Service
        "WinDefend",             // Microsoft Defender Antivirus Service
        "SecurityHealthService", // Windows Security Health Service
        "wscsvc",                // Security Center
        "mpssvc"                 // Windows Defender Firewall
    };

    public static readonly IReadOnlyList<string> ProtectedPolicies = new[]
    {
        "DisableWindowsUpdateAccess",
        "NoAutoUpdate",
        "DoNotConnectToWindowsUpdateInternetLocations",
        "DisableStoreApps",
        "RemoveWindowsStore",
        "RequirePrivateStoreOnly"
    };

    public static readonly IReadOnlyDictionary<string, string> ServiceDefaultStartups = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["wuauserv"] = "Manual",
        ["BITS"] = "Manual",
        ["UsoSvc"] = "Manual",
        ["WaaSMedicSvc"] = "Manual",
        ["DoSvc"] = "Automatic",
        ["TrustedInstaller"] = "Manual",
        ["CryptSvc"] = "Automatic",
        ["AppXSvc"] = "Manual",
        ["ClipSVC"] = "Manual",
        ["LicenseManager"] = "Manual",
        ["InstallService"] = "Manual",
        ["WinDefend"] = "Automatic",
        ["SecurityHealthService"] = "Manual",
        ["wscsvc"] = "Automatic",
        ["mpssvc"] = "Automatic"
    };

    public static readonly IReadOnlyDictionary<string, string> ServiceCategories = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["wuauserv"] = "Windows Update",
        ["BITS"] = "Windows Update",
        ["UsoSvc"] = "Windows Update",
        ["WaaSMedicSvc"] = "Windows Update",
        ["DoSvc"] = "Windows Update",
        ["TrustedInstaller"] = "System Servicing",
        ["CryptSvc"] = "System Servicing",
        ["AppXSvc"] = "Microsoft Store",
        ["ClipSVC"] = "Microsoft Store",
        ["LicenseManager"] = "Microsoft Store",
        ["InstallService"] = "Microsoft Store",
        ["WinDefend"] = "Windows Defender Security",
        ["SecurityHealthService"] = "Windows Defender Security",
        ["wscsvc"] = "Windows Defender Security",
        ["mpssvc"] = "Windows Defender Security",
        ["WSearch"] = "Windows Search"
    };
}

public sealed class ServicingComponentHealth
{
    public required string Name { get; init; }
    public required string Category { get; init; }
    public required string ExpectedState { get; init; }
    public required string ActualState { get; init; }
    public bool IsCompromised { get; init; }
    public string? IssueDescription { get; init; }
}

public sealed class ServicingHealthReport
{
    public bool IsHealthy => Components.All(c => !c.IsCompromised);
    public List<ServicingComponentHealth> Components { get; } = new();
    public List<string> Warnings { get; } = new();

    public string SummaryText
    {
        get
        {
            var parts = new List<string>();
            var wu = Components.FirstOrDefault(c => c.Name.Equals("wuauserv", StringComparison.OrdinalIgnoreCase));
            if (wu != null) parts.Add($"Windows Update: {wu.ActualState}");

            var bits = Components.FirstOrDefault(c => c.Name.Equals("BITS", StringComparison.OrdinalIgnoreCase));
            if (bits != null) parts.Add($"BITS: {bits.ActualState}");

            var store = Components.FirstOrDefault(c => c.Name.Equals("InstallService", StringComparison.OrdinalIgnoreCase));
            if (store != null) parts.Add($"Store: {store.ActualState}");

            var defender = Components.FirstOrDefault(c => c.Name.Equals("WinDefend", StringComparison.OrdinalIgnoreCase));
            if (defender != null) parts.Add($"Defender: {defender.ActualState}");

            var search = Components.FirstOrDefault(c => c.Name.Equals("WSearch", StringComparison.OrdinalIgnoreCase));
            if (search != null) parts.Add($"Windows Search: {search.ActualState}");

            return string.Join(" | ", parts);
        }
    }
}

public static class ServicingProtectionHelper
{
    public static bool IsProtectedService(string serviceName)
    {
        if (string.IsNullOrWhiteSpace(serviceName)) return false;
        return ServicingProtectionConstants.ProtectedServices.Any(s => string.Equals(s, serviceName, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsProtectedPolicy(string key, string valueName)
    {
        if (string.IsNullOrWhiteSpace(valueName)) return false;
        if (ServicingProtectionConstants.ProtectedPolicies.Any(p => string.Equals(p, valueName, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(key))
        {
            if (key.Contains("WindowsUpdate", StringComparison.OrdinalIgnoreCase) ||
                key.Contains("WindowsStore", StringComparison.OrdinalIgnoreCase))
            {
                return ServicingProtectionConstants.ProtectedPolicies.Any(p => string.Equals(p, valueName, StringComparison.OrdinalIgnoreCase));
            }
        }

        return false;
    }

    public static async Task<ServicingHealthReport> CheckServicingHealthAsync(ISystemServicesService services, IRegistryService registry)
    {
        var report = new ServicingHealthReport();
        await Task.Yield();

        // 1. Check all protected services
        foreach (var svcName in ServicingProtectionConstants.ProtectedServices)
        {
            string category = ServicingProtectionConstants.ServiceCategories.TryGetValue(svcName, out var cat) ? cat : "Servicing";
            string expected = ServicingProtectionConstants.ServiceDefaultStartups.TryGetValue(svcName, out var exp) ? exp : "Automatic";

            try
            {
                string actualStartup = services.GetStartupType(svcName);
                string actualState = services.GetState(svcName);
                bool isCompromised = string.Equals(actualStartup, "Disabled", StringComparison.OrdinalIgnoreCase);

                string issue = isCompromised
                    ? $"Service '{svcName}' is Disabled. This prevents {category} from functioning properly."
                    : string.Empty;

                if (isCompromised)
                {
                    report.Warnings.Add(issue);
                }

                report.Components.Add(new ServicingComponentHealth
                {
                    Name = svcName,
                    Category = category,
                    ExpectedState = expected,
                    ActualState = $"{actualStartup} ({actualState})",
                    IsCompromised = isCompromised,
                    IssueDescription = isCompromised ? issue : null
                });
            }
            catch (Exception ex)
            {
                report.Components.Add(new ServicingComponentHealth
                {
                    Name = svcName,
                    Category = category,
                    ExpectedState = expected,
                    ActualState = "Unknown",
                    IsCompromised = false,
                    IssueDescription = $"Could not query service: {ex.Message}"
                });
            }
        }

        // 2. Check Windows Search service state (informational / read-only)
        try
        {
            string wsearchStartup = services.GetStartupType("WSearch");
            string wsearchState = services.GetState("WSearch");
            report.Components.Add(new ServicingComponentHealth
            {
                Name = "WSearch",
                Category = "Windows Search",
                ExpectedState = "Automatic",
                ActualState = $"{wsearchStartup} ({wsearchState})",
                IsCompromised = false,
                IssueDescription = null
            });
        }
        catch (Exception ex)
        {
            report.Components.Add(new ServicingComponentHealth
            {
                Name = "WSearch",
                Category = "Windows Search",
                ExpectedState = "Automatic",
                ActualState = "Unknown",
                IsCompromised = false,
                IssueDescription = $"Could not query WSearch: {ex.Message}"
            });
        }

        // 3. Check protected registry policies
        var policyChecks = new[]
        {
            (@"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "NoAutoUpdate", "Windows Update"),
            (@"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "DisableWindowsUpdateAccess", "Windows Update"),
            (@"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "DoNotConnectToWindowsUpdateInternetLocations", "Windows Update"),
            (@"SOFTWARE\Policies\Microsoft\WindowsStore", "RemoveWindowsStore", "Microsoft Store"),
            (@"SOFTWARE\Policies\Microsoft\WindowsStore", "DisableStoreApps", "Microsoft Store")
        };

        foreach (var (key, valueName, category) in policyChecks)
        {
            try
            {
                object? val = registry.GetValue("HKLM", key, valueName);
                bool isBlocked = val is int i && i == 1 || (val is string s && s == "1");

                if (isBlocked)
                {
                    string issue = $"Policy 'HKLM\\{key}\\{valueName}' is set to 1. This blocks {category}.";
                    report.Warnings.Add(issue);
                    report.Components.Add(new ServicingComponentHealth
                    {
                        Name = valueName,
                        Category = category,
                        ExpectedState = "Not Set (0)",
                        ActualState = "1 (Blocked)",
                        IsCompromised = true,
                        IssueDescription = issue
                    });
                }
            }
            catch { }
        }

        return report;
    }

    public static async Task<List<string>> RestoreServicingComponentsAsync(ISystemServicesService services, IRegistryService registry)
    {
        var restored = new List<string>();
        await Task.Yield();

        foreach (var svcName in ServicingProtectionConstants.ProtectedServices)
        {
            try
            {
                string actualStartup = services.GetStartupType(svcName);
                if (string.Equals(actualStartup, "Disabled", StringComparison.OrdinalIgnoreCase))
                {
                    string target = ServicingProtectionConstants.ServiceDefaultStartups.TryGetValue(svcName, out var def) ? def : "Manual";
                    services.SetStartupType(svcName, target);
                    restored.Add($"Restored service '{svcName}' startup to {target}");
                }
            }
            catch { }
        }

        var policyDeletes = new[]
        {
            (@"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "NoAutoUpdate"),
            (@"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "DisableWindowsUpdateAccess"),
            (@"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "DoNotConnectToWindowsUpdateInternetLocations"),
            (@"SOFTWARE\Policies\Microsoft\WindowsStore", "RemoveWindowsStore"),
            (@"SOFTWARE\Policies\Microsoft\WindowsStore", "DisableStoreApps")
        };

        foreach (var (key, valueName) in policyDeletes)
        {
            try
            {
                object? val = registry.GetValue("HKLM", key, valueName);
                if (val != null)
                {
                    registry.DeleteValue("HKLM", key, valueName);
                    restored.Add($"Removed blocking policy 'HKLM\\{key}\\{valueName}'");
                }
            }
            catch { }
        }

        return restored;
    }
}
