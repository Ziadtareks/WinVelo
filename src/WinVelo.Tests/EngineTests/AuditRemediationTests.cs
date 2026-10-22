using System.Text.Json;
using WinVelo.Core.Engine;
using WinVelo.Core.Engine.Handlers;
using WinVelo.Core.Engine.Runners;
using WinVelo.Core.Interfaces;
using WinVelo.Core.Models;
using WinVelo.Core.Operations;
using WinVelo.Core.Profiles;
using WinVelo.Core.Safety;
using WinVelo.Modules.Apps;
using WinVelo.Modules.Cleaner;
using WinVelo.Modules.Config;
using WinVelo.Modules.Network;
using WinVelo.Modules.Tweaks;
using WinVelo.Simulation;
using Xunit;
using Ctx = WinVelo.Core.Engine.ExecutionContext;

namespace WinVelo.Tests.EngineTests;

public sealed class AuditRemediationTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "WinVeloRemediation", Guid.NewGuid().ToString("N"));
    private readonly FakeRegistryService _registry = new();
    private readonly FakeServicesService _services = new();
    private readonly FakeTaskSchedulerService _tasks = new();
    private readonly FakeShellService _shell = new();
    private Ctx Context(ITaskSchedulerService? tasks = null) => new()
    { Registry = _registry, Services = _services, Tasks = tasks ?? _tasks, Shell = _shell, Journal = new RollbackJournalManager(_directory) };
    private TweakRunner Runner(Ctx context, params IActionHandler[] handlers) => new(handlers.Length == 0 ? new IActionHandler[] { new RegistryActionHandler(), new ServiceActionHandler(), new TaskActionHandler(), new PowerShellActionHandler() } : handlers, context, new FakeRestoreService(), new StateDetector(_registry, _services, context.Tasks, _shell));
    private static ActionItem Script(string value) => new() { Type = "PowerShell", Operation = "Execute", Script = value };
    private static ActionItem Set(string name, int data) => new() { Type = "Registry", Operation = "Set", Hive = "HKCU", Key = "Software\\WinVeloAudit", ValueName = name, ValueKind = "DWord", Data = JsonSerializer.SerializeToElement(data) };
    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }

    [Fact]
    public async Task ScriptRollback_SurvivesRestartWithoutCatalogDefinition()
    {
        var ctx = Context(); var tweak = new TweakDefinition { Id = "audit.script", Apply = [Script("apply-sentinel")], Undo = [Script("undo-sentinel")] };
        Assert.Equal(1, (await Runner(ctx).ApplyBatchAsync([tweak])).AppliedCount);
        var restarted = Context(); var session = Assert.Single(restarted.Journal.GetAllSessions());
        Assert.True((await Runner(restarted).RollbackSessionAsync(session)).Success);
        Assert.Contains("undo-sentinel", _shell.ExecutedScripts);
        Assert.All(restarted.Journal.GetAllSessions().SelectMany(s => s.Records), r => Assert.True(r.IsReverted));
    }
    [Fact]
    public async Task FailedScriptUndo_RemainsRetryable()
    {
        var ctx = Context(); var tweak = new TweakDefinition { Id = "audit.retry", Apply = [Script("apply")], Undo = [Script("undo")] };
        await Runner(ctx).ApplyBatchAsync([tweak]);
        _shell.ScriptResultFactory = (script, _) => new() { ExitCode = script == "undo" ? 1 : 0, StdErr = "Denied", StdOut = "", TimedOut = false, Duration = TimeSpan.Zero };
        var session = Assert.Single(ctx.Journal.GetAllSessions());
        Assert.False((await Runner(ctx).RollbackSessionAsync(session)).Success);
        Assert.Contains(ctx.Journal.GetAllSessions().SelectMany(s => s.Records), r => !r.IsReverted);
        _shell.ScriptResultFactory = null;
        Assert.True((await Runner(ctx).RollbackSessionAsync(session)).Success);
    }
    [Fact]
    public async Task FailedTaskMutationAndUndo_NeverReturnSuccess()
    {
        var ctx = Context(new DeniedTasks()); ctx.Journal.StartSession();
        var handler = new TaskActionHandler();
        var result = await handler.ExecuteAsync(new() { Type = "Task", Operation = "Disable", TaskName = "Probe", TaskPath = "\\" }, "audit.task", 0, ctx);
        Assert.False(result.Success);
        Assert.False((await handler.UndoAsync(new() { TweakId = "audit.task", ActionType = "ScheduledTask", TargetIdentifier = "\\Probe", PreviousData = "True" }, ctx)).Success);
    }
    [Fact]
    public async Task MixedMarker_DoesNotSkipRequiredScript()
    {
        _registry.SetValue("HKCU", "Software\\WinVeloAudit", "marker", 1, "DWord");
        var tweak = new TweakDefinition { Id = "audit.marker", Apply = [Set("marker", 1), Script("required-script")], Undo = [Script("undo-script")] };
        Assert.Equal(1, (await Runner(Context()).ApplyBatchAsync([tweak])).AppliedCount);
        Assert.Contains("required-script", _shell.ExecutedScripts);
        Assert.Equal(TweakState.Partial, await new StateDetector(_registry, _services, _tasks, _shell).DetectTweakStateAsync(tweak, false));
    }
    [Fact]
    public async Task ConflictPlan_IsRejectedBeforeAnyWrite()
    {
        var ctx = Context(); var first = new TweakDefinition { Id = "a", Apply = [Set("x", 1)], OppositeOf = ["b"] }; var second = new TweakDefinition { Id = "b", Apply = [Set("x", 0)] };
        var report = await Runner(ctx).ApplyBatchAsync([first, second]);
        Assert.Equal(2, report.FailedCount); Assert.Empty(ctx.Journal.GetAllSessions()); Assert.False(_registry.ValueExists("HKCU", "Software\\WinVeloAudit", "x"));
    }
    [Fact]
    public async Task ToolboxRegistry_CanBeRestoredThroughHistory()
    {
        var ctx = Context(); _registry.SetValue("HKCU", "Software\\WinVeloAudit", "x", 9, "DWord");
        ModuleJournal.Registry(_registry, ctx.Journal, "tools.audit").SetValue("HKCU", "Software\\WinVeloAudit", "x", 1, "DWord");
        Assert.True((await Runner(ctx).RollbackSessionAsync(Assert.Single(ctx.Journal.GetAllSessions()))).Success);
        Assert.Equal(9, _registry.GetValue("HKCU", "Software\\WinVeloAudit", "x"));
    }
    [Fact]
    public async Task Snapshot_RestoresDefaultAndTypedValues()
    {
        _registry.SetValue("HKCU", "Software\\WinVeloAudit", "", 9, "DWord");
        _registry.SetValue("HKCU", "Software\\WinVeloAudit", "multi", new[] { "a", "b" }, "MultiString");
        var manager = new BackupManager(_registry, _services, new FakeRestoreService(), Path.Combine(_directory, "backup"));
        var multi = Set("multi", 1); multi.ValueKind = "MultiString";
        string backup = await manager.CreateSnapshotAsync([new() { Id = "audit.snapshot", Apply = [Set("", 1), multi] }]);
        _registry.SetValue("HKCU", "Software\\WinVeloAudit", "", 7, "DWord"); _registry.SetValue("HKCU", "Software\\WinVeloAudit", "multi", new[] { "changed" }, "MultiString");
        Assert.True(await manager.RestoreBackupAsync(backup));
        Assert.Equal(9, _registry.GetValue("HKCU", "Software\\WinVeloAudit", "")); Assert.Equal(new[] { "a", "b" }, Assert.IsType<string[]>(_registry.GetValue("HKCU", "Software\\WinVeloAudit", "multi")));
    }
    [Fact]
    public async Task ScriptOnlyFeature_UnsupportedDisableIsFailure()
    {
        var report = await new FeatureService(_shell).DisableFeaturesAsync([new() { Key = "audit", Name = "Audit", Description = "", Category = "Features", InvokeScripts = ["enable"] }], new OperationRunner());
        Assert.Equal(1, report.FailedCount); Assert.Empty(_shell.ExecutedScripts);
    }
    [Fact]
    public async Task CleanerAndFileHandler_TestModeDoNotDeleteSentinel()
    {
        bool original = TestModeManager.IsTestMode; TestModeManager.EnableTestMode();
        try
        {
            Directory.CreateDirectory(_directory); string sentinel = Path.Combine(_directory, "sentinel.txt"); await File.WriteAllTextAsync(sentinel, "keep");
            await new CleanerService(_services).CleanCategoriesAsync(["UserTemp"]);
            await new FileActionHandler().ExecuteAsync(new() { Type = "File", Operation = "Delete", AllowedRoot = "%TEMP%", PathPattern = sentinel }, "audit.file", 0, Context());
            Assert.True(File.Exists(sentinel));
        }
        finally { TestModeManager.IsTestMode = original; }
    }
    [Fact]
    public void GuiConfigRoundTrip_ResolvesEverySectionAndRejectsUnknown()
    {
        var tweak = new TweakDefinition { Id = "local", Origin = "upstream", Apply = [Set("x", 1)] };
        var config = ConfigManager.ImportConfigJson(ConfigManager.ExportConfigJson(new() { SelectedTweaks = ["upstream"], SelectedApps = ["chrome"], SelectedFeatures = ["feature"], SelectedDns = "dns", DnsAdapterIndex = 3, UpdatesMode = "Security" }))!;
        var apps = new[] { new ApplicationItem { Key = "chrome", Name = "Chrome", Category = "Browser", WingetId = "Google.Chrome" } };
        var features = new[] { new FeatureDefinition { Key = "feature", Name = "Feature", Description = "", Category = "Features" } };
        var dns = new[] { new DnsProviderDefinition { Id = "dns", Name = "DNS", Description = "", PrimaryDns = "1.1.1.1" } };
        var plan = ConfigurationPlan.Resolve(config, [tweak], apps, features, dns);
        Assert.Empty(plan.Errors); Assert.Single(plan.ApplyTweaks); Assert.Single(plan.Apps); Assert.Single(plan.Features); Assert.NotNull(plan.Dns); Assert.NotNull(plan.Updates);
        config.SelectedApps.Add("unknown"); Assert.Contains(ConfigurationPlan.Resolve(config, [tweak], apps, features, dns).Errors, e => e.Contains("Unknown app"));
    }
    [Fact]
    public async Task Dns_UsesSelectedAdapterIPv6AndExplicitDoh()
    {
        var service = new DnsService(_shell); var provider = service.GetProviders().First(p => p.Id == "dns.cloudflare.standard");
        Assert.True(await service.ApplyDnsAsync(provider, 7, true));
        string script = Assert.Single(_shell.ExecutedScripts);
        Assert.Contains("$index=7", script); Assert.Contains("IPv6", script); Assert.Contains("DohServerAddress", script); Assert.DoesNotContain("Get-NetAdapter | Where-Object { $_.Status", script);
    }
    [Fact]
    public async Task Appx_RequiresConfirmationAndOnlyRemovesSelectedCurrentUserPackage()
    {
        var service = new AppxService(_shell); var item = service.Load().First();
        Assert.Equal(1, (await service.RemoveAsync([item], false, new OperationRunner())).FailedCount); Assert.Empty(_shell.ExecutedScripts);
        Assert.Equal(0, (await service.RemoveAsync([item], true, new OperationRunner())).FailedCount);
        string script = Assert.Single(_shell.ExecutedScripts); Assert.Contains(item.PackageId, script); Assert.DoesNotContain("-AllUsers", script); Assert.DoesNotContain("Remove-AppxProvisionedPackage", script);
    }
    [Fact]
    public async Task UnknownFix_HasAFailedResult()
    { Assert.Equal(1, (await new FixService(_shell).RunFixAsync("unknown", new OperationRunner())).FailedCount); Assert.Empty(_shell.ExecutedScripts); }
    [Fact]
    public void ProcessResult_RejectsCancellationAndFailedStartEvenWithZeroExit()
    {
        Assert.False(new PowerShellResult { StdOut = "", StdErr = "", TimedOut = false, ExitCode = 0, Cancelled = true, Duration = TimeSpan.Zero }.Success);
        Assert.False(new PowerShellResult { StdOut = "", StdErr = "", TimedOut = false, ExitCode = 0, FailedToStart = true, Duration = TimeSpan.Zero }.Success);
    }
    [Fact]
    public void Dependencies_AreOrderedAndCyclesRejected()
    {
        var a = new TweakDefinition { Id = "a", DependsOn = ["b"] }; var b = new TweakDefinition { Id = "b" };
        var plan = TweakPlanValidator.OrderDependencies([a, b]); Assert.Empty(plan.Errors); Assert.Equal(new[] { "b", "a" }, plan.Ordered.Select(t => t.Id));
        b.DependsOn = ["a"]; Assert.NotEmpty(TweakPlanValidator.OrderDependencies([a, b]).Errors);
    }
    [Fact]
    public async Task CancelledScript_UsesUncancelledRollbackAndKeepsHonestStatus()
    {
        var context = Context();
        _shell.ScriptResultFactory = (script, _) => new() { ExitCode = 0, Cancelled = script == "apply", StdOut = "", StdErr = "", TimedOut = false, Duration = TimeSpan.Zero };
        var report = await Runner(context).ApplyBatchAsync([new() { Id = "cancelled", Apply = [Script("apply")], Undo = [Script("undo")] }]);
        Assert.Equal(TweakExecutionStatus.FailedRolledBack, Assert.Single(report.Results).Status);
        Assert.Contains("undo", _shell.ExecutedScripts); Assert.All(context.Journal.GetAllSessions().SelectMany(s => s.Records), record => Assert.True(record.IsReverted));
    }
    [Fact]
    public async Task MissingDependency_RejectsBeforeWriting()
    {
        var context = Context(); var report = await Runner(context).ApplyBatchAsync([new() { Id = "a", DependsOn = ["missing.dependency"], Apply = [Set("x", 1)] }]);
        Assert.Equal(1, report.FailedCount); Assert.Empty(context.Journal.GetAllSessions());
    }
    [Fact]
    public void Importer_InvalidInputDoesNotCreateOutputOrOverwriteCatalog()
    {
        string source = Path.Combine(_directory, "source"); Directory.CreateDirectory(source);
        foreach (string name in new[] { "applications.json", "feature.json", "dns.json", "appx.json", "preset.json", "tweaks.json" }) File.WriteAllText(Path.Combine(source, name), "{}");
        File.WriteAllText(Path.Combine(source, "tweaks.json"), "invalid");
        string output = Path.Combine(_directory, "proposal");
        Assert.Equal(1, UpstreamImporter.Program.Main([source, output])); Assert.False(Directory.Exists(output));
    }
    [Fact]
    public void Importer_ProducesSourceProposalAndRefusesExistingOutput()
    {
        string source = Path.Combine(_directory, "source"); Directory.CreateDirectory(source);
        foreach (string name in new[] { "applications.json", "feature.json", "dns.json", "appx.json", "preset.json", "tweaks.json" }) File.WriteAllText(Path.Combine(source, name), "{}");
        string output = Path.Combine(_directory, "proposal");
        Assert.Equal(0, UpstreamImporter.Program.Main([source, output])); Assert.True(File.Exists(Path.Combine(output, "review.json")));
        Assert.True(File.Exists(Path.Combine(output, "source", "tweaks.json"))); Assert.False(Directory.Exists(Path.Combine(output, "Tweaks")));
        Assert.Equal(1, UpstreamImporter.Program.Main([source, output]));
    }
    [Fact]
    public void RegistryExport_UsesFullHivesDefaultValueAndLittleEndianQword()
    {
        var profile = new WinVeloProfile { Id = "audit", Name = "Audit", Description = "Export", TweakIds = ["audit"] };
        var value = Set("", 1); value.ValueKind = "QWord"; value.Data = JsonSerializer.SerializeToElement(0x0102030405060708L);
        string reg = ProfileManager.ExportProfileToReg(profile, [new() { Id = "audit", Apply = [value, Script("not exported")] }]);
        Assert.Contains("[HKEY_CURRENT_USER", reg); Assert.Contains("@=hex(b):08,07,06,05,04,03,02,01", reg); Assert.Contains("Omitted PowerShell action", reg);
    }
    [Fact]
    public void FeatureSelection_RaisesPropertyChanged()
    {
        var feature = new FeatureDefinition { Key = "test", Name = "Test", Description = "", Category = "Features" }; var names = new List<string?>();
        feature.PropertyChanged += (_, args) => names.Add(args.PropertyName); feature.IsSelected = true; feature.Status = "Partial";
        Assert.Contains("IsSelected", names); Assert.Contains("Status", names);
    }
    [Fact]
    public void JournalManagers_ForEquivalentPathsShareOperationGate()
    {
        Assert.Same(new RollbackJournalManager(_directory).OperationGate, new RollbackJournalManager(_directory + Path.DirectorySeparatorChar).OperationGate);
    }
    [Fact]
    public async Task SessionRollback_PreservesOtherSessionsAndOriginalMetadata()
    {
        var context = Context();
        await ModuleJournal.RunScriptAsync(_shell, context.Journal, "tools.audit", "apply-a", [Script("undo-a")]);
        var first = Assert.Single(context.Journal.GetAllSessions());
        await ModuleJournal.RunScriptAsync(_shell, context.Journal, "tools.audit", "apply-b", [Script("undo-b")]);
        var second = context.Journal.GetAllSessions().Single(s => s.SessionId != first.SessionId);
        var view = new RollbackJournalSession { SessionId = second.SessionId, Records = second.Records };
        Assert.True((await Runner(context).RollbackSessionAsync(view)).Success);
        var sessions = context.Journal.GetAllSessions();
        Assert.False(Assert.Single(sessions.Single(s => s.SessionId == first.SessionId).Records).IsReverted);
        Assert.True(Assert.Single(sessions.Single(s => s.SessionId == second.SessionId).Records).IsReverted);
        Assert.Equal(second.Mode, sessions.Single(s => s.SessionId == second.SessionId).Mode);
        Assert.Contains("undo-b", _shell.ExecutedScripts); Assert.DoesNotContain("undo-a", _shell.ExecutedScripts);
    }
    private sealed class DeniedTasks : ITaskSchedulerService
    {
        public bool TaskExists(string path, string name) => true; public bool IsTaskEnabled(string path, string name) => true;
        public void EnableTask(string path, string name) => throw new UnauthorizedAccessException();
        public void DisableTask(string path, string name) => throw new UnauthorizedAccessException();
        public void DeleteTask(string path, string name) => throw new UnauthorizedAccessException();
    }
}
