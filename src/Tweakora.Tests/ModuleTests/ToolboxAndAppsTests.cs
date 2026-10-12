using Tweakora.Core.Fakes;
using Tweakora.Modules.Apps;
using Tweakora.Modules.Cleaner;
using Tweakora.Modules.Fixes;
using Tweakora.Modules.Network;
using Tweakora.Modules.Services;
using Tweakora.Modules.Startup;
using Tweakora.Modules.Uninstaller;
using Xunit;

namespace Tweakora.Tests.ModuleTests;

public class ToolboxAndAppsTests
{
    [Fact]
    public async Task SmartCleaner_ScanAndClean_Simulation_DoesNotThrow()
    {
        var cleaner = new SmartCleaner(isSimulationMode: true);
        var categories = await cleaner.ScanCategoriesAsync();

        Assert.NotEmpty(categories);
        Assert.All(categories, c => Assert.False(string.IsNullOrWhiteSpace(c.Name)));

        long cleaned = await cleaner.CleanCategoriesAsync(categories.Select(c => c.Id));
        Assert.True(cleaned >= 0);
    }

    [Fact]
    public void StartupManager_GetAndSetState_UsesRegistry()
    {
        var fakeReg = new FakeRegistryService();
        fakeReg.SetValue("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Run", "Discord", "Discord.exe", "String");

        var manager = new StartupManager(fakeReg);
        var entries = manager.GetStartupEntries();

        Assert.Contains(entries, e => e.Name == "Discord");

        var discord = entries.First(e => e.Name == "Discord");
        manager.SetEntryState(discord, false);

        Assert.False(discord.IsEnabled);
        Assert.False(fakeReg.ValueExists("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Run", "Discord"));
    }

    [Fact]
    public void ServicesManager_GetServices_ReturnsPopulatedList()
    {
        var fakeSvc = new FakeServicesService();
        fakeSvc.Services["wuauserv"] = ("Manual", "Running");
        var manager = new ServicesManager(fakeSvc);

        var services = manager.GetServices();
        Assert.NotEmpty(services);

        manager.ChangeStartupType("wuauserv", "Disabled");
        Assert.Equal("Disabled", fakeSvc.GetStartupType("wuauserv"));
    }

    [Fact]
    public async Task WinGetManager_SimulatedInstall_Succeeds()
    {
        var manager = new WinGetManager(catalogPath: null, isSimulationMode: true);
        var app = new WinGetApp
        {
            Id = "Mozilla.Firefox",
            Name = "Mozilla Firefox",
            Category = "Browsers",
            Description = "Fast private browser",
            Publisher = "Mozilla"
        };

        bool ok = await manager.InstallAppAsync(app);
        Assert.True(ok);
        Assert.True(app.IsInstalled);

        string script = WinGetManager.GenerateBatchInstallScript(new[] { app });
        Assert.Contains("winget install --id Mozilla.Firefox", script);
    }

    [Fact]
    public async Task AppUninstaller_ListAndUninstall_Simulation_Succeeds()
    {
        var fakeReg = new FakeRegistryService();
        var uninstaller = new AppUninstaller(fakeReg, isSimulationMode: true);

        var apps = uninstaller.GetInstalledApps();
        Assert.NotEmpty(apps);

        bool result = await uninstaller.UninstallAppAsync(apps.First());
        Assert.True(result);
    }

    [Fact]
    public async Task FixesCenter_RunFix_Simulation_Succeeds()
    {
        var center = new FixesCenter(isSimulationMode: true);
        var fixes = center.GetAvailableFixes();

        Assert.NotEmpty(fixes);
        var dnsFix = fixes.First(f => f.Id == "fix.dns.flush");

        bool result = await center.RunFixAsync(dnsFix);
        Assert.True(result);
    }

    [Fact]
    public async Task DnsSwitcher_ApplyDns_Simulation_Succeeds()
    {
        var switcher = new DnsSwitcher(isSimulationMode: true);
        var providers = switcher.GetProviders();

        Assert.NotEmpty(providers);
        var cloudflare = providers.First(p => p.Id == "dns.cloudflare.standard");

        bool applied = await switcher.ApplyDnsAsync(cloudflare);
        Assert.True(applied);

        int dhcpPing = await switcher.PingDnsAsync("DHCP");
        Assert.Equal(0, dhcpPing);
    }
}
