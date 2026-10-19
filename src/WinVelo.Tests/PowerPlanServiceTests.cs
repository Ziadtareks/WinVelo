using System;
using System.IO;
using System.Threading.Tasks;
using WinVelo.Core.Engine;
using WinVelo.Core.Models;
using WinVelo.Modules.Tweaks;
using WinVelo.Simulation;
using Xunit;

namespace WinVelo.Tests;

public sealed class PowerPlanServiceTests
{
    [Fact]
    public async Task UltimatePerformance_DuplicatesAndActivatesPlan()
    {
        var (service, shell) = CreateService();

        bool success = await service.SetPerformancePlanAsync("Ultimate Performance");

        Assert.True(success);
        string script = Assert.Single(shell.ExecutedScripts);
        Assert.Contains("e9a42b02-d5df-448d-aa00-03f14749eb61", script);
        Assert.Contains("if (-not ($list -match [regex]::Escape($guid)))", script);
        Assert.Contains("-duplicatescheme $guid", script);
        Assert.Contains("$guid = $match.Value", script);
        Assert.Contains("/setactive $guid", script);
        Assert.Contains("$LASTEXITCODE", script);
    }

    [Fact]
    public async Task Balanced_ActivatesDefaultPlan()
    {
        var (service, shell) = CreateService();

        bool success = await service.SetPerformancePlanAsync("Balanced");

        Assert.True(success);
        string script = Assert.Single(shell.ExecutedScripts);
        Assert.Contains("/setactive 381b4222-f694-41f0-9685-ff5bb260df2e", script);
        Assert.Contains("exit $LASTEXITCODE", script);
    }

    [Fact]
    public async Task UltimatePerformance_ReportsNativeFailure()
    {
        var (service, shell) = CreateService();
        shell.ScriptResultFactory = (_, _) => Result(exitCode: 1);

        bool success = await service.SetPerformancePlanAsync("Ultimate Performance");

        Assert.False(success);
    }

    private static (TweakService Service, FakeShellService Shell) CreateService()
    {
        var registry = new FakeRegistryService();
        var services = new FakeServicesService();
        var tasks = new FakeTaskSchedulerService();
        var restore = new FakeRestoreService();
        var shell = new FakeShellService();

        string journalDir = Path.Combine(AppContext.BaseDirectory, $"test-journal-power-plan-{Guid.NewGuid():N}");
        Directory.CreateDirectory(journalDir);
        var journal = new RollbackJournalManager(journalDir);

        return (new TweakService(registry, services, tasks, restore, shell, journal), shell);
    }

    private static PowerShellResult Result(int exitCode) => new()
    {
        ExitCode = exitCode,
        StdOut = string.Empty,
        StdErr = "Access is denied",
        TimedOut = false,
        Duration = TimeSpan.FromMilliseconds(10)
    };
}
