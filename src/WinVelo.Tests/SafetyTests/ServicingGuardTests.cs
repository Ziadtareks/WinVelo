using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using WinVelo.Core.Catalog;
using WinVelo.Core.Engine;
using WinVelo.Core.Engine.Handlers;
using WinVelo.Core.Models;
using WinVelo.Core.Safety;
using WinVelo.Simulation;
using Xunit;
using ExecutionContext = WinVelo.Core.Engine.ExecutionContext;

namespace WinVelo.Tests.SafetyTests;

/// <summary>Windows Update, BITS, the Store and Windows Security stay on, whatever the catalog says.</summary>
public sealed class ServicingGuardTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "WinVeloGuard_" + Guid.NewGuid().ToString("N"));

    public ServicingGuardTests()
    {
        TestModeManager.EnableTestMode();
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static JsonElement Json(string raw) => JsonDocument.Parse(raw).RootElement;

    [Theory]
    [InlineData("wuauserv", "SetStartup", "Disabled", true)]
    [InlineData("BITS", "SetStartup", "Disabled", true)]
    [InlineData("WinDefend", "SetStartup", "Manual", true)]   // default is Automatic
    [InlineData("mpssvc", "Stop", null, true)]
    [InlineData("WUAUSERV", "SetStartup", "Disabled", true)] // case does not matter
    [InlineData("wuauserv", "SetStartup", "Manual", false)]  // its default
    [InlineData("WinDefend", "SetStartup", "Automatic", false)]
    [InlineData("DiagTrack", "SetStartup", "Disabled", false)] // not protected
    public void Services(string service, string operation, string? startup, bool blocked)
    {
        Assert.Equal(blocked, ServicingGuard.CheckService(service, operation, startup) != null);
    }

    [Theory]
    [InlineData(@"SYSTEM\CurrentControlSet\Services\wuauserv", "Start", "Set", "4", true)]
    [InlineData(@"SYSTEM\ControlSet001\Services\BITS", "Start", "Set", "4", true)]
    [InlineData(@"SYSTEM\CurrentControlSet\Services\WinDefend", "Start", "Set", "3", true)]
    [InlineData(@"SYSTEM\CurrentControlSet\Services\wuauserv", null, "DeleteKey", "0", true)]
    [InlineData(@"SYSTEM\CurrentControlSet\Services\wuauserv", "Start", "Set", "3", false)]
    [InlineData(@"SYSTEM\CurrentControlSet\Services\DiagTrack", "Start", "Set", "4", false)]
    [InlineData(@"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "NoAutoUpdate", "Set", "1", true)]
    [InlineData(@"SOFTWARE\Policies\Microsoft\WindowsStore", "RemoveWindowsStore", "Set", "1", true)]
    [InlineData(@"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "NoAutoUpdate", "Set", "0", false)]
    [InlineData(@"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "NoAutoUpdate", "DeleteValue", "0", false)]
    public void Registry(string key, string? valueName, string operation, string data, bool blocked)
    {
        Assert.Equal(blocked, ServicingGuard.CheckRegistry("HKLM", key, valueName, operation, Json(data)) != null);
    }

    [Theory]
    [InlineData("Stop-Service -Name wuauserv -Force", true)]
    [InlineData("Set-Service -Name BITS -StartupType Disabled", true)]
    [InlineData("sc.exe config WinDefend start= disabled", true)]
    [InlineData("Get-Service wuauserv | Stop-Service", true)]
    [InlineData("Set-ItemProperty 'HKLM:\\SYSTEM\\CurrentControlSet\\Services\\wuauserv' -Name Start -Value 4", true)]
    [InlineData("Stop-Service -Name DiagTrack -Force", false)]
    [InlineData("Get-Service wuauserv", false)]
    public void Scripts(string script, bool blocked)
    {
        Assert.Equal(blocked, ServicingGuard.CheckScript(script) != null);
    }

    [Fact]
    public void BuiltInCatalog_HasNoBlockedTweaks()
    {
        var result = CatalogLoader.LoadTweaks(CatalogSource.EmbeddedOnly());
        Assert.DoesNotContain(result.Issues, i => i.Message.Contains("Windows Update", StringComparison.Ordinal));
        Assert.All(result.Items, t => Assert.Empty(ServicingGuard.CheckTweak(t)));
    }

    [Fact]
    public void MaliciousCatalogEntry_IsNotLoaded()
    {
        string tweaks = Path.Combine(_dir, "Catalog", "Tweaks");
        Directory.CreateDirectory(tweaks);
        File.WriteAllText(Path.Combine(tweaks, "evil.json"), """
            [
              { "id": "evil.disable-updates", "name": "Speed up PC", "category": "performance", "risk": "Safe",
                "apply": [ { "type": "Service", "serviceName": "wuauserv", "operation": "SetStartup", "startupType": "Disabled" } ],
                "undo":  [ { "type": "Service", "serviceName": "wuauserv", "operation": "SetStartup", "startupType": "Manual" } ] },
              { "id": "evil.script", "name": "Clean", "category": "performance", "risk": "Safe",
                "apply": [ { "type": "PowerShell", "script": "Stop-Service -Name WinDefend -Force" } ], "undo": [] },
              { "id": "fine.tweak", "name": "Fine", "category": "ui", "risk": "Safe", "apply": [], "undo": [] }
            ]
            """);

        var result = CatalogLoader.LoadTweaks(CatalogSource.FromFolderOnly(Path.Combine(_dir, "Catalog")));

        Assert.Equal(new[] { "fine.tweak" }, result.Items.Select(t => t.Id).ToArray());
        Assert.Contains(result.Issues, i => i.ItemId == "evil.disable-updates");
        Assert.Contains(result.Issues, i => i.ItemId == "evil.script");
    }

    [Fact]
    public async Task Handlers_RefuseProtectedChanges_EvenWhenCalledDirectly()
    {
        var services = new FakeServicesService();
        var registry = new FakeRegistryService();
        var shell = new FakeShellService();
        var context = new ExecutionContext
        {
            Registry = registry,
            Services = services,
            Tasks = new FakeTaskSchedulerService(),
            Shell = shell,
            Journal = new RollbackJournalManager(_dir),
            IsDryRun = false
        };

        var service = await new ServiceActionHandler().ExecuteAsync(
            new ActionItem { Type = "Service", ServiceName = "wuauserv", Operation = "SetStartup", StartupType = "Disabled" }, "t", 0, context);
        var reg = await new RegistryActionHandler().ExecuteAsync(
            new ActionItem { Type = "Registry", Hive = "HKLM", Key = @"SYSTEM\CurrentControlSet\Services\BITS", ValueName = "Start", ValueKind = "DWord", Operation = "Set", Data = Json("4") }, "t", 0, context);
        var script = await new PowerShellActionHandler().ExecuteAsync(
            new ActionItem { Type = "PowerShell", Script = "Stop-Service -Name mpssvc -Force" }, "t", 0, context);

        Assert.False(service.Success);
        Assert.False(reg.Success);
        Assert.False(script.Success);
        Assert.False(registry.ValueExists("HKLM", @"SYSTEM\CurrentControlSet\Services\BITS", "Start"));
        Assert.Empty(shell.ExecutedScripts);
        Assert.NotEqual("Disabled", services.GetStartupType("wuauserv"));
    }
}
