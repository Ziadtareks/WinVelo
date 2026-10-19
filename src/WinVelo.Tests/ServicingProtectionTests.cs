using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using WinVelo.Core.Interfaces;
using WinVelo.Core.Models;
using WinVelo.Core.Safety;
using WinVelo.Modules.Tweaks;
using WinVelo.Simulation;
using Xunit;

namespace WinVelo.Tests;

public sealed class ServicingProtectionTests
{
    private readonly string _catalogDir;

    public ServicingProtectionTests()
    {
        string baseDir = AppContext.BaseDirectory;
        string? candidate = baseDir;
        string? found = null;
        while (candidate != null)
        {
            string p = Path.Combine(candidate, "Catalog");
            if (Directory.Exists(p))
            {
                found = p;
                break;
            }
            candidate = Directory.GetParent(candidate)?.FullName;
        }

        _catalogDir = found ?? Path.Combine(baseDir, "Catalog");
    }

    [Fact]
    public void ProtectedServices_ContainsAllFifteenRequiredServices()
    {
        var requiredServices = new[]
        {
            "wuauserv",
            "BITS",
            "UsoSvc",
            "WaaSMedicSvc",
            "DoSvc",
            "TrustedInstaller",
            "CryptSvc",
            "AppXSvc",
            "ClipSVC",
            "LicenseManager",
            "InstallService",
            "WinDefend",
            "SecurityHealthService",
            "wscsvc",
            "mpssvc"
        };

        foreach (var svc in requiredServices)
        {
            Assert.True(
                ServicingProtectionHelper.IsProtectedService(svc),
                $"Service '{svc}' should be recognized as a protected servicing service.");
        }
    }

    [Fact]
    public void Presets_DoNotContainAnyServicingImpactingTweaks()
    {
        string presetsDir = Path.Combine(_catalogDir, "Presets");
        string tweaksDir = Path.Combine(_catalogDir, "Tweaks");
        Assert.True(Directory.Exists(presetsDir), "Presets directory must exist.");

        var catalogTweaks = new Dictionary<string, TweakDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.GetFiles(tweaksDir, "*.json", SearchOption.AllDirectories))
        {
            var tweaks = JsonSerializer.Deserialize<List<TweakDefinition>>(File.ReadAllText(file), new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new List<TweakDefinition>();

            foreach (var t in tweaks)
            {
                catalogTweaks[t.Id] = t;
            }
        }

        foreach (var pFile in Directory.GetFiles(presetsDir, "*.json"))
        {
            var preset = JsonSerializer.Deserialize<PresetDefinition>(File.ReadAllText(pFile), new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            Assert.NotNull(preset);

            foreach (var tid in preset.TweakIds)
            {
                Assert.True(catalogTweaks.TryGetValue(tid, out var tweak), $"Tweak {tid} in preset {preset.Id} not found in catalog");

                // Presets must never contain High-risk tweaks
                Assert.NotEqual("High", tweak.Risk);

                // Presets must never contain tweaks that touch protected servicing
                foreach (var action in tweak.Apply)
                {
                    if (string.Equals(action.Type, "Service", StringComparison.OrdinalIgnoreCase))
                    {
                        Assert.False(
                            ServicingProtectionHelper.IsProtectedService(action.ServiceName ?? string.Empty),
                            $"Preset '{preset.Id}' contains tweak '{tid}' modifying protected service '{action.ServiceName}'");
                    }
                    else if (string.Equals(action.Type, "Registry", StringComparison.OrdinalIgnoreCase))
                    {
                        Assert.False(
                            ServicingProtectionHelper.IsProtectedPolicy(action.Key ?? string.Empty, action.ValueName ?? string.Empty),
                            $"Preset '{preset.Id}' contains tweak '{tid}' modifying protected policy '{action.Key}\\{action.ValueName}'");
                    }
                }
            }
        }
    }

    [Fact]
    public async Task CheckServicingHealth_WhenAllHealthy_ReturnsIsHealthyTrue()
    {
        var services = new FakeServicesService();
        var registry = new FakeRegistryService();

        // Ensure all protected services are present and set to healthy defaults
        foreach (var svc in ServicingProtectionConstants.ProtectedServices)
        {
            string def = ServicingProtectionConstants.ServiceDefaultStartups[svc];
            services.SetStartupType(svc, def);
        }

        var report = await ServicingProtectionHelper.CheckServicingHealthAsync(services, registry);

        Assert.True(report.IsHealthy);
        Assert.Empty(report.Warnings);
        Assert.Contains("Windows Update", report.SummaryText);
        Assert.Contains("BITS", report.SummaryText);
        Assert.Contains("Store", report.SummaryText);
        Assert.Contains("Defender", report.SummaryText);
        Assert.Contains("Windows Search", report.SummaryText);
    }

    [Fact]
    public async Task CheckServicingHealth_WhenServiceDisabled_ReportsCompromisedAndWarning()
    {
        var services = new FakeServicesService();
        var registry = new FakeRegistryService();

        // Disable Windows Update service
        services.SetStartupType("wuauserv", "Disabled");

        var report = await ServicingProtectionHelper.CheckServicingHealthAsync(services, registry);

        Assert.False(report.IsHealthy);
        Assert.Contains(report.Warnings, w => w.Contains("wuauserv") && w.Contains("Disabled"));
        var wuComp = report.Components.First(c => c.Name.Equals("wuauserv", StringComparison.OrdinalIgnoreCase));
        Assert.True(wuComp.IsCompromised);
    }

    [Fact]
    public async Task CheckServicingHealth_WhenBlockingPolicySet_ReportsCompromised()
    {
        var services = new FakeServicesService();
        var registry = new FakeRegistryService();

        // Set NoAutoUpdate policy to 1
        registry.SetValue("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "NoAutoUpdate", 1, "DWord");

        var report = await ServicingProtectionHelper.CheckServicingHealthAsync(services, registry);

        Assert.False(report.IsHealthy);
        Assert.Contains(report.Warnings, w => w.Contains("NoAutoUpdate"));
        var policyComp = report.Components.First(c => c.Name.Equals("NoAutoUpdate", StringComparison.OrdinalIgnoreCase));
        Assert.True(policyComp.IsCompromised);
    }

    [Fact]
    public async Task RestoreServicingComponents_RestoresDisabledServicesAndRemovesPolicies()
    {
        var services = new FakeServicesService();
        var registry = new FakeRegistryService();

        // Compromise BITS and Windows Store policy
        services.SetStartupType("BITS", "Disabled");
        registry.SetValue("HKLM", @"SOFTWARE\Policies\Microsoft\WindowsStore", "DisableStoreApps", 1, "DWord");

        var preReport = await ServicingProtectionHelper.CheckServicingHealthAsync(services, registry);
        Assert.False(preReport.IsHealthy);

        // Restore
        var restored = await ServicingProtectionHelper.RestoreServicingComponentsAsync(services, registry);
        Assert.NotEmpty(restored);

        var postReport = await ServicingProtectionHelper.CheckServicingHealthAsync(services, registry);
        Assert.True(postReport.IsHealthy);
        Assert.Empty(postReport.Warnings);
        Assert.NotEqual("Disabled", services.GetStartupType("BITS"));
        Assert.Null(registry.GetValue("HKLM", @"SOFTWARE\Policies\Microsoft\WindowsStore", "DisableStoreApps"));
    }

    [Fact]
    public void EssentialDisableStoreSearch_HasRiskHighAndSideEffects()
    {
        string tweaksDir = Path.Combine(_catalogDir, "Tweaks");
        string essentialPath = Path.Combine(tweaksDir, "essential.json");
        Assert.True(File.Exists(essentialPath), "essential.json must exist.");

        var tweaks = JsonSerializer.Deserialize<List<TweakDefinition>>(File.ReadAllText(essentialPath), new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new List<TweakDefinition>();

        var storeSearchTweak = tweaks.FirstOrDefault(t => t.Id == "essential.disablestoresearch");
        Assert.NotNull(storeSearchTweak);
        Assert.Equal("High", storeSearchTweak.Risk);
        Assert.False(string.IsNullOrWhiteSpace(storeSearchTweak.SideEffects));
        Assert.Contains("store.db", storeSearchTweak.SideEffects);
    }
}
