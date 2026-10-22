using System.Collections.Concurrent;
using System.Threading;
using System.Xml.Linq;
using WinVelo.Core.Safety;
using WinVelo.Platform;
using WinVelo.Simulation;
using Xunit;

namespace WinVelo.Tests;

[CollectionDefinition("ReadOnlyProcess", DisableParallelization = true)]
public sealed class ReadOnlyProcessCollection { }

[Collection("ReadOnlyProcess")]
public sealed class ResponsivenessRegressionTests
{
    private sealed class QueuedContext : SynchronizationContext
    {
        public readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> Queue = new();
        public override void Post(SendOrPostCallback callback, object? state) => Queue.Enqueue((callback, state));
        public void Drain() { while (Queue.TryDequeue(out var work)) work.Callback(work.State); }
    }

    private static void AssertDoesNotNeedUiPump(Action operation)
    {
        var context = new QueuedContext();
        Exception? error = null;
        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(context);
            try { operation(); } catch (Exception ex) { error = ex; }
        }) { IsBackground = true };
        thread.Start();
        bool completed = thread.Join(TimeSpan.FromSeconds(4));
        // Release captured continuations after a failure, so the reproducer leaves no hung work behind.
        var cleanup = System.Diagnostics.Stopwatch.StartNew();
        while (thread.IsAlive && cleanup.Elapsed < TimeSpan.FromSeconds(5)) { context.Drain(); thread.Join(10); }
        Assert.True(completed, "Operation deadlocked waiting for the blocked UI synchronization context.");
        Assert.Null(error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadOnlyProcess_CompletesWithoutUiPump(bool powershell)
    {
        bool previous = TestModeManager.IsTestMode;
        try
        {
            TestModeManager.DisableTestMode();
            AssertDoesNotNeedUiPump(() =>
            {
                var shell = new WindowsShellService();
                var result = powershell
                    ? shell.ExecutePowerShellAsync("Start-Sleep -Milliseconds 100; 'responsive'", 3).GetAwaiter().GetResult()
                    : shell.RunProcessAsync("cmd.exe", new[] { "/d", "/c", "echo responsive" }, 3).GetAwaiter().GetResult();
                Assert.True(result.Success, result.StdErr);
                Assert.Contains("responsive", result.StdOut);
            });
        }
        finally { TestModeManager.IsTestMode = previous; }
    }

    [Fact]
    public void HealthCheck_CompletesWithoutUiPump()
        => AssertDoesNotNeedUiPump(() =>
            ServicingProtectionHelper.CheckServicingHealthAsync(new FakeServicesService(), new FakeRegistryService()).GetAwaiter().GetResult());

    [Fact]
    public void RepairWithFakeServices_CompletesWithoutUiPump()
        => AssertDoesNotNeedUiPump(() =>
            ServicingProtectionHelper.RestoreServicingComponentsAsync(new FakeServicesService(), new FakeRegistryService()).GetAwaiter().GetResult());

    [Fact]
    public void ProcessTimeout_CompletesWithoutUiPump()
    {
        bool previous = TestModeManager.IsTestMode;
        try
        {
            TestModeManager.DisableTestMode();
            AssertDoesNotNeedUiPump(() =>
            {
                var result = new WindowsShellService().ExecutePowerShellAsync("Start-Sleep -Seconds 10", 1).GetAwaiter().GetResult();
                Assert.True(result.TimedOut);
                Assert.False(result.Success);
            });
        }
        finally { TestModeManager.IsTestMode = previous; }
    }

    [Fact]
    public void StartupTool_BindsExistingCollectionAndOneWayToggle()
    {
        string? root = AppContext.BaseDirectory;
        while (root != null && !File.Exists(Path.Combine(root, "WinVelo.sln"))) root = Directory.GetParent(root)?.FullName;
        Assert.NotNull(root);
        var doc = XDocument.Load(Path.Combine(root, "src", "WinVelo.App", "Views", "ToolsTabView.xaml"));
        XNamespace ns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var list = doc.Descendants(ns + "ItemsControl").Single(e => ((string?)e.Attribute("ItemsSource"))?.Contains("StartupVM.") == true);
        Assert.Equal("{Binding StartupVM.FilteredApps}", (string?)list.Attribute("ItemsSource"));
        var toggle = list.Descendants(ns + "CheckBox").Single();
        Assert.Contains("StartupVM.ToggleAppCommand", (string?)toggle.Attribute("Command"));
        Assert.Contains("Mode=OneWay", (string?)toggle.Attribute("IsChecked"));
    }
}
