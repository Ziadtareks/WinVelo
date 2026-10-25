using System.Text.Json;
using WinVelo.Cli;
using WinVelo.Core.Engine;
using WinVelo.Core.Interfaces;
using WinVelo.Core.Models;
using WinVelo.Core.Safety;
using WinVelo.Modules.Network;
using WinVelo.Platform;
using Xunit;

namespace WinVelo.Tests;

[Collection("ReadOnlyProcess")]
public sealed class DnsRegressionTests
{
    private static DnsProviderDefinition Google => new DnsService(new WinVelo.Simulation.FakeShellService()).GetProviders().First(p => p.Name == "Google");

    [Fact]
    public void IPv4OnlyAdapter_MatchesGoogleWithoutRequiringDisabledIPv6()
    {
        var state = new DnsAdapterState(true, false, false, true, ["8.8.8.8", "8.8.4.4"], []);
        Assert.True(state.Matches(Google));
        Assert.False((state with { IPv4Servers = ["1.1.1.1", "1.0.0.1"] }).Matches(Google));
        Assert.False((state with { IPv6Enabled = true }).Matches(Google));
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    public async Task GeneratedScript_RespectsEnabledFamilies(bool ipv6, bool dhcp, bool doh)
    {
        bool previous=TestModeManager.IsTestMode;
        try
        {
            TestModeManager.DisableTestMode();
            var provider=dhcp ? new DnsProviderDefinition{Id="dhcp", Name="DHCP", Description="",PrimaryDns="DHCP"}:Google;
            string mocks = """
$global:Calls4=0; $global:Calls6=0;
$global:DohEntries=@{};
function Get-DnsClientDohServerAddress { [CmdletBinding()]param($ServerAddress) $global:DohEntries[$ServerAddress] }
function Add-DnsClientDohServerAddress { [CmdletBinding()]param($ServerAddress,$DohTemplate,$AutoUpgrade,$AllowFallbackToUdp) if(-not $global:IPv6 -and $ServerAddress.Contains(':')){throw 'Disabled IPv6 DoH was accessed'}; $global:DohEntries[$ServerAddress]=[pscustomobject]@{DohTemplate=$DohTemplate;AutoUpgrade=$AutoUpgrade;AllowFallbackToUdp=$AllowFallbackToUdp} }
function Set-DnsClientDohServerAddress { [CmdletBinding()]param($ServerAddress,$DohTemplate,$AutoUpgrade,$AllowFallbackToUdp) Add-DnsClientDohServerAddress @PSBoundParameters }
function Get-NetAdapter { [CmdletBinding()]param([switch]$IncludeHidden) [pscustomobject]@{ifIndex=7; Name='Test'; InterfaceGuid=[guid]'00000000-0000-0000-0000-000000000001'} }
function Get-NetAdapterBinding { [CmdletBinding()]param($Name,$ComponentID,[switch]$IncludeHidden) [pscustomobject]@{Enabled=($ComponentID -eq 'ms_tcpip' -or $global:IPv6)} }
function Get-DnsClientServerAddress { [CmdletBinding()]param($InterfaceIndex,$AddressFamily) if($AddressFamily -eq 'IPv6' -and -not $global:IPv6){throw 'Disabled IPv6 was accessed'}; [pscustomobject]@{Family=$AddressFamily} }
function Set-DnsClientServerAddress { [CmdletBinding()]param([Parameter(ValueFromPipeline=$true)]$InputObject,[string[]]$ServerAddresses,[switch]$ResetServerAddresses) process { if($InputObject.Family -eq 'IPv4'){$global:Calls4++}else{$global:Calls6++} } }
if((Get-Command Set-DnsClientServerAddress).CommandType -ne 'Function'){throw 'DNS test stub is unavailable'}
if((Get-Command Add-DnsClientDohServerAddress).CommandType -ne 'Function' -or (Get-Command Set-DnsClientDohServerAddress).CommandType -ne 'Function'){throw 'DoH test stubs are unavailable'}
""";
            string script="$global:IPv6=$"+ipv6.ToString().ToLowerInvariant()+";\n"+mocks+"\n"+DnsService.BuildApplyScript(provider,7,doh)+"; [pscustomobject]@{Calls4=$global:Calls4; Calls6=$global:Calls6; DohCount=$global:DohEntries.Count} | ConvertTo-Json -Compress";
            var result=await new WindowsShellService().ExecutePowerShellAsync(script,15);
            Assert.True(result.Success,result.StdErr);
            using var doc=JsonDocument.Parse(result.StdOut);
            Assert.Equal(1,doc.RootElement.GetProperty("Calls4").GetInt32());
            Assert.Equal(ipv6?1:0,doc.RootElement.GetProperty("Calls6").GetInt32());
            Assert.Equal(doh?(ipv6?4:2):0,doc.RootElement.GetProperty("DohCount").GetInt32());
        }
        finally { TestModeManager.IsTestMode=previous; }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedChange_RestoresBaselineOrLeavesRecoverableJournal(bool rollbackFails)
    {
        bool previous=TestModeManager.IsTestMode;
        string path=Path.Combine(Path.GetTempPath(),"WinVelo-DnsRegression-"+Guid.NewGuid().ToString("N"));
        try
        {
            TestModeManager.DisableTestMode();
            var shell=new DnsShell{FailApply=true,FailRollback=rollbackFails};
            var journal=new RollbackJournalManager(path);
            var result=await new DnsService(shell,journal).ApplyDnsWithResultAsync(Google,7,false);
            Assert.False(result.Success);
            Assert.Contains("forced DNS failure",result.Message);
            var record=Assert.Single(Assert.Single(journal.GetAllSessions()).Records);
            Assert.Equal(!rollbackFails,record.IsReverted);
            Assert.Equal(rollbackFails?1:0,journal.GetUnfinishedSessions().Count);
            Assert.DoesNotContain(shell.Scripts.Where(s=>s.StartsWith("Get-DnsClientServerAddress")),s=>s.Contains("IPv6"));
        }
        finally { TestModeManager.IsTestMode=previous; if(Directory.Exists(path)) { Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()),Path.GetFullPath(path)); Assert.StartsWith("WinVelo-DnsRegression-",Path.GetFileName(path)); Directory.Delete(path,true); } }
    }

    [Fact]
    public async Task VerificationFailure_TriggersRollbackRatherThanReportingCustomSuccess()
    {
        bool previous=TestModeManager.IsTestMode;
        try
        {
            TestModeManager.DisableTestMode();
            var shell=new DnsShell();
            var custom=new DnsProviderDefinition{Id="custom",Name="Custom",Description="",PrimaryDns="8.8.8.8",SecondaryDns="8.8.4.4"};
            var result=await new DnsService(shell).ApplyDnsWithResultAsync(custom,7,false);
            Assert.False(result.Success);
            Assert.Contains("did not retain",result.Message);
            Assert.Contains(shell.Scripts,s=>s.StartsWith("Get-DnsClientServerAddress")&&s.Contains("1.1.1.1"));
        }
        finally{TestModeManager.IsTestMode=previous;}
    }

    private sealed class DnsShell : IShellService
    {
        public bool FailApply {get;init;}
        public bool FailRollback {get;init;}
        public List<string> Scripts {get;}=[];
        public Task<PowerShellResult> ExecutePowerShellAsync(string script,int timeoutSeconds=60)
        {
            Scripts.Add(script);
            bool read=script.Contains("IPv4Automatic=[string]");
            bool fail=script.Contains("$ips=@")?FailApply:script.StartsWith("Get-DnsClientServerAddress")&&FailRollback;
            string output=read?"""{"IPv4Enabled":true,"IPv6Enabled":false,"IPv4Automatic":false,"IPv6Automatic":true,"IPv4Servers":["1.1.1.1","1.0.0.1"],"IPv6Servers":[]}""":"";
            return Task.FromResult(new PowerShellResult{ExitCode=fail?1:0,StdOut=output,StdErr=fail?"forced DNS failure":"",TimedOut=false,Duration=TimeSpan.Zero});
        }
        public Task<PowerShellResult> RunProcessAsync(string fileName,IReadOnlyList<string> arguments,int timeoutSeconds=60,CancellationToken cancellationToken=default)=>throw new NotSupportedException();
        public void RestartExplorer(){}
        public void RefreshShellEnvironment(){}
        public void OpenUrl(string url){}
        public void LaunchApplet(string command){}
    }
}

public sealed class CliArgumentRegressionTests
{
    [Theory]
    [InlineData("--dns")]
    [InlineData("--unknown")]
    [InlineData("--adapter", "0")]
    [InlineData("--dns", "Google", "--real-mode", "--test-mode")]
    [InlineData("--tweak", "a", "--dns", "Google")]
    [InlineData("--dns", "Google", "--enable")]
    [InlineData("--dns", "Google", "--undo")]
    [InlineData("--list", "--restore")]
    [InlineData("--self-test", "--level", "2")]
    public void InvalidArguments_AreRejected(params string[] args)=>Assert.Throws<ArgumentException>(()=>CliHost.Normalize(args));

    [Fact]
    public void QuotedPathsAndEqualValues_ArePreserved()
    {
        Assert.Equal(new[]{"--config",@"C:\My Folder\config.json","--dry-run"},CliHost.Split("--config \"C:\\My Folder\\config.json\" --dry-run"));
        Assert.Equal(new[]{"--dns","Google","--adapter","16"},CliHost.Normalize(["--DNS=Google","--adapter-index=16"]));
    }

    [Fact]
    public async Task InteractiveHost_StaysOpenAfterFailureAndAcceptsMoreCommands()
    {
        using var input=new StringReader("--unknown\n--list\nexit\n");
        using var output=new StringWriter(); int calls=0;
        int code=await CliHost.RunInteractiveAsync(input,output,args=>{CliHost.Normalize(args);calls++;return Task.FromResult(0);});
        Assert.Equal(0,code);Assert.Equal(1,calls);Assert.Contains("[ERROR]",output.ToString());Assert.Contains("[EXIT CODE] 0",output.ToString());
    }
}

[Collection("ReadOnlyProcess")]
public sealed class CliDryRunRegressionTests
{
    [Theory]
    [InlineData("--dns", "Google")]
    [InlineData("--feature", "WPFFeaturesdotnet", "--enable")]
    [InlineData("--clean", "all")]
    [InlineData("--update-mode", "Security")]
    [InlineData("--fix", "dns_flush")]
    [InlineData("--preset", "gaming", "--run")]
    [InlineData("--tweak", "ui.explorer.show-file-extensions", "--undo")]
    [InlineData("--recover", "rollback")]
    public async Task DryRun_PreviewsWithoutExecuting(params string[] command)
    {
        bool previous = TestModeManager.IsTestMode;
        var stdout = Console.Out;
        var stderr = Console.Error;
        using var output = new StringWriter();
        try
        {
            Console.SetOut(output);
            Console.SetError(output);
            int code = await Program.Main([..command, "--dry-run", "--test-mode"]);
            Assert.Equal(0, code);
            Assert.Contains("[DRY RUN", output.ToString());
            Assert.DoesNotContain("[EXECUTE]", output.ToString());
        }
        finally
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            TestModeManager.IsTestMode = previous;
        }
    }
}

[Collection("ReadOnlyProcess")]
public sealed class StartupAccessibilityRegressionTests
{
    [Fact]
    public async Task StartupToggle_PreservesUnchangedRowContainers()
    {
        bool previous = TestModeManager.IsTestMode;
        try
        {
            TestModeManager.EnableTestMode();
            var vm = new WinVelo.App.ViewModels.StartupViewModel(new WinVelo.Modules.Startup.StartupService(new WinVelo.Simulation.FakeRegistryService()));
            await vm.RefreshAsync();
            var before = vm.FilteredApps.ToArray();
            int changes = 0;
            vm.FilteredApps.CollectionChanged += (_, _) => changes++;
            await vm.ToggleAppAsync(before.First(entry => entry.Name == "Discord"));
            Assert.Equal(0, changes);
            Assert.Equal(before, vm.FilteredApps);
            Assert.False(vm.FilteredApps.First(entry => entry.Name == "Discord").IsEnabled);
        }
        finally { TestModeManager.IsTestMode = previous; }
    }

    [Fact]
    public void AccessibleToggle_ExecutesCommandAndRespectsDisabledState()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                int calls = 0;
                var control = new WinVelo.App.Controls.CommandCheckBox { IsChecked = true };
                control.Command = new CommunityToolkit.Mvvm.Input.RelayCommand(() => { calls++; control.IsChecked = false; });
                var peer = System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(control)!;
                var provider = Assert.IsAssignableFrom<System.Windows.Automation.Provider.IToggleProvider>(peer.GetPattern(System.Windows.Automation.Peers.PatternInterface.Toggle));
                provider.Toggle();
                System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                Assert.Equal(1, calls);
                Assert.False(control.IsChecked);
                control.IsEnabled = false;
                Assert.Throws<System.Windows.Automation.ElementNotEnabledException>(() => provider.Toggle());
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "Accessible toggle did not finish.");
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [Fact]
    public async Task SimulatedStartup_UsesSeededEntriesAndExcludesDesktopMetadata()
    {
        bool previous = TestModeManager.IsTestMode;
        try
        {
            TestModeManager.EnableTestMode();
            var registry = new WinVelo.Simulation.FakeRegistryService();
            registry.SetValue("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder", "desktop.ini", new byte[12], "Binary");
            var entries = await new WinVelo.Modules.Startup.StartupService(registry).GetStartupEntriesAsync();
            Assert.Equal(10, entries.Count);
            Assert.DoesNotContain(entries, entry => entry.Name == "desktop");
            Assert.Contains(entries, entry => entry.Name == "Microsoft Teams");
        }
        finally { TestModeManager.IsTestMode = previous; }
    }
}
