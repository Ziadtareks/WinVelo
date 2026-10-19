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
using WinVelo.Core.Operations;
using WinVelo.Core.Safety;
using WinVelo.Modules.Services;
using WinVelo.Modules.Updates;
using WinVelo.Simulation;
using Xunit;
using ExecutionContext = WinVelo.Core.Engine.ExecutionContext;

namespace WinVelo.Tests;

/// <summary>
/// Release smoke checks, run by the CI "smoke" job on Windows (dotnet test --filter Category=Smoke).
/// Everything runs on the simulation fakes with temporary folders: nothing touches the runner's real
/// registry, services, tasks or %APPDATA%.
/// </summary>
[Trait("Category", "Smoke")]
public sealed class SmokeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "WinVeloSmoke_" + Guid.NewGuid().ToString("N"));

    public SmokeTests()
    {
        TestModeManager.EnableTestMode();
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private sealed record Machine(FakeRegistryService Registry, FakeServicesService Services, FakeTaskSchedulerService Tasks, TweakRunner Runner);

    private Machine NewMachine()
    {
        var registry = new FakeRegistryService();
        var services = new FakeServicesService();
        var tasks = new FakeTaskSchedulerService();
        var context = new ExecutionContext
        {
            Registry = registry,
            Services = services,
            Tasks = tasks,
            Shell = new FakeShellService(),
            Journal = new RollbackJournalManager(Path.Combine(_dir, "Journal")),
            IsDryRun = false,
            BackupDirectory = Path.Combine(_dir, "Backups")
        };
        var handlers = new List<IActionHandler>
        {
            new RegistryActionHandler(), new ServiceActionHandler(), new TaskActionHandler(), new FileActionHandler(), new PowerShellActionHandler()
        };
        var runner = new TweakRunner(handlers, context, new FakeRestoreService(), new StateDetector(registry, services, tasks));
        return new Machine(registry, services, tasks, runner);
    }

    /// <summary>Every registry value, service and task of the fake PC. Empty registry keys are ignored.</summary>
    private static List<string> Snapshot(Machine m)
    {
        var lines = new List<string>();
        foreach (var (key, values) in m.Registry.Keys)
        {
            foreach (var (name, (data, kind)) in values)
            {
                string text = data switch
                {
                    byte[] b => Convert.ToBase64String(b),
                    string[] a => string.Join("\u0001", a),
                    _ => Convert.ToString(data, System.Globalization.CultureInfo.InvariantCulture) ?? ""
                };
                lines.Add($"reg|{key.ToUpperInvariant()}|{name.ToUpperInvariant()}|{kind}|{text}");
            }
        }
        lines.AddRange(m.Services.Services.Select(s => $"svc|{s.Key.ToUpperInvariant()}|{s.Value.Startup}|{s.Value.State}"));
        lines.AddRange(m.Tasks.Tasks.Select(t => $"task|{t.Key.ToUpperInvariant()}|{t.Value}"));
        lines.Sort(StringComparer.Ordinal);
        return lines;
    }

    private static List<TweakDefinition> Catalog() => CatalogLoader.LoadTweaks(CatalogSource.EmbeddedOnly()).Items;

    [Fact]
    public void EmbeddedCatalog_LoadsWithoutACatalogFolder()
    {
        var result = CatalogLoader.LoadTweaks(CatalogSource.EmbeddedOnly());
        Assert.Equal(140, result.Items.Count);
        Assert.Empty(result.Issues);
        Assert.Equal(6, CatalogLoader.LoadPresets(CatalogSource.EmbeddedOnly()).Items.Count);
    }

    [Fact]
    public async Task ApplyThenUndo_OneTweak_LeavesThePcExactlyAsBefore()
    {
        var m = NewMachine();
        var before = Snapshot(m);
        var tweak = Catalog().Single(t => t.Id == "ui.explorer.show-file-extensions");

        var report = await m.Runner.ApplyBatchAsync(new[] { tweak });
        Assert.Equal(TweakExecutionStatus.Applied, Assert.Single(report.Results).Status);
        Assert.NotEqual(before, Snapshot(m));

        Assert.True((await m.Runner.UndoTweakAsync(tweak)).Success);
        Assert.Equal(before, Snapshot(m));
    }

    [Theory]
    [InlineData(false)] // undo in reverse order
    [InlineData(true)]  // undo in the order they were applied
    public async Task ApplyThenUndo_PrivacyPreset_LeavesThePcExactlyAsBefore(bool sameOrder)
    {
        var m = NewMachine();
        var before = Snapshot(m);
        var catalog = Catalog().ToDictionary(t => t.Id, StringComparer.OrdinalIgnoreCase);
        var preset = CatalogLoader.LoadPresets(CatalogSource.EmbeddedOnly()).Items.Single(p => p.Id == "preset.privacy");
        var tweaks = preset.Standard.Select(id => catalog[id]).ToList();

        var report = await m.Runner.ApplyBatchAsync(tweaks);
        Assert.Equal(0, report.FailedCount);

        foreach (var tweak in sameOrder ? tweaks : Enumerable.Reverse(tweaks))
        {
            var undo = await m.Runner.UndoTweakAsync(tweak);
            Assert.True(undo.Success, $"{tweak.Id}: {undo.Message}");
        }

        Assert.Equal(before, Snapshot(m));
    }

    [Fact]
    public void JournalsFromVersion101AndTweakora_StillLoadAfterMigration()
    {
        string legacy = Path.Combine(_dir, "Tweakora");
        string current = Path.Combine(_dir, "WinVelo");
        Directory.CreateDirectory(Path.Combine(legacy, "Journal"));
        foreach (string fixture in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Fixtures"), "session-*.json"))
        {
            File.Copy(fixture, Path.Combine(legacy, "Journal", Path.GetFileName(fixture)));
        }

        Assert.True(AppDataMigrator.Migrate(legacy, current));
        var journal = new RollbackJournalManager(Path.Combine(current, "Journal"));
        var sessions = journal.GetAllSessions();

        Assert.Equal(2, sessions.Count);
        Assert.Empty(journal.DamagedFiles);
        Assert.Contains(sessions, s => s.AppVersion == "1.0.1" && s.Records.Single().PreviousData == "1");
        Assert.Contains(sessions, s => s.IsLegacy && s.Records.Single().TweakId == "privacy.advertising.advertising-id");
        Assert.True(Directory.Exists(legacy)); // the old folder is never deleted
    }

    [Theory]
    [InlineData("wuauserv")]
    [InlineData("BITS")]
    [InlineData("UsoSvc")]
    [InlineData("WaaSMedicSvc")]
    [InlineData("InstallService")]
    [InlineData("ClipSVC")]
    [InlineData("AppXSvc")]
    [InlineData("WinDefend")]
    [InlineData("SecurityHealthService")]
    [InlineData("wscsvc")]
    [InlineData("mpssvc")]
    public async Task ProtectedServices_AreRefusedEverywhere(string service)
    {
        Assert.NotNull(ServicingGuard.CheckService(service, "SetStartup", "Disabled"));
        Assert.NotNull(ServicingGuard.CheckService(service, "Stop", null));

        var services = new FakeServicesService();
        services.Services[service] = ("Manual", "Running");
        var tool = new ServicesManagerService(services);
        Assert.False(await tool.SetStartupTypeAsync(service, "Disabled"));
        Assert.False(await tool.ToggleServiceStateAsync(service, start: false));
        Assert.Equal(("Manual", "Running"), services.Services[service]);
    }

    [Fact]
    public async Task UpdatesDisabledMode_NeverDisablesUpdateServices()
    {
        var registry = new FakeRegistryService();
        var services = new FakeServicesService();
        foreach (string s in new[] { "wuauserv", "BITS", "UsoSvc" }) services.Services[s] = ("Manual", "Running");
        var updates = new UpdateModeService(registry, services, new FakeTaskSchedulerService());

        await updates.ApplyModeAsync(UpdateProfileMode.DisableAll, new OperationRunner());

        foreach (string s in new[] { "wuauserv", "BITS", "UsoSvc" })
        {
            Assert.NotEqual("Disabled", services.GetStartupType(s));
            Assert.Equal("Running", services.GetState(s));
        }
    }

    [Fact]
    public void EnglishAndArabic_HaveTheSameUiKeys()
    {
        string? dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir!, "WinVelo.sln"))) dir = Path.GetDirectoryName(dir);
        string loc = Path.Combine(dir!, "src", "WinVelo.App", "Localization");
        var en = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(loc, "Lang-en.json")))!;
        var ar = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(loc, "Lang-ar.json")))!;

        Assert.Empty(en.Keys.Where(k => !ar.ContainsKey(k)));
        Assert.Empty(ar.Keys.Where(k => System.Text.RegularExpressions.Regex.IsMatch(k, @"^[A-Za-z]+_[A-Za-z0-9_]+$") && !en.ContainsKey(k)));
    }
}
