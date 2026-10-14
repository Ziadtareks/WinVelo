using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Tweakora.Core.Engine;
using Tweakora.Core.Engine.Handlers;
using Tweakora.Core.Models;
using Tweakora.Simulation;
using Xunit;
using ExecutionContext = Tweakora.Core.Engine.ExecutionContext;

namespace Tweakora.Tests.EngineTests;

public sealed class RealRegistrySyncTests
{
    private readonly FakeRegistryService _registry = new();
    private readonly FakeServicesService _services = new();
    private readonly FakeTaskSchedulerService _tasks = new();
    private readonly FakeShellService _shell = new();
    private readonly FakeRestoreService _restore = new();
    private readonly StateDetector _detector;
    private readonly TweakRunner _runner;
    private readonly List<TweakDefinition> _catalog;

    public RealRegistrySyncTests()
    {
        _detector = new StateDetector(_registry, _services, _tasks);

        var handlers = new List<IActionHandler>
        {
            new RegistryActionHandler(),
            new ServiceActionHandler(),
            new TaskActionHandler(),
            new FileActionHandler(),
            new PowerShellActionHandler()
        };

        string tempJournalDir = Path.Combine(Path.GetTempPath(), "WinVelo_SyncTest_Journal_" + Guid.NewGuid());
        var journal = new RollbackJournalManager(tempJournalDir);

        var context = new ExecutionContext
        {
            Registry = _registry,
            Services = _services,
            Tasks = _tasks,
            Shell = _shell,
            Journal = journal,
            IsDryRun = false,
            BackupDirectory = Path.Combine(Path.GetTempPath(), "WinVelo_SyncTest_Backups")
        };

        _runner = new TweakRunner(handlers, context, _restore, _detector);
        _catalog = LoadCatalog();
    }

    private static List<TweakDefinition> LoadCatalog()
    {
        string current = AppContext.BaseDirectory;
        string? catalogDir = null;
        while (!string.IsNullOrEmpty(current))
        {
            string candidate = Path.Combine(current, "Catalog");
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "schema", "tweak.schema.json")))
            {
                catalogDir = candidate;
                break;
            }
            string? parent = Directory.GetParent(current)?.FullName;
            if (parent == current) break;
            current = parent ?? string.Empty;
        }

        if (catalogDir == null) throw new InvalidOperationException("Could not find Catalog directory.");

        var list = new List<TweakDefinition>();
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        foreach (string file in Directory.GetFiles(Path.Combine(catalogDir, "Tweaks"), "*.json"))
        {
            string json = File.ReadAllText(file);
            var items = JsonSerializer.Deserialize<List<TweakDefinition>>(json, options);
            if (items != null) list.AddRange(items);
        }
        return list;
    }

    private TweakDefinition GetTweak(string idOrOrigin)
    {
        var tweak = _catalog.FirstOrDefault(t =>
            string.Equals(t.Id, idOrOrigin, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(t.Origin, idOrOrigin, StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(tweak);
        return tweak;
    }

    [Fact]
    public async Task DarkMode_ExternalRegistrySync_DetectsChangeImmediately()
    {
        var tweak = GetTweak("pref.darkmode");

        // 1. External change: Turn Dark Mode ON in Windows Settings
        _registry.SetValue("HKCU", "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize", "AppsUseLightTheme", 0, "DWord");
        _registry.SetValue("HKCU", "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize", "SystemUsesLightTheme", 0, "DWord");

        bool active = await _detector.IsTweakActiveAsync(tweak, useCache: false);
        Assert.True(active, "WinVelo must detect Dark Mode as active when Windows Settings writes AppsUseLightTheme = 0");

        // 2. External change: Turn Dark Mode OFF in Windows Settings
        _registry.SetValue("HKCU", "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize", "AppsUseLightTheme", 1, "DWord");
        _registry.SetValue("HKCU", "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize", "SystemUsesLightTheme", 1, "DWord");

        active = await _detector.IsTweakActiveAsync(tweak, useCache: false);
        Assert.False(active, "WinVelo must detect Dark Mode as inactive when Windows Settings writes AppsUseLightTheme = 1");

        // 3. WinVelo Apply: should set to 0
        var report = await _runner.ApplyBatchAsync(new[] { tweak });
        Assert.Equal(1, report.AppliedCount);
        active = await _detector.IsTweakActiveAsync(tweak, useCache: false);
        Assert.True(active);

        // 4. WinVelo Undo: should revert to 1
        var undoRes = await _runner.UndoTweakAsync(tweak);
        Assert.True(undoRes.Success);
        active = await _detector.IsTweakActiveAsync(tweak, useCache: false);
        Assert.False(active);
    }

    [Fact]
    public async Task FileExtensions_ExternalRegistrySync_DetectsChangeImmediately()
    {
        var tweak = GetTweak("pref.showext");

        // 1. External change: Explorer folder options shows extensions (HideFileExt = 0)
        _registry.SetValue("HKCU", "Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced", "HideFileExt", 0, "DWord");
        bool active = await _detector.IsTweakActiveAsync(tweak, useCache: false);
        Assert.True(active);

        // 2. External change: Explorer folder options hides extensions (HideFileExt = 1)
        _registry.SetValue("HKCU", "Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced", "HideFileExt", 1, "DWord");
        active = await _detector.IsTweakActiveAsync(tweak, useCache: false);
        Assert.False(active);

        // 3. WinVelo Apply -> sets 0
        await _runner.ApplyBatchAsync(new[] { tweak });
        active = await _detector.IsTweakActiveAsync(tweak, useCache: false);
        Assert.True(active);

        // 4. WinVelo Undo -> sets 1
        await _runner.UndoTweakAsync(tweak);
        active = await _detector.IsTweakActiveAsync(tweak, useCache: false);
        Assert.False(active);
    }

    [Fact]
    public async Task EndTaskOnTaskbar_ExternalRegistrySync_DetectsChangeImmediately()
    {
        var tweak = GetTweak("essential.endtaskontaskbar");

        _registry.SetValue("HKCU", "Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced\\TaskbarDeveloperSettings", "TaskbarEndTask", 1, "DWord");
        bool active = await _detector.IsTweakActiveAsync(tweak, useCache: false);
        Assert.True(active);

        _registry.SetValue("HKCU", "Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced\\TaskbarDeveloperSettings", "TaskbarEndTask", 0, "DWord");
        active = await _detector.IsTweakActiveAsync(tweak, useCache: false);
        Assert.False(active);
    }

    [Fact]
    public async Task ClassicContextMenu_KeyExistsDetection_SyncsWithoutFakeMarkers()
    {
        var tweak = GetTweak("advanced.rightclickmenu");

        // 1. External change: Key is created
        _registry.CreateKey("HKCU", "Software\\Classes\\CLSID\\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\\InprocServer32");
        bool active = await _detector.IsTweakActiveAsync(tweak, useCache: false);
        Assert.True(active, "Must detect classic menu when InprocServer32 key exists");

        // 2. External change: Key is deleted
        _registry.DeleteKey("HKCU", "Software\\Classes\\CLSID\\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\\InprocServer32");
        _registry.DeleteKey("HKCU", "Software\\Classes\\CLSID\\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}");
        active = await _detector.IsTweakActiveAsync(tweak, useCache: false);
        Assert.False(active, "Must detect classic menu as inactive when key is removed");

        // 3. WinVelo Apply -> Creates key
        await _runner.ApplyBatchAsync(new[] { tweak });
        active = await _detector.IsTweakActiveAsync(tweak, useCache: false);
        Assert.True(active);

        // 4. WinVelo Undo -> Deletes key
        await _runner.UndoTweakAsync(tweak);
        active = await _detector.IsTweakActiveAsync(tweak, useCache: false);
        Assert.False(active);
    }

    [Fact]
    public async Task DisableBingSearch_ExternalRegistrySync_DetectsChangeImmediately()
    {
        var tweak = GetTweak("pref.bingsearch");

        // 1. External change: Bing disabled via policy
        _registry.SetValue("HKCU", "Software\\Policies\\Microsoft\\Windows\\Explorer", "DisableSearchBoxSuggestions", 1, "DWord");
        bool active = await _detector.IsTweakActiveAsync(tweak, useCache: false);
        Assert.True(active);

        // 2. External change: Re-enabled
        _registry.SetValue("HKCU", "Software\\Policies\\Microsoft\\Windows\\Explorer", "DisableSearchBoxSuggestions", 0, "DWord");
        _registry.SetValue("HKCU", "Software\\Microsoft\\Windows\\CurrentVersion\\Search", "BingSearchEnabled", 1, "DWord");
        active = await _detector.IsTweakActiveAsync(tweak, useCache: false);
        Assert.False(active);
    }

    [Fact]
    public async Task DisableBackgroundApps_ExternalRegistrySync_DetectsChangeImmediately()
    {
        var tweak = GetTweak("advanced.disablebgapps");

        _registry.SetValue("HKCU", "Software\\Microsoft\\Windows\\CurrentVersion\\BackgroundAccessApplications", "GlobalUserDisabled", 1, "DWord");
        bool active = await _detector.IsTweakActiveAsync(tweak, useCache: false);
        Assert.True(active);

        _registry.SetValue("HKCU", "Software\\Microsoft\\Windows\\CurrentVersion\\BackgroundAccessApplications", "GlobalUserDisabled", 0, "DWord");
        active = await _detector.IsTweakActiveAsync(tweak, useCache: false);
        Assert.False(active);
    }

    [Fact]
    public async Task DisableSpotlightAds_ExternalRegistrySync_DetectsChangeImmediately()
    {
        var tweak = GetTweak("privacy.spotlight.lockscreen-ads");

        _registry.SetValue("HKCU", "Software\\Microsoft\\Windows\\CurrentVersion\\ContentDeliveryManager", "RotatingLockScreenOverlayEnabled", 0, "DWord");
        bool active = await _detector.IsTweakActiveAsync(tweak, useCache: false);
        Assert.True(active);

        _registry.SetValue("HKCU", "Software\\Microsoft\\Windows\\CurrentVersion\\ContentDeliveryManager", "RotatingLockScreenOverlayEnabled", 1, "DWord");
        active = await _detector.IsTweakActiveAsync(tweak, useCache: false);
        Assert.False(active);
    }

    [Fact]
    public async Task DisableTelemetry_ExternalRegistrySync_DetectsChangeImmediately()
    {
        var tweak = GetTweak("essential.telemetry");

        // 1. Disabling telemetry in policy
        _registry.SetValue("HKLM", "SOFTWARE\\Policies\\Microsoft\\Windows\\DataCollection", "AllowTelemetry", 0, "DWord");
        bool active = await _detector.IsTweakActiveAsync(tweak, useCache: false);
        Assert.True(active);

        // 2. Default state: telemetry allowed
        _registry.SetValue("HKLM", "SOFTWARE\\Policies\\Microsoft\\Windows\\DataCollection", "AllowTelemetry", 1, "DWord");
        _registry.SetValue("HKLM", "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\DataCollection", "AllowTelemetry", 1, "DWord");
        _registry.SetValue("HKCU", "Software\\Microsoft\\Windows\\CurrentVersion\\AdvertisingInfo", "Enabled", 1, "DWord");
        active = await _detector.IsTweakActiveAsync(tweak, useCache: false);
        Assert.False(active);
    }
}
