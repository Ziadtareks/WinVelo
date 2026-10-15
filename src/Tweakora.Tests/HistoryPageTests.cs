using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Tweakora.App.ViewModels;
using Tweakora.Core.Engine;
using Tweakora.Core.Engine.Runners;
using Tweakora.Core.Models;
using Tweakora.Core.Operations;
using Tweakora.Modules.Tweaks;
using Tweakora.Simulation;
using Xunit;

namespace Tweakora.Tests;

public sealed class HistoryPageTests : IDisposable
{
    private readonly string _testJournalDir;

    public HistoryPageTests()
    {
        _testJournalDir = Path.Combine(Path.GetTempPath(), "WinVelo_HistoryTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testJournalDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testJournalDir))
            {
                Directory.Delete(_testJournalDir, true);
            }
        }
        catch { }
    }

    private (HistoryViewModel VM, TweakService TweakSvc, FakeRestoreService RestoreSvc, UnifiedExecutionDialogViewModel Dialog) CreateFixture()
    {
        var fakeReg = new FakeRegistryService();
        var fakeServices = new FakeServicesService();
        var fakeTasks = new FakeTaskSchedulerService();
        var fakeRestore = new FakeRestoreService();
        var fakeShell = new FakeShellService();

        var journal = new RollbackJournalManager(_testJournalDir);
        var tweakService = new TweakService(fakeReg, fakeServices, fakeTasks, fakeRestore, fakeShell, journal);
        var dialog = new UnifiedExecutionDialogViewModel(tweakService, new OperationRunner());

        var vm = new HistoryViewModel(tweakService, fakeRestore, new OperationRunner(), dialog);
        return (vm, tweakService, fakeRestore, dialog);
    }

    [Fact]
    public void HistoryPage_LoadsSessions_NewestFirst()
    {
        var (vm, tweakService, _, _) = CreateFixture();

        // Write two test sessions to the journal
        var sOld = new RollbackJournalSession
        {
            SessionId = "session_old_20261010",
            StartedAtUtc = DateTime.UtcNow.AddDays(-2),
            IsCompleted = true,
            Records = new()
            {
                new JournalActionRecord { TweakId = "pref.darkmode", ActionType = "Registry", TargetIdentifier = "HKCU\\Test", IsBaseline = true }
            }
        };

        var sNew = new RollbackJournalSession
        {
            SessionId = "session_new_20261012",
            StartedAtUtc = DateTime.UtcNow.AddMinutes(-10),
            IsCompleted = true,
            Records = new()
            {
                new JournalActionRecord { TweakId = "gaming.mode.enable", ActionType = "Registry", TargetIdentifier = "HKCU\\Gaming", IsBaseline = true }
            }
        };

        File.WriteAllText(Path.Combine(_testJournalDir, $"{sOld.SessionId}.json"), System.Text.Json.JsonSerializer.Serialize(sOld));
        File.WriteAllText(Path.Combine(_testJournalDir, $"{sNew.SessionId}.json"), System.Text.Json.JsonSerializer.Serialize(sNew));

        vm.RefreshHistory();

        Assert.Equal(2, vm.Sessions.Count);
        Assert.Equal("session_new_20261012", vm.Sessions[0].SessionId);
        Assert.Equal("session_old_20261010", vm.Sessions[1].SessionId);
    }

    [Fact]
    public void HistoryPage_DetectsTriggerSources_AndGroupsTweaks()
    {
        var (vm, _, _, _) = CreateFixture();

        var sCli = new RollbackJournalSession
        {
            SessionId = "cli_session_12345",
            StartedAtUtc = DateTime.UtcNow,
            IsCompleted = true,
            Records = new()
            {
                new JournalActionRecord { TweakId = "pref.darkmode", ActionType = "Registry", TargetIdentifier = "HKCU\\Test1", IsBaseline = true },
                new JournalActionRecord { TweakId = "pref.darkmode", ActionType = "Registry", TargetIdentifier = "HKCU\\Test2", IsBaseline = false }
            }
        };

        File.WriteAllText(Path.Combine(_testJournalDir, $"{sCli.SessionId}.json"), System.Text.Json.JsonSerializer.Serialize(sCli));

        vm.RefreshHistory();

        Assert.Single(vm.Sessions);
        var session = vm.Sessions[0];
        Assert.Equal("CLI", session.TriggerSource);
        Assert.Equal(2, session.TotalActions);
        Assert.Single(session.Tweaks);

        var tweakGroup = session.Tweaks[0];
        Assert.Equal("pref.darkmode", tweakGroup.TweakId);
        Assert.Equal(2, tweakGroup.ActionCount);
        Assert.False(tweakGroup.IsReverted);
        Assert.Equal("Active", tweakGroup.StatusText);
    }

    [Fact]
    public void HistoryPage_PopulatesRestorePoints_ReadOnlyList()
    {
        var (vm, _, restore, _) = CreateFixture();

        Assert.True(vm.RestorePoints.Count >= 3);
        Assert.Equal(vm.RestorePoints.Count, vm.RestorePointCount);
        Assert.True(vm.IsRestoreEnabled);
        Assert.Contains(vm.RestorePoints, r => r.Description.Contains("Checkpoint"));
    }

    [Fact]
    public async Task HistoryPage_UndoTweak_OpensUnifiedPreviewDialog()
    {
        var (vm, _, _, dialog) = CreateFixture();

        await vm.UndoTweakAsync("pref.darkmode");

        Assert.True(dialog.IsPreviewOpen);
        Assert.Contains("Undo Tweak", dialog.PreviewTitle);
        Assert.True(dialog.IsUndo);
        Assert.Equal("Confirm & Revert", dialog.PreviewConfirmText);
    }

    [Fact]
    public async Task HistoryPage_UndoAllChanges_IncludesExplicitToolCoverageNotice()
    {
        var (vm, _, _, dialog) = CreateFixture();

        // Write an active journal session so GetActiveJournalTweaks returns tweaks
        var s = new RollbackJournalSession
        {
            SessionId = "session_active",
            StartedAtUtc = DateTime.UtcNow,
            IsCompleted = true,
            Records = new()
            {
                new JournalActionRecord { TweakId = "pref.darkmode", ActionType = "Registry", TargetIdentifier = "HKCU\\Test", IsBaseline = true, IsReverted = false }
            }
        };
        File.WriteAllText(Path.Combine(_testJournalDir, $"{s.SessionId}.json"), System.Text.Json.JsonSerializer.Serialize(s));

        await vm.UndoAllChangesAsync();

        Assert.True(dialog.IsPreviewOpen);
        Assert.Equal("Undo All WinVelo Changes", dialog.PreviewTitle);
        Assert.Contains("Changes made by other tools or Windows Settings are not covered", dialog.PreviewNotice);
    }

    [Fact]
    public void HistoryPage_ExportSessionLog_GeneratesFormattedTextFile()
    {
        var (vm, _, _, _) = CreateFixture();

        var s = new HistorySessionCardModel
        {
            SessionId = "test_export_session",
            StartedAtUtc = DateTime.UtcNow,
            IsCompleted = true,
            TriggerSource = "Manual",
            Records = new List<JournalActionRecord>
            {
                new JournalActionRecord { TweakId = "pref.darkmode", ActionType = "Registry", TargetIdentifier = "HKCU\\Export", IsBaseline = true }
            },
            Tweaks = new List<HistoryTweakItemModel>
            {
                new HistoryTweakItemModel
                {
                    TweakId = "pref.darkmode",
                    TweakName = "Dark Mode",
                    ActionCount = 1,
                    IsReverted = false,
                    Records = new List<JournalActionRecord>()
                }
            }
        };

        vm.ExportSessionLog(s);

        Assert.True(vm.IsExportDialogOpen);
        Assert.Contains("WINVELO JOURNAL SESSION EXPORT", vm.ExportedLogContent);
        Assert.Contains("test_export_session", vm.ExportedLogContent);
        Assert.Contains("Dark Mode", vm.ExportedLogContent);
    }
}
