using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Threading;
using Tweakora.App.ViewModels;
using Tweakora.Core.Engine;
using Tweakora.Core.Engine.Runners;
using Tweakora.Core.Models;
using Tweakora.Core.Operations;
using Tweakora.Modules.Advisor;
using Tweakora.Modules.Memory;
using Tweakora.Modules.Tweaks;
using Tweakora.Simulation;
using Xunit;

namespace Tweakora.Tests;

public sealed class HomeCalmStatusTests
{
    private (DashboardViewModel Vm, FakeRestoreService Restore, FakeShellService Shell, FakeRegistryService Reg, TweakService TweakSvc) CreateFixture()
    {
        var fakeReg = new FakeRegistryService();
        var fakeSvc = new FakeServicesService();
        var fakeSched = new FakeTaskSchedulerService();
        var fakeRestore = new FakeRestoreService();
        var fakeShell = new FakeShellService();

        string journalDir = Path.Combine(AppContext.BaseDirectory, $"test-journal-home-{Guid.NewGuid():N}");
        Directory.CreateDirectory(journalDir);
        var journal = new RollbackJournalManager(journalDir);
        var runner = new OperationRunner();

        var tweakService = new TweakService(fakeReg, fakeSvc, fakeSched, fakeRestore, fakeShell, journal);
        var memOpt = new MemoryOptimizerService();
        var advisor = new SmartAdvisorService(fakeReg, fakeShell);

        var vm = new DashboardViewModel(tweakService, runner, fakeRestore, memOpt, advisor, null, fakeShell);
        return (vm, fakeRestore, fakeShell, fakeReg, tweakService);
    }

    [Fact]
    public async Task DashboardViewModel_CalmStatus_ReportsAdminAndSystemRestoreWithDate()
    {
        var (vm, restore, _, _, _) = CreateFixture();
        using (vm)
        {
            await vm.RefreshCalmStatusAsync();

            Assert.False(string.IsNullOrWhiteSpace(vm.AdminStatusText));
            Assert.True(vm.IsSystemRestoreEnabled);
            Assert.NotNull(vm.LatestRestorePointDate);
            Assert.Contains("Active", vm.SystemRestoreStatusText);
            Assert.Contains(vm.LatestRestorePointDate.Value.ToString("MMM d"), vm.SystemRestoreStatusText);
        }
    }

    [Fact]
    public async Task DashboardViewModel_CalmStatus_SystemRestoreDisabled()
    {
        var (vm, restore, _, _, _) = CreateFixture();
        using (vm)
        {
            restore.IsEnabled = false;
            await vm.RefreshCalmStatusAsync();

            Assert.False(vm.IsSystemRestoreEnabled);
            Assert.Equal("System Restore: Disabled", vm.SystemRestoreStatusText);
            Assert.Equal("#7F1D1D", vm.SystemRestoreBadgeBg);
            Assert.Equal("#F87171", vm.SystemRestoreBadgeFg);
        }
    }

    [Fact]
    public async Task DashboardViewModel_CalmStatus_AppliedTweaksCountReflectsJournal()
    {
        var (vm, _, _, _, tweakSvc) = CreateFixture();
        using (vm)
        {
            await vm.RefreshCalmStatusAsync();

            int activeCount = tweakSvc.GetActiveJournalTweaks().Count;
            Assert.Equal(activeCount, vm.AppliedTweaksCount);
            Assert.Contains("applied by WinVelo", vm.AppliedTweaksSummaryText);
        }
    }

    [Fact]
    public async Task DashboardViewModel_CalmStatus_ServicingHealthStatus()
    {
        var (vm, _, _, _, _) = CreateFixture();
        using (vm)
        {
            await vm.RefreshCalmStatusAsync();

            Assert.NotNull(vm.LastServicingHealthReport);
            Assert.True(vm.IsServicingHealthy);
            Assert.Contains("Normal", vm.ServicingHealthSummary);
            Assert.Equal("#064E3B", vm.ServicingHealthBadgeBg);
            Assert.Equal("#10B981", vm.ServicingHealthBadgeFg);
        }
    }

    [Fact]
    public async Task DashboardViewModel_PendingRebootAndExplorerRestart_DetectionAndCommand()
    {
        var (vm, _, shell, reg, tweakSvc) = CreateFixture();
        using (vm)
        {
            // When 0 tweaks applied, even if Windows has generic pending renames, WinVelo banner is NOT shown
            reg.SetValue("HKLM", @"SYSTEM\CurrentControlSet\Control\Session Manager", "PendingFileRenameOperations", new[] { "temp.dll" }, "MultiString");
            await vm.RefreshCalmStatusAsync();
            Assert.False(vm.IsRebootPending);

            // When a WinVelo tweak requiring reboot is applied, banner is shown
            var catalog = tweakSvc.LoadCatalog();
            var rebootTweak = catalog.FirstOrDefault(t => t.RequiresReboot && !t.RequiresExplorerRestart && t.Reversible);
            Assert.NotNull(rebootTweak);
            await tweakSvc.ApplyTweaksAsync(new[] { rebootTweak }, new OperationRunner());
            await vm.RefreshCalmStatusAsync();
            Assert.True(vm.IsRebootPending);

            // Simulate explorer restart pending
            vm.IsExplorerRestartPending = true;
            Assert.False(shell.ExplorerRestarted);

            vm.RestartExplorerCommand.Execute(null);

            Assert.True(shell.ExplorerRestarted);
            Assert.False(vm.IsExplorerRestartPending);
            Assert.Contains("Explorer restarted", vm.GlobalStatusMessage);
        }
    }

    [Fact]
    public void DashboardViewModel_NoPollingTimers_DispatcherTimerFieldsOmitted()
    {
        var fields = typeof(DashboardViewModel).GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        foreach (var f in fields)
        {
            Assert.False(typeof(DispatcherTimer).IsAssignableFrom(f.FieldType),
                $"DashboardViewModel should not declare DispatcherTimer fields (polling for decoration removed). Found: {f.Name}");
        }
    }

    [Fact]
    public void QuickToggleCardModel_StatusBadgeText_ReflectsRealStateNotDefault()
    {
        var model = new QuickToggleCardModel
        {
            Id = "test.toggle",
            Title = "Test Toggle",
            Description = "Test toggle description",
            IconSymbol = "Apps24"
        };

        // Initial / NotApplied state
        Assert.Equal(TweakState.NotApplied, model.State);
        Assert.False(model.IsActive);
        Assert.Equal("Off", model.StatusBadgeText);

        // Applied state
        model.State = TweakState.Applied;
        Assert.True(model.IsActive);
        Assert.Equal("On", model.StatusBadgeText);

        // Partial state
        model.State = TweakState.Partial;
        Assert.False(model.IsActive);
        Assert.Equal("Partial", model.StatusBadgeText);
    }
}
