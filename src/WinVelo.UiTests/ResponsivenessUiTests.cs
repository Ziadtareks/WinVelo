using System;
using System.Linq;
using System.Threading;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;
using Xunit;

namespace WinVelo.UiTests;

public sealed partial class WinVeloUiTests
{
    [DesktopUiFact]
    public void DnsRegression_GoogleAndDhcpCompleteAndNavigationWorks()
    {
        var (app, automation, window) = LaunchAppInTestMode();
        try
        {
            NavigateTab(window, "Tools");
            SafeInvoke(WaitVisible(window, "Tool_DNS"));
            SafeInvoke(WaitVisible(window, "UseDnsOverHttpsCheckBox"));
            var google = WaitVisible(window, "ApplyDns_dns.google.public");
            SafeInvoke(google);
            Assert.True(Retry.WhileTrue(() => !WaitVisible(window, "DnsStatusText").Name.Contains("no network settings were changed"), TimeSpan.FromSeconds(15)).Result);
            Assert.True(google.IsEnabled);
            SafeInvoke(WaitVisible(window, "ResetDnsButton"));
            Assert.True(Retry.WhileTrue(() => !WaitVisible(window, "DnsStatusText").Name.Contains("Automatic (DHCP) successfully"), TimeSpan.FromSeconds(15)).Result);
            NavigateTab(window, "Home");
            NavigateTab(window, "Tweaks");
            AssertNoErrorDialogs(app, automation);
        }
        finally { app.Close(); automation.Dispose(); }
    }

    private static AutomationElement WaitVisible(Window window, string id)
    {
        var result = Retry.WhileNull(() =>
        {
            var element = window.FindFirstDescendant(cf => cf.ByAutomationId(id));
            return element is { IsOffscreen: false } ? element : null;
        }, TimeSpan.FromSeconds(20)).Result;
        Assert.NotNull(result);
        return result;
    }

    [DesktopUiFact]
    public void StartupRegression_ListToggleRefreshAndNavigation()
    {
        var (app, automation, window) = LaunchAppInTestMode();
        try
        {
            NavigateTab(window, "Tools");
            SafeInvoke(WaitVisible(window, "Tool_Startup"));
            var checkbox = WaitVisible(window, "StartupToggle_Discord").AsCheckBox();
            Assert.True(Retry.WhileTrue(() => !checkbox.IsEnabled, TimeSpan.FromSeconds(10)).Result);
            var initial = checkbox.ToggleState;
            window.Focus();
            // Exercise the accessible control action, including its OS command.
            checkbox.Patterns.Toggle.Pattern.Toggle();
            // Check the currently visible row rather than a cached UIA value.
            var changed = Retry.WhileTrue(() => WaitVisible(window, "StartupToggle_Discord").AsCheckBox().ToggleState == initial, TimeSpan.FromSeconds(10));
            Assert.True(changed.Result, "Startup toggle did not change its real displayed state.");
            var expected = WaitVisible(window, "StartupToggle_Discord").AsCheckBox().ToggleState;
            Assert.NotEqual(initial, expected);
            SafeInvoke(WaitVisible(window, "RefreshStartupButton"));
            Thread.Sleep(600);
            Assert.Equal(expected, WaitVisible(window, "StartupToggle_Discord").AsCheckBox().ToggleState);
            NavigateTab(window, "Home");
            NavigateTab(window, "History");
            AssertNoErrorDialogs(app, automation);
        }
        finally { app.Close(); automation.Dispose(); }
    }

    [DesktopUiFact]
    public void ApplyRegression_ProfileCompletesAndNavigationRemainsResponsive()
    {
        var (app, automation, window) = LaunchAppInTestMode();
        try
        {
            SafeInvoke(WaitVisible(window, "ProfileBtn_preset.recommended"));
            var confirm = WaitVisible(window, "PreviewConfirmButton");
            Assert.True(Retry.WhileTrue(() => !confirm.IsEnabled, TimeSpan.FromSeconds(20)).Result);
            Assert.True(confirm.IsEnabled);
            SafeInvoke(confirm);
            SafeInvoke(WaitVisible(window, "ResultCloseButton"));
            NavigateTab(window, "Tweaks");
            NavigateTab(window, "Tools");
            SafeInvoke(WaitVisible(window, "Tool_Startup"));
            Assert.NotNull(WaitVisible(window, "StartupToggle_Discord"));
            NavigateTab(window, "History");
            AssertNoErrorDialogs(app, automation);
        }
        finally { app.Close(); automation.Dispose(); }
    }
}
