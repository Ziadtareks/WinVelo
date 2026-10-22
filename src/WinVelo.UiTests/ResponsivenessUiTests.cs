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

    [Fact]
    public void StartupRegression_ListToggleRefreshAndNavigation()
    {
        var (app, automation, window) = LaunchAppInTestMode();
        try
        {
            NavigateTab(window, "Tools");
            SafeInvoke(WaitVisible(window, "Tool_Startup"));
            var checkbox = WaitVisible(window, "StartupToggle_Discord").AsCheckBox();
            var initial = checkbox.ToggleState;
            checkbox.Click();
            var changed = Retry.WhileTrue(() => checkbox.ToggleState == initial, TimeSpan.FromSeconds(10));
            Assert.True(changed.Result, "Startup toggle did not change its real displayed state.");
            Assert.NotEqual(initial, checkbox.ToggleState);
            var expected = checkbox.ToggleState;
            SafeInvoke(WaitVisible(window, "RefreshStartupButton"));
            Thread.Sleep(600);
            Assert.Equal(expected, WaitVisible(window, "StartupToggle_Discord").AsCheckBox().ToggleState);
            NavigateTab(window, "Home");
            NavigateTab(window, "History");
            AssertNoErrorDialogs(app, automation);
        }
        finally { app.Close(); automation.Dispose(); }
    }

    [Fact]
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
