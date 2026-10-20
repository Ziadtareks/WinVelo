using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using WinVelo.Core.Models;
using WinVelo.Modules.Power;
using WinVelo.Simulation;
using Xunit;

namespace WinVelo.Tests;

/// <summary>Ultimate Performance power plan, run against the fake shell (no real powercfg).</summary>
public sealed class PowerPlanServiceTests : IDisposable
{
    private readonly string _state = Path.Combine(Path.GetTempPath(), "WinVeloPower_" + Guid.NewGuid().ToString("N") + ".json");
    private const string Previous = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
    private const string Created = "11111111-2222-3333-4444-555555555555";

    public void Dispose()
    {
        try { File.Delete(_state); } catch (IOException) { }
    }

    private static PowerShellResult Ok(string output) => new() { ExitCode = 0, StdOut = output, StdErr = "", TimedOut = false, Duration = TimeSpan.Zero };

    private (FakeShellService Shell, Func<string> Active) NewPc()
    {
        string active = Previous;
        bool created = false;
        var shell = new FakeShellService();
        shell.ProcessResultFactory = (file, args) =>
        {
            Assert.Equal("powercfg.exe", file);
            switch (args[0])
            {
                case "/getactivescheme": return Ok($"Power Scheme GUID: {active}  (Name)");
                case "-duplicatescheme": created = true; return Ok($"Power Scheme GUID: {Created}  (Ultimate Performance)");
                case "/setactive": active = args[1]; return Ok("");
                case "/delete": created = false; return Ok("");
                case "/list": return Ok($"Power Scheme GUID: {Previous}  (High performance)\n" + (created ? $"Power Scheme GUID: {Created}  (Ultimate Performance)" : ""));
                default: return Ok("");
            }
        };
        return (shell, () => active);
    }

    [Fact]
    public async Task Enable_DuplicatesTheTemplate_AndActivatesTheNewGuid_NotTheTemplate()
    {
        var (shell, active) = NewPc();
        var result = await new PowerPlanService(shell, _state).EnableUltimatePerformanceAsync();

        Assert.True(result.Success, result.Message);
        Assert.Equal(Created, active());
        Assert.DoesNotContain(shell.ExecutedProcesses, p => p.SequenceEqual(new[] { "powercfg.exe", "/setactive", PowerPlanService.UltimateTemplateGuid }));
    }

    [Fact]
    public async Task Disable_GoesBackToThePreviousPlan_AndDeletesOnlyTheAddedPlan()
    {
        var (shell, active) = NewPc();
        var service = new PowerPlanService(shell, _state);
        await service.EnableUltimatePerformanceAsync();

        var result = await service.DisableUltimatePerformanceAsync();

        Assert.True(result.Success, result.Message);
        Assert.Equal(Previous, active());
        Assert.Contains(shell.ExecutedProcesses, p => p.SequenceEqual(new[] { "powercfg.exe", "/delete", Created }));
        Assert.DoesNotContain(shell.ExecutedProcesses, p => p.Contains("-restoredefaultschemes"));
    }

    [Theory]
    [InlineData("Power Scheme GUID: 381B4222-F694-41F0-9685-FF5BB260DF2E  (Balanced)", "381b4222-f694-41f0-9685-ff5bb260df2e", "Balanced")]
    [InlineData("GUID de modo de energía: e9a42b02-d5df-448d-aa00-03f14749eb61  (Máximo rendimiento)", "e9a42b02-d5df-448d-aa00-03f14749eb61", "Máximo rendimiento")]
    public void ParseScheme_ReadsGuidAndName_InAnyLanguage(string output, string guid, string name)
    {
        var parsed = PowerPlanService.ParseScheme(output);
        Assert.Equal((guid, name), parsed);
    }
}
