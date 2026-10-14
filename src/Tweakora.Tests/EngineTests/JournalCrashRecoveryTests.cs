using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Tweakora.App.ViewModels;
using Tweakora.Core.Engine;
using Tweakora.Core.Engine.Handlers;
using Tweakora.Core.Interfaces;
using Tweakora.Core.Models;
using Tweakora.Core.Operations;
using Tweakora.Modules.Tweaks;
using Tweakora.Simulation;
using Xunit;

namespace Tweakora.Tests.EngineTests;

public sealed class JournalCrashRecoveryTests : IDisposable
{
    private readonly string _tempJournalDir;
    private readonly FakeRegistryService _fakeRegistry;
    private readonly FakeServicesService _fakeServices;
    private readonly FakeTaskSchedulerService _fakeTasks;
    private readonly FakeRestoreService _fakeRestore;
    private readonly FakeShellService _fakeShell;
    private readonly RollbackJournalManager _journal;
    private readonly StateDetector _detector;
    private readonly List<IActionHandler> _handlers;
    private readonly Core.Engine.ExecutionContext _context;
    private readonly TweakRunner _runner;

    public JournalCrashRecoveryTests()
    {
        _tempJournalDir = Path.Combine(Path.GetTempPath(), "WinVelo_CrashRecovery_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempJournalDir);

        _fakeRegistry = new FakeRegistryService();
        _fakeServices = new FakeServicesService();
        _fakeTasks = new FakeTaskSchedulerService();
        _fakeRestore = new FakeRestoreService();
        _fakeShell = new FakeShellService();

        _journal = new RollbackJournalManager(_tempJournalDir);
        _detector = new StateDetector(_fakeRegistry, _fakeServices, _fakeTasks);

        _handlers = new List<IActionHandler>
        {
            new RegistryActionHandler(),
            new ServiceActionHandler(),
            new TaskActionHandler(),
            new FileActionHandler(),
            new PowerShellActionHandler()
        };

        _context = new Core.Engine.ExecutionContext
        {
            Registry = _fakeRegistry,
            Services = _fakeServices,
            Tasks = _fakeTasks,
            Shell = _fakeShell,
            Journal = _journal,
            IsDryRun = false,
            BackupDirectory = Path.Combine(_tempJournalDir, "Backups")
        };

        _runner = new TweakRunner(_handlers, _context, _fakeRestore, _detector);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempJournalDir))
            {
                Directory.Delete(_tempJournalDir, true);
            }
        }
        catch { }
    }

    [Fact]
    public async Task CrashMidApply_SessionFileWrittenButNotCompleted_RollbackRecoveryRestoresBaselineAndCompletesSession()
    {
        // 1. Initial baseline in system state
        string hive = "HKCU";
        string subKey = @"Software\WinVeloTest\CrashTest";
        string valueName = "Setting1";
        _fakeRegistry.SetValue(hive, subKey, valueName, 0, "DWord");

        // 2. Simulate mid-apply write-ahead logging before a crash
        var session = _journal.StartSession();
        _fakeRegistry.SetValue(hive, subKey, valueName, 1, "DWord"); // Value modified by tweak

        var record1 = new JournalActionRecord
        {
            TweakId = "pref.crashtest",
            ActionIndex = 0,
            ActionType = "Registry",
            TargetIdentifier = $"{hive}\\{subKey}\\{valueName}",
            ExistedBefore = true,
            PreviousData = "0",
            PreviousValueKind = "DWord",
            IsBaseline = true,
            AppliedAtUtc = DateTime.UtcNow
        };
        _journal.RecordActionWriteAhead(record1);

        // Add a second action (Service start mode)
        string serviceName = "WinVeloService";
        _fakeServices.SetStartupType(serviceName, "Automatic");
        _fakeServices.SetStartupType(serviceName, "Disabled"); // modified by tweak

        var record2 = new JournalActionRecord
        {
            TweakId = "pref.crashtest",
            ActionIndex = 1,
            ActionType = "Service",
            TargetIdentifier = serviceName,
            ExistedBefore = true,
            PreviousData = "Automatic",
            IsBaseline = true,
            AppliedAtUtc = DateTime.UtcNow.AddSeconds(1)
        };
        _journal.RecordActionWriteAhead(record2);

        // Simulate crash: process terminates without calling CompleteSession()!
        // At this point, the session file on disk has isCompleted = false.
        var initialUnfinished = _journal.GetUnfinishedSessions();
        Assert.Single(initialUnfinished);
        Assert.False(initialUnfinished[0].IsCompleted);
        Assert.Equal(2, initialUnfinished[0].Records.Count);

        // 3. New startup in a new runner instance (simulating next app launch)
        var nextJournal = new RollbackJournalManager(_tempJournalDir);
        var nextContext = new Core.Engine.ExecutionContext
        {
            Registry = _fakeRegistry,
            Services = _fakeServices,
            Tasks = _fakeTasks,
            Shell = _fakeShell,
            Journal = nextJournal,
            IsDryRun = false,
            BackupDirectory = Path.Combine(_tempJournalDir, "Backups")
        };
        var nextRunner = new TweakRunner(_handlers, nextContext, _fakeRestore, _detector);

        var unfinishedOnStartup = nextJournal.GetUnfinishedSessions();
        Assert.Single(unfinishedOnStartup);

        // Verify values are currently in the crashed/modified state before recovery
        Assert.Equal(1, Convert.ToInt32(_fakeRegistry.GetValue(hive, subKey, valueName)));
        Assert.Equal("Disabled", _fakeServices.GetStartupType(serviceName));

        // 4. Execute rollback recovery
        var rollbackResult = await nextRunner.RollbackSessionAsync(unfinishedOnStartup[0]);
        Assert.True(rollbackResult.Success, rollbackResult.Message);

        // 5. Verify all touched values are restored to baseline
        Assert.Equal(0, Convert.ToInt32(_fakeRegistry.GetValue(hive, subKey, valueName)));
        Assert.Equal("Automatic", _fakeServices.GetStartupType(serviceName));

        // 6. Verify session is marked completed on disk and no unfinished sessions remain
        var remainingUnfinished = nextJournal.GetUnfinishedSessions();
        Assert.Empty(remainingUnfinished);

        var allSessions = nextJournal.GetAllSessions();
        Assert.Single(allSessions);
        Assert.True(allSessions[0].IsCompleted);
        Assert.All(allSessions[0].Records, r => Assert.True(r.IsReverted));
    }

    [Fact]
    public void CrashMidApply_KeepRecovery_MarksSessionCompleteWithoutChangingValues()
    {
        // 1. Initial state and mid-apply
        string hive = "HKCU";
        string subKey = @"Software\WinVeloTest\KeepTest";
        string valueName = "KeepVal";
        _fakeRegistry.SetValue(hive, subKey, valueName, 0, "DWord");

        var session = _journal.StartSession();
        _fakeRegistry.SetValue(hive, subKey, valueName, 42, "DWord"); // Modified

        var record = new JournalActionRecord
        {
            TweakId = "pref.keeptest",
            ActionIndex = 0,
            ActionType = "Registry",
            TargetIdentifier = $"{hive}\\{subKey}\\{valueName}",
            ExistedBefore = true,
            PreviousData = "0",
            PreviousValueKind = "DWord",
            IsBaseline = true,
            AppliedAtUtc = DateTime.UtcNow
        };
        _journal.RecordActionWriteAhead(record);

        // Crash before completion
        var unfinished = _journal.GetUnfinishedSessions();
        Assert.Single(unfinished);

        // Keep recovery
        _journal.CompleteSession(unfinished[0]);

        // Value must NOT be rolled back
        Assert.Equal(42, Convert.ToInt32(_fakeRegistry.GetValue(hive, subKey, valueName)));

        // Session must be marked completed
        Assert.Empty(_journal.GetUnfinishedSessions());
        var all = _journal.GetAllSessions();
        Assert.True(all[0].IsCompleted);
    }

    [Fact]
    public async Task RollbackAllUnfinishedSessionsAsync_MultipleInterruptedSessions_RollsBackAllInReverseOrder()
    {
        // Session 1: modified ValA to 10 (baseline 0)
        string hive = "HKCU";
        string subKey = @"Software\WinVeloTest\MultiTest";
        _fakeRegistry.SetValue(hive, subKey, "ValA", 0, "DWord");
        var s1 = _journal.StartSession();
        _fakeRegistry.SetValue(hive, subKey, "ValA", 10, "DWord");
        _journal.RecordActionWriteAhead(new JournalActionRecord
        {
            TweakId = "tweak.a",
            ActionIndex = 0,
            ActionType = "Registry",
            TargetIdentifier = $"{hive}\\{subKey}\\ValA",
            ExistedBefore = true,
            PreviousData = "0",
            PreviousValueKind = "DWord",
            AppliedAtUtc = DateTime.UtcNow.AddMinutes(-5)
        });

        // Session 2: modified ValB to 20 (baseline 5)
        _fakeRegistry.SetValue(hive, subKey, "ValB", 5, "DWord");
        var s2 = new RollbackJournalSession
        {
            SessionId = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_") + "sess2",
            StartedAtUtc = DateTime.UtcNow,
            IsCompleted = false
        };
        s2.Records.Add(new JournalActionRecord
        {
            TweakId = "tweak.b",
            ActionIndex = 0,
            ActionType = "Registry",
            TargetIdentifier = $"{hive}\\{subKey}\\ValB",
            ExistedBefore = true,
            PreviousData = "5",
            PreviousValueKind = "DWord",
            AppliedAtUtc = DateTime.UtcNow
        });
        string s2File = Path.Combine(_tempJournalDir, $"session-{s2.SessionId}.json");
        File.WriteAllText(s2File, JsonSerializer.Serialize(s2));

        Assert.Equal(2, _journal.GetUnfinishedSessions().Count);

        // Execute RollbackAllUnfinishedSessionsAsync
        var result = await _runner.RollbackAllUnfinishedSessionsAsync();
        Assert.True(result.Success);

        // Baselines restored
        Assert.Equal(0, Convert.ToInt32(_fakeRegistry.GetValue(hive, subKey, "ValA")));
        Assert.Equal(5, Convert.ToInt32(_fakeRegistry.GetValue(hive, subKey, "ValB")));

        // No unfinished sessions remaining
        Assert.Empty(_journal.GetUnfinishedSessions());
    }

    [Fact]
    public async Task MainViewModel_CheckUnfinishedSessionsOnStartup_OpensDialogAndRollsBack()
    {
        // Setup mid-apply unfinished session
        string hive = "HKCU";
        string subKey = @"Software\WinVeloTest\GuiRecovery";
        _fakeRegistry.SetValue(hive, subKey, "SettingX", 100, "DWord");

        var session = _journal.StartSession();
        _fakeRegistry.SetValue(hive, subKey, "SettingX", 999, "DWord");
        _journal.RecordActionWriteAhead(new JournalActionRecord
        {
            TweakId = "pref.guirecovery",
            ActionIndex = 0,
            ActionType = "Registry",
            TargetIdentifier = $"{hive}\\{subKey}\\SettingX",
            ExistedBefore = true,
            PreviousData = "100",
            PreviousValueKind = "DWord",
            AppliedAtUtc = DateTime.UtcNow
        });

        var tweakService = new TweakService(_fakeRegistry, _fakeServices, _fakeTasks, _fakeRestore, _fakeShell, _journal);
        var opRunner = new OperationRunner();
        var startup = new Modules.Startup.StartupService(_fakeRegistry);
        var servicesMgr = new Modules.Services.ServicesManagerService(_fakeServices);
        var fixService = new Modules.Config.FixService(_fakeShell);
        var dnsService = new Modules.Network.DnsService(_fakeShell);
        var memOpt = new Modules.Memory.MemoryOptimizerService();
        var netOpt = new Modules.Network.NetworkOptimizerService(_fakeRegistry, _fakeShell);
        var advisor = new Modules.Advisor.SmartAdvisorService(_fakeRegistry, _fakeShell);
        var appInstall = new Modules.Apps.AppInstallService(_fakeShell);
        var featureService = new Modules.Config.FeatureService(_fakeShell);
        var updateMode = new Modules.Updates.UpdateModeService(_fakeRegistry, _fakeServices, _fakeTasks);
        var cleaner = new Modules.Cleaner.CleanerService(_fakeServices);

        var dashboardVm = new DashboardViewModel(tweakService, opRunner, _fakeRestore, memOpt, advisor);
        var tweaksVm = new TweaksViewModel(tweakService, opRunner);
        var startupVm = new StartupViewModel(startup);
        var toolboxVm = new ToolboxViewModel(cleaner, startup, servicesMgr, fixService, dnsService, opRunner, memOpt, netOpt);
        var installVm = new InstallViewModel(appInstall, opRunner);
        var configVm = new ConfigViewModel(featureService, fixService, opRunner);
        var updatesVm = new UpdatesViewModel(updateMode, opRunner);
        var backupsVm = new BackupsViewModel(tweakService);

        var mainVm = new MainViewModel(
            dashboardVm,
            installVm,
            tweaksVm,
            configVm,
            updatesVm,
            toolboxVm,
            startupVm,
            backupsVm,
            activityTracker: null,
            tweakService: tweakService);

        Assert.False(mainVm.IsRecoveryDialogOpen);

        // 1. Startup check triggers dialog
        await mainVm.CheckUnfinishedSessionsOnStartupAsync();
        Assert.True(mainVm.IsRecoveryDialogOpen);
        Assert.Contains("1 actions were applied to [pref.guirecovery]", mainVm.RecoveryDialogMessage);

        // 2. Click rollback recovery
        await mainVm.RollbackRecoveryCommand.ExecuteAsync(null);

        Assert.False(mainVm.IsRecoveryDialogOpen);
        Assert.Equal(100, Convert.ToInt32(_fakeRegistry.GetValue(hive, subKey, "SettingX")));
        Assert.Empty(_journal.GetUnfinishedSessions());
    }

    [Fact]
    public async Task MainViewModel_KeepRecovery_MarksCompleteWithoutReverting()
    {
        string hive = "HKCU";
        string subKey = @"Software\WinVeloTest\GuiKeep";
        _fakeRegistry.SetValue(hive, subKey, "SettingY", 555, "DWord");

        var session = _journal.StartSession();
        _fakeRegistry.SetValue(hive, subKey, "SettingY", 777, "DWord");
        _journal.RecordActionWriteAhead(new JournalActionRecord
        {
            TweakId = "pref.guikeep",
            ActionIndex = 0,
            ActionType = "Registry",
            TargetIdentifier = $"{hive}\\{subKey}\\SettingY",
            ExistedBefore = true,
            PreviousData = "555",
            PreviousValueKind = "DWord",
            AppliedAtUtc = DateTime.UtcNow
        });

        var tweakService = new TweakService(_fakeRegistry, _fakeServices, _fakeTasks, _fakeRestore, _fakeShell, _journal);
        var opRunner = new OperationRunner();
        var startup = new Modules.Startup.StartupService(_fakeRegistry);
        var servicesMgr = new Modules.Services.ServicesManagerService(_fakeServices);
        var fixService = new Modules.Config.FixService(_fakeShell);
        var dnsService = new Modules.Network.DnsService(_fakeShell);
        var memOpt = new Modules.Memory.MemoryOptimizerService();
        var netOpt = new Modules.Network.NetworkOptimizerService(_fakeRegistry, _fakeShell);
        var advisor = new Modules.Advisor.SmartAdvisorService(_fakeRegistry, _fakeShell);
        var appInstall = new Modules.Apps.AppInstallService(_fakeShell);
        var featureService = new Modules.Config.FeatureService(_fakeShell);
        var updateMode = new Modules.Updates.UpdateModeService(_fakeRegistry, _fakeServices, _fakeTasks);
        var cleaner = new Modules.Cleaner.CleanerService(_fakeServices);

        var dashboardVm = new DashboardViewModel(tweakService, opRunner, _fakeRestore, memOpt, advisor);
        var tweaksVm = new TweaksViewModel(tweakService, opRunner);
        var startupVm = new StartupViewModel(startup);
        var toolboxVm = new ToolboxViewModel(cleaner, startup, servicesMgr, fixService, dnsService, opRunner, memOpt, netOpt);
        var installVm = new InstallViewModel(appInstall, opRunner);
        var configVm = new ConfigViewModel(featureService, fixService, opRunner);
        var updatesVm = new UpdatesViewModel(updateMode, opRunner);
        var backupsVm = new BackupsViewModel(tweakService);

        var mainVm = new MainViewModel(
            dashboardVm,
            installVm,
            tweaksVm,
            configVm,
            updatesVm,
            toolboxVm,
            startupVm,
            backupsVm,
            activityTracker: null,
            tweakService: tweakService);

        await mainVm.CheckUnfinishedSessionsOnStartupAsync();
        Assert.True(mainVm.IsRecoveryDialogOpen);

        // Click keep recovery
        mainVm.KeepRecoveryCommand.Execute(null);

        Assert.False(mainVm.IsRecoveryDialogOpen);
        Assert.Equal(777, Convert.ToInt32(_fakeRegistry.GetValue(hive, subKey, "SettingY")));
        Assert.Empty(_journal.GetUnfinishedSessions());
    }
}
