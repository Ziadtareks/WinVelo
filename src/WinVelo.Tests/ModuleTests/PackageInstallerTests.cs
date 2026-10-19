using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WinVelo.Core.Models;
using WinVelo.Core.Operations;
using WinVelo.Core.Safety;
using WinVelo.Core.Utilities;
using WinVelo.Modules.Apps;
using WinVelo.Simulation;
using Xunit;

namespace WinVelo.Tests.ModuleTests;

/// <summary>winget/choco are started with argument lists, report clear errors, and can be cancelled.</summary>
public sealed class PackageInstallerTests
{
    public PackageInstallerTests()
    {
        TestModeManager.EnableTestMode();
    }

    private static PowerShellResult Result(int exitCode, string stdout = "", string stderr = "", bool failedToStart = false, bool cancelled = false, bool timedOut = false) => new()
    {
        ExitCode = exitCode,
        StdOut = stdout,
        StdErr = stderr,
        TimedOut = timedOut,
        FailedToStart = failedToStart,
        Cancelled = cancelled,
        Duration = TimeSpan.Zero
    };

    private static ApplicationItem App(string key, string winget, string choco = "") => new()
    {
        Key = key,
        Name = key,
        Category = "Test",
        WingetId = winget,
        ChocoId = choco
    };

    [Fact]
    public void AllCatalogApps_HaveWellFormedIdsAndWebLinks()
    {
        var apps = new AppInstallService(new FakeShellService()).LoadApplications();

        Assert.NotEmpty(apps);
        foreach (var app in apps)
        {
            Assert.True(app.WingetId.Length == 0 || PackageIds.IsValidWingetId(app.WingetId), $"{app.Key}: winget id '{app.WingetId}'");
            Assert.True(app.ChocoId.Length == 0 || PackageIds.IsValidChocolateyId(app.ChocoId), $"{app.Key}: choco id '{app.ChocoId}'");
            Assert.True(app.WingetId.Length > 0 || app.ChocoId.Length > 0, $"{app.Key} has no package id");
            Assert.True(SafeUrl.IsWebUrl(app.Link), $"{app.Key}: link '{app.Link}'");
        }
    }

    [Theory]
    [InlineData("Google.Chrome", true)]
    [InlineData("Notepad++.Notepad++", true)]
    [InlineData("Python.Python.3.12", true)]
    [InlineData("9NKSQGP7F2NH", true)]
    [InlineData("Chrome", false)]
    [InlineData("Google.Chrome; calc", false)]
    [InlineData("Google.Chrome\" & calc & \"", false)]
    [InlineData("$(calc).Chrome", false)]
    [InlineData("Google.Chrome --override x", false)]
    [InlineData("", false)]
    public void WingetIdFormat(string id, bool valid)
    {
        Assert.Equal(valid, PackageIds.IsValidWingetId(id));
    }

    [Theory]
    [InlineData("googlechrome", true)]
    [InlineData("paint.net", true)]
    [InlineData("microsoft-windows-terminal", true)]
    [InlineData("git;calc", false)]
    [InlineData("-y", false)]
    [InlineData("a b", false)]
    public void ChocolateyIdFormat(string id, bool valid)
    {
        Assert.Equal(valid, PackageIds.IsValidChocolateyId(id));
    }

    [Fact]
    public void Arguments_AreSeparateItems_AndStoreIdsUseMsStore()
    {
        var community = PackageCommands.Arguments("WinGet", PackageAction.Install, "Google.Chrome");
        Assert.Equal(new[] { "install", "--id", "Google.Chrome", "--exact", "--source", "winget" }, community.Take(6));

        var store = PackageCommands.Arguments("WinGet", PackageAction.Install, "9NKSQGP7F2NH");
        Assert.Contains("msstore", store);

        var choco = PackageCommands.Arguments("Chocolatey", PackageAction.Uninstall, "googlechrome");
        Assert.Equal(new[] { "uninstall", "googlechrome", "-y" }, choco);
    }

    [Fact]
    public void Arguments_RejectAnythingThatIsNotAPlainId()
    {
        Assert.Throws<ArgumentException>(() => PackageCommands.Arguments("WinGet", PackageAction.Install, "Google.Chrome\"; Remove-Item C:\\ -Recurse; \""));
        Assert.Throws<ArgumentException>(() => PackageCommands.Arguments("Chocolatey", PackageAction.Install, "git & calc"));
    }

    [Fact]
    public void Interpret_ExplainsMissingTool_Cancel_Timeout_AndErrors()
    {
        Assert.Contains("App Installer", PackageCommands.Interpret("WinGet", PackageAction.Install, Result(-1, failedToStart: true)).Message);
        Assert.Contains("choco.exe", PackageCommands.Interpret("Chocolatey", PackageAction.Install, Result(-1, failedToStart: true)).Message);
        Assert.Equal(OperationItemStatus.Skipped, PackageCommands.Interpret("WinGet", PackageAction.Install, Result(-1, cancelled: true)).Status);
        Assert.Contains("internet", PackageCommands.Interpret("WinGet", PackageAction.Install, Result(-1, timedOut: true)).Message);

        Assert.Equal(OperationItemStatus.Succeeded, PackageCommands.Interpret("WinGet", PackageAction.Install, Result(0, "Successfully installed")).Status);
        Assert.Equal(OperationItemStatus.Succeeded, PackageCommands.Interpret("WinGet", PackageAction.Install, Result(PackageCommands.WingetAlreadyInstalled)).Status);
        Assert.Equal(OperationItemStatus.Succeeded, PackageCommands.Interpret("Chocolatey", PackageAction.Install, Result(3010)).Status);

        var notFound = PackageCommands.Interpret("WinGet", PackageAction.Install, Result(PackageCommands.WingetNoPackageFound));
        Assert.Equal(OperationItemStatus.Failed, notFound.Status);
        Assert.Contains("could not find", notFound.Message);

        var other = PackageCommands.Interpret("WinGet", PackageAction.Install, Result(unchecked((int)0x80072EE7), stderr: "network error"));
        Assert.Equal(OperationItemStatus.Failed, other.Status);
        Assert.Contains("0x80072EE7", other.Message);

        // Exit code 0 is success even when an app's own output contains the word "Error".
        Assert.Equal(OperationItemStatus.Succeeded, PackageCommands.Interpret("WinGet", PackageAction.Install, Result(0, "Installed Error Lens")).Status);
    }

    [Fact]
    public void ListContains_MatchesWholeIdsAndNames_NotSubstrings()
    {
        string list = "Name             Id                   Version\n" +
                      "-------------------------------------------\n" +
                      "GitHub Desktop   GitHub.GitHubDesktop 3.4.1\n" +
                      "Mozilla Firefox  Mozilla.Firefox      131.0\n";

        Assert.True(PackageCommands.ListContains(list, "GitHub.GitHubDesktop", "GitHub Desktop"));
        Assert.True(PackageCommands.ListContains(list, "Mozilla.Firefox", "Firefox"));
        Assert.False(PackageCommands.ListContains(list, "Git.Git", "Git"));
        Assert.False(PackageCommands.ListContains(list, "Mozilla.Firefox.ESR", "Firefox ESR"));
    }

    [Fact]
    public async Task Install_RunsWingetWithArgumentList_NeverThroughPowerShell()
    {
        var shell = new FakeShellService();
        var service = new AppInstallService(shell);
        var runner = new OperationRunner();

        var report = await service.InstallAppsAsync(new[] { App("chrome", "Google.Chrome", "googlechrome") }, "WinGet", runner);

        Assert.Empty(shell.ExecutedScripts);
        var call = Assert.Single(shell.ExecutedProcesses);
        Assert.Equal("winget.exe", call[0]);
        Assert.Contains("Google.Chrome", call);
        Assert.Equal(1, report.SucceededCount);
    }

    [Fact]
    public async Task Install_WithBadId_FailsWithoutStartingAnything()
    {
        var shell = new FakeShellService();
        var service = new AppInstallService(shell);

        var report = await service.InstallAppsAsync(new[] { App("evil", "Evil.App\"; calc; \"") }, "WinGet", new OperationRunner());

        Assert.Empty(shell.ExecutedProcesses);
        Assert.Empty(shell.ExecutedScripts);
        Assert.Equal(1, report.FailedCount);
    }

    [Fact]
    public async Task Install_WithChocolatey_NeverFallsBackToTheWingetId()
    {
        var shell = new FakeShellService();
        var service = new AppInstallService(shell);

        var report = await service.InstallAppsAsync(new[] { App("wingetonly", "Some.App") }, "Chocolatey", new OperationRunner());

        Assert.Empty(shell.ExecutedProcesses);
        Assert.Equal(1, report.SkippedCount);
    }

    [Fact]
    public async Task Install_WhenWingetIsMissing_ReportsAClearFailure()
    {
        var shell = new FakeShellService
        {
            ProcessResultFactory = (_, _) => Result(-1, stderr: "The system cannot find the file specified", failedToStart: true)
        };
        var service = new AppInstallService(shell);

        Assert.False(await service.IsPackageManagerAvailableAsync("WinGet"));
        var report = await service.InstallAppsAsync(new[] { App("chrome", "Google.Chrome") }, "WinGet", new OperationRunner());

        Assert.Equal(1, report.FailedCount);
        Assert.Contains(report.Results, i => (i.Reason ?? string.Empty).Contains("App Installer"));
    }

    [Fact]
    public async Task Install_Cancelled_StopsBeforeTheNextApp()
    {
        var shell = new FakeShellService();
        var service = new AppInstallService(shell);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var report = await service.InstallAppsAsync(new[] { App("a", "A.A"), App("b", "B.B") }, "WinGet", new OperationRunner(), cts.Token);

        Assert.Empty(shell.ExecutedProcesses);
        Assert.Equal(0, report.SucceededCount);
    }

    [Theory]
    [InlineData("https://www.videolan.org/vlc/", true)]
    [InlineData("http://example.com", true)]
    [InlineData("C:\\Windows\\System32\\calc.exe", false)]
    [InlineData("file:///C:/Windows/System32/calc.exe", false)]
    [InlineData("\\\\server\\share\\x.exe", false)]
    [InlineData("ms-settings:privacy", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("httpx://example.com", false)]
    [InlineData("", false)]
    public void SafeUrl_OnlyAllowsWebLinks(string url, bool allowed)
    {
        Assert.Equal(allowed, SafeUrl.IsWebUrl(url));
    }
}
