using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using WinVelo.Core.Models;
using WinVelo.Core.Operations;
using WinVelo.Modules.Apps;
using WinVelo.Simulation;
using Xunit;

namespace WinVelo.Tests;

/// <summary>Built-in Windows apps (WinUtil appx.json), run against the fake shell only.</summary>
public sealed class BuiltInAppsTests : IDisposable
{
    private readonly string _log = Path.Combine(Path.GetTempPath(), "WinVeloAppx_" + Guid.NewGuid().ToString("N") + ".jsonl");

    public void Dispose()
    {
        try { File.Delete(_log); } catch (IOException) { }
    }

    [Fact]
    public void AllWinUtilApps_AreListed_WithARiskRating()
    {
        var apps = new BuiltInAppsService(new FakeShellService(), _log).LoadApps();

        Assert.Equal(34, apps.Count);
        Assert.All(apps, a => Assert.Contains(a.Risk, new[] { "Safe", "Low", "Medium", "High" }));
        Assert.Equal("High", apps.Single(a => a.PackageId == "Microsoft.XboxIdentityProvider").Risk);
        Assert.Contains(apps, a => a.PackageId == "MSTeams" && a.StoreId == "XP8BT8DW290MPQ"); // 14-character Store id
    }

    [Fact]
    public void Recommended_NeverIncludesAppsPeopleExpect()
    {
        var recommended = new BuiltInAppsService(new FakeShellService(), _log).LoadApps().Where(a => a.IsRecommended).Select(a => a.PackageId).ToList();

        foreach (string kept in new[] { "Microsoft.WindowsCalculator", "Microsoft.WindowsAlarms", "Microsoft.MicrosoftStickyNotes", "Microsoft.Windows.Photos",
                                        "Microsoft.WindowsNotepad", "Microsoft.ScreenSketch", "Microsoft.Paint", "Microsoft.XboxIdentityProvider" })
        {
            Assert.DoesNotContain(kept, recommended);
        }
        Assert.Contains("Microsoft.BingNews", recommended);
    }

    [Fact]
    public void RemovalScript_UsesTheExactName_NotAWildcard()
    {
        string script = BuiltInAppsService.RemovalScript("Microsoft.Paint");

        Assert.Contains("-Name 'Microsoft.Paint'", script);
        Assert.Contains("DisplayName -eq 'Microsoft.Paint'", script);
        Assert.DoesNotContain("*", script);
        Assert.DoesNotContain("dllhost", script);
    }

    [Theory]
    [InlineData("Microsoft.Paint'; Remove-Item C:\\")]
    [InlineData("*Paint*")]
    public void BadPackageNames_AreRefused(string bad) =>
        Assert.Throws<ArgumentException>(() => BuiltInAppsService.RemovalScript(bad));

    [Fact]
    public async Task Remove_RunsOneScriptPerApp_AndLogsItForReinstall()
    {
        var shell = new FakeShellService();
        var service = new BuiltInAppsService(shell, _log);
        var apps = service.LoadApps().Where(a => a.PackageId is "Microsoft.BingNews" or "Microsoft.BingWeather").ToList();

        var report = await service.RemoveAsync(apps, new OperationRunner());

        Assert.Equal(2, report.SucceededCount);
        Assert.Equal(2, shell.ExecutedScripts.Count(s => s.Contains("Remove-AppxPackage")));
        Assert.Equal(apps.Select(a => a.Key).OrderBy(k => k), service.RemovedKeys().OrderBy(k => k));
    }

    [Fact]
    public async Task Reinstall_FallsBackToTheStore_WhenNoLocalCopyExists()
    {
        var shell = new FakeShellService
        {
            // The re-register script reports "no local copy" (exit 2).
            ScriptResultFactory = (script, _) => new PowerShellResult { ExitCode = script.Contains("Add-AppxPackage -Register") ? 2 : 0, StdOut = "", StdErr = "", TimedOut = false, Duration = TimeSpan.Zero }
        };
        var service = new BuiltInAppsService(shell, _log);
        var news = service.LoadApps().Single(a => a.PackageId == "Microsoft.BingNews");

        var report = await service.ReinstallAsync(new[] { news }, new OperationRunner());

        Assert.Equal(1, report.SucceededCount);
        var winget = Assert.Single(shell.ExecutedProcesses);
        Assert.Equal("winget.exe", winget[0]);
        Assert.Contains(news.StoreId, winget);
        Assert.Contains("msstore", winget);
    }
}
