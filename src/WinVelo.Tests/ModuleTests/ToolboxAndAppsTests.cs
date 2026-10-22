using System.Linq;
using System.Threading.Tasks;
using WinVelo.Core.Operations;
using WinVelo.Modules.Apps;
using WinVelo.Modules.Cleaner;
using WinVelo.Modules.Config;
using WinVelo.Modules.Network;
using WinVelo.Modules.Services;
using WinVelo.Modules.Startup;
using WinVelo.Modules.Updates;
using WinVelo.Simulation;
using Xunit;

namespace WinVelo.Tests.ModuleTests;

public class ToolboxAndAppsTests
{
    [Fact]
    public async Task CleanerService_ScanCategories_ReturnsWhitelistedLocations()
    {
        var fakeSvc = new FakeServicesService();
        var cleaner = new CleanerService(fakeSvc);

        var categories = await cleaner.ScanCategoriesAsync();

        Assert.NotEmpty(categories);
        Assert.All(categories, c => Assert.False(string.IsNullOrWhiteSpace(c.Name)));
        Assert.Contains(categories, c => c.Id == "UserTemp");
        Assert.Contains(categories, c => c.Id == "WindowsTemp");
        Assert.Contains(categories, c => c.Id == "UpdateCache");
    }

    [Fact]
    public async Task StartupService_GetAndSetState_UsesRegistry()
    {
        var fakeReg = new FakeRegistryService();
        fakeReg.SetValue("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Run", "Discord", "Discord.exe", "String");

        var service = new StartupService(fakeReg);
        var entries = await service.GetStartupEntriesAsync();

        Assert.Contains(entries, e => e.Name == "Discord");

        var discord = entries.First(e => e.Name == "Discord");
        bool ok = await service.SetEntryStateAsync(discord, false);

        Assert.True(ok);
        Assert.False(discord.IsEnabled);
        // Authentic Windows behavior: Run command is NOT destroyed
        Assert.True(fakeReg.ValueExists("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Run", "Discord"));
        // Authentic Windows behavior: StartupApproved has disabled flag set
        Assert.True(fakeReg.ValueExists("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run", "Discord"));
        var approvedBytes = fakeReg.GetValue("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run", "Discord") as byte[];
        Assert.NotNull(approvedBytes);
        Assert.Equal(0x01, approvedBytes[0] & 1); // Low bit = 1 indicates disabled
    }

    [Fact]
    public async Task ServicesManagerService_GetServices_ReturnsPopulatedList()
    {
        var fakeSvc = new FakeServicesService();
        fakeSvc.Services["Spooler"] = ("Manual", "Running");
        var service = new ServicesManagerService(fakeSvc);

        var services = service.GetServices();
        Assert.NotEmpty(services);

        // Protected services (wuauserv, BITS, ...) are covered by ServicesManager_RefusesToDisableOrStopProtectedServices.
        bool changed = await service.SetStartupTypeAsync("Spooler", "Disabled");
        Assert.True(changed);
        Assert.Equal("Disabled", fakeSvc.GetStartupType("Spooler"));

        bool stopped = await service.ToggleServiceStateAsync("Spooler", false);
        Assert.True(stopped);
        Assert.Equal("Stopped", fakeSvc.GetState("Spooler"));
    }

    [Fact]
    public async Task AppInstallService_InstallAndUninstall_UsesRunnerAndShell()
    {
        var fakeShell = new FakeShellService();
        var service = new AppInstallService(fakeShell);
        var runner = new OperationRunner();

        var apps = new[]
        {
            new ApplicationItem
            {
                Key = "Mozilla.Firefox",
                Name = "Mozilla Firefox",
                Category = "Browsers",
                WingetId = "Mozilla.Firefox",
                IsSelected = true
            }
        };

        var installReport = await service.InstallAppsAsync(apps, "WinGet", runner);
        Assert.Equal(1, installReport.SucceededCount);
        Assert.True(apps[0].IsInstalled);

        var uninstallReport = await service.UninstallAppsAsync(apps, "WinGet", runner);
        Assert.Equal(1, uninstallReport.SucceededCount);
        Assert.False(apps[0].IsInstalled);
    }

    [Fact]
    public async Task FixService_GetAvailableFixesAndPanels_ReturnsItems()
    {
        var fakeShell = new FakeShellService();
        var service = new FixService(fakeShell);
        var runner = new OperationRunner();

        var fixes = service.GetAvailableFixes();
        Assert.NotEmpty(fixes);
        Assert.Contains(fixes, f => f.Id == "dns_flush");

        var panels = service.GetLegacyApplets();
        Assert.Equal(14, panels.Count);

        var report = await service.RunFixAsync("dns_flush", runner);
        Assert.Equal(1, report.SucceededCount);
    }

    [Fact]
    public async Task DnsService_ApplyAndPing_Succeeds()
    {
        var fakeShell = new FakeShellService();
        var service = new DnsService(fakeShell);

        var providers = service.GetProviders();
        Assert.NotEmpty(providers);
        var cloudflare = providers.First(p => p.Id == "dns.cloudflare.standard");

        bool applied = await service.ApplyDnsAsync(cloudflare);
        Assert.True(applied);

        int dhcpPing = await service.PingDnsAsync("DHCP");
        Assert.Equal(-1, dhcpPing);
    }

    [Fact]
    public async Task UpdateModeService_ApplyModes_SetsRegistryAndServices()
    {
        var fakeReg = new FakeRegistryService();
        var fakeSvc = new FakeServicesService();
        var fakeTask = new FakeTaskSchedulerService();
        var service = new UpdateModeService(fakeReg, fakeSvc, fakeTask);
        var runner = new OperationRunner();

        // Apply Security mode
        var report = await service.ApplyModeAsync(UpdateProfileMode.Security, runner);
        Assert.Equal(1, report.SucceededCount);

        var mode = service.DetectCurrentMode();
        Assert.Equal(UpdateProfileMode.Security, mode);

        // Apply DisableAll mode
        await service.ApplyModeAsync(UpdateProfileMode.DisableAll, runner);
        Assert.Equal(UpdateProfileMode.DisableAll, service.DetectCurrentMode());
    }

    [Fact]
    public async Task UpdateModeService_DisableAll_OnlySetsThePolicy_AndRepairsDisabledServices()
    {
        var fakeReg = new FakeRegistryService();
        var fakeSvc = new FakeServicesService();
        fakeSvc.Services["wuauserv"] = ("Disabled", "Stopped"); // left behind by WinVelo 1.0.1's DisableAll
        fakeSvc.Services["BITS"] = ("Manual", "Running");
        var service = new UpdateModeService(fakeReg, fakeSvc, new FakeTaskSchedulerService());

        await service.ApplyModeAsync(UpdateProfileMode.DisableAll, new OperationRunner());

        Assert.Equal(1, fakeReg.GetValue("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "NoAutoUpdate"));
        Assert.Equal("Manual", fakeSvc.GetStartupType("wuauserv"));
        Assert.Equal("Manual", fakeSvc.GetStartupType("BITS"));
        Assert.Equal("Running", fakeSvc.GetState("BITS"));
    }

    [Fact]
    public async Task ServicesManager_RefusesToDisableOrStopProtectedServices()
    {
        var fakeSvc = new FakeServicesService();
        var manager = new ServicesManagerService(fakeSvc);

        Assert.False(await manager.SetStartupTypeAsync("wuauserv", "Disabled"));
        Assert.False(await manager.ToggleServiceStateAsync("BITS", start: false));
        Assert.Equal("Automatic", fakeSvc.GetStartupType("wuauserv"));
        Assert.Equal("Running", fakeSvc.GetState("BITS"));

        Assert.True(await manager.SetStartupTypeAsync("DiagTrack", "Disabled"));
    }
}
