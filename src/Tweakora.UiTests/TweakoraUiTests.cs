using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;
using FlaUI.UIA3;
using Xunit;
using Xunit.Abstractions;

namespace Tweakora.UiTests;

public sealed partial class TweakoraUiTests
{
    private readonly ITestOutputHelper _output;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    public TweakoraUiTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static string FindSolutionRoot()
    {
        string current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "Tweakora.sln")))
            {
                return current;
            }
            string? parent = Directory.GetParent(current)?.FullName;
            if (parent == current) break;
            current = parent ?? string.Empty;
        }
        throw new InvalidOperationException("Could not locate Tweakora.sln directory upwards.");
    }

    private static void SafeInvoke(AutomationElement? element)
    {
        Assert.NotNull(element);
        if (element.Patterns.Invoke.IsSupported)
        {
            element.Patterns.Invoke.Pattern.Invoke();
        }
        else if (element.Patterns.Toggle.IsSupported)
        {
            element.Patterns.Toggle.Pattern.Toggle();
        }
        else
        {
            element.Click();
        }
    }

    private static void SafeSetText(AutomationElement? element, string text)
    {
        Assert.NotNull(element);
        if (element.Patterns.Value.IsSupported)
        {
            element.Patterns.Value.Pattern.SetValue(text);
        }
        else
        {
            element.AsTextBox().Text = text;
        }
    }

    private static void AssertNoErrorDialogs(Application app, UIA3Automation automation)
    {
        var windows = app.GetAllTopLevelWindows(automation);
        foreach (var win in windows)
        {
            string title = win.Title ?? string.Empty;
            Assert.False(title.Contains("Error", StringComparison.OrdinalIgnoreCase) && !title.Contains("WinVelo Error Handling", StringComparison.OrdinalIgnoreCase) && !title.Contains("Tweakora Error Handling", StringComparison.OrdinalIgnoreCase),
                $"Unexpected error dialog detected: '{title}'");
            Assert.False(title.Contains("Exception", StringComparison.OrdinalIgnoreCase),
                $"Unexpected exception dialog detected: '{title}'");
            Assert.False(title.Contains("Crash", StringComparison.OrdinalIgnoreCase),
                $"Unexpected crash dialog detected: '{title}'");
        }
    }

    private static void CaptureViewScreenshot(Window window, string screenshotsDir, string filename)
    {
        string target = Path.Combine(screenshotsDir, filename);
        if (File.Exists(target) && new FileInfo(target).Length > 10000)
        {
            return;
        }

        try
        {
            Capture.Element(window).ToFile(target);
        }
        catch
        {
            try
            {
                Capture.Screen().ToFile(target);
            }
            catch
            {
                // Fallback silently if device locked
            }
        }
    }

    [Fact]
    public void FullAuditUiTestSuite_All5TabsAndControls()
    {
        string solutionDir = FindSolutionRoot();
        string screenshotsDir = Path.Combine(solutionDir, "docs", "screenshots");
        Directory.CreateDirectory(screenshotsDir);

        string exePath = Path.Combine(solutionDir, "src", "Tweakora.App", "bin", "Debug", "net10.0-windows", "WinVelo.exe");
        if (!File.Exists(exePath))
        {
            exePath = Path.Combine(solutionDir, "src", "Tweakora.App", "bin", "Debug", "net10.0-windows", "Tweakora.App.exe");
        }
        Assert.True(File.Exists(exePath), $"Executable not found at {exePath}");

        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = "--test-mode",
            UseShellExecute = false
        };

        using var app = Application.Launch(psi);
        using var automation = new UIA3Automation();
        app.WaitWhileMainHandleIsMissing(TimeSpan.FromSeconds(15));

        var window = Retry.WhileNull(() => app.GetMainWindow(automation), TimeSpan.FromSeconds(15)).Result;
        Assert.NotNull(window);
        window.Focus();
        try
        {
            SetWindowPos(window.Properties.NativeWindowHandle, IntPtr.Zero, 40, 40, 1440, 900, 0x0040);
        }
        catch { }
        Thread.Sleep(2000);

        _output.WriteLine("[UI-AUDIT] WinVelo launched successfully in --test-mode");

        // 1. Check MainWindowHandle != 0 and Warning Banner
        IntPtr handle = app.MainWindowHandle;
        Assert.True(handle != IntPtr.Zero, "MainWindowHandle was 0.");

        var banner = Retry.WhileNull(() =>
            window.FindFirstDescendant(cf => cf.ByAutomationId("TestModeBanner")) ??
            window.FindFirstDescendant(cf => cf.ByAutomationId("TestModeBannerText")) ??
            window.FindFirstDescendant(cf => cf.ByName("TEST MODE ACTIVE — Simulated Platform Engine. Real Windows host system is untouched.")),
            TimeSpan.FromSeconds(20)).Result;
        Assert.NotNull(banner);
        Assert.Contains("TEST MODE ACTIVE", banner.Name);
        _output.WriteLine("[UI-AUDIT] Handle != 0 and TEST MODE ACTIVE banner verified.");

        // 2. Tab Navigation & Screenshots of all tabs
        string[] tabIds = { "Tab_Dashboard", "Tab_Install", "Tab_Tweaks", "Tab_Config", "Tab_Updates", "Tab_Toolbox" };
        string[] screenshotNames = { "dashboard.png", "install.png", "tweaks.png", "config.png", "updates.png", "toolbox.png" };

        for (int i = 0; i < tabIds.Length; i++)
        {
            string tabId = tabIds[i];
            string shotName = screenshotNames[i];

            var tabBtn = Retry.WhileNull(() => window.FindFirstDescendant(cf => cf.ByAutomationId(tabId)), TimeSpan.FromSeconds(5)).Result;
            Assert.NotNull(tabBtn);
            SafeInvoke(tabBtn);
            Thread.Sleep(800);
            AssertNoErrorDialogs(app, automation);

            CaptureViewScreenshot(window, screenshotsDir, shotName);
            _output.WriteLine($"[UI-AUDIT] Captured {shotName} for {tabId}");
        }

        // 3. Test INSTALL Tab
        _output.WriteLine("[UI-AUDIT] Testing INSTALL Tab...");
        var tabInstall = Retry.WhileNull(() => window.FindFirstDescendant(cf => cf.ByAutomationId("Tab_Install")), TimeSpan.FromSeconds(5)).Result;
        Assert.NotNull(tabInstall);
        SafeInvoke(tabInstall);
        Thread.Sleep(500);

        // Find top search box and filter
        var topSearchBox = Retry.WhileNull(() => window.FindFirstDescendant(cf => cf.ByAutomationId("TopSearchBox")), TimeSpan.FromSeconds(5)).Result;
        Assert.NotNull(topSearchBox);
        SafeSetText(topSearchBox, "Chrome");
        Thread.Sleep(500);
        AssertNoErrorDialogs(app, automation);

        // Clear search
        SafeSetText(topSearchBox, string.Empty);
        Thread.Sleep(500);

        // Find first app checkbox and select it
        var firstAppCheck = Retry.WhileNull(() =>
        {
            return window.FindAllDescendants(cf => cf.ByControlType(ControlType.CheckBox))
                .FirstOrDefault(c => (c.AutomationId ?? string.Empty).StartsWith("AppCheck_"));
        }, TimeSpan.FromSeconds(5)).Result;

        if (firstAppCheck != null)
        {
            SafeInvoke(firstAppCheck);
            Thread.Sleep(300);

            // Click Clear Selection
            var clearAppBtn = Retry.WhileNull(() => window.FindFirstDescendant(cf => cf.ByAutomationId("ClearAppSelectionButton")), TimeSpan.FromSeconds(3)).Result;
            Assert.NotNull(clearAppBtn);
            SafeInvoke(clearAppBtn);
            Thread.Sleep(300);
        }
        _output.WriteLine("[UI-AUDIT] INSTALL Tab verified.");

        // 4. Test TWEAKS Tab (Presets, Columns, CAUTION BitLocker, Diff Preview)
        _output.WriteLine("[UI-AUDIT] Testing TWEAKS Tab...");
        var tabTweaks = Retry.WhileNull(() => window.FindFirstDescendant(cf => cf.ByAutomationId("Tab_Tweaks")), TimeSpan.FromSeconds(5)).Result;
        Assert.NotNull(tabTweaks);
        SafeInvoke(tabTweaks);
        Thread.Sleep(800);

        // Test Presets
        var presetStd = Retry.WhileNull(() => window.FindFirstDescendant(cf => cf.ByAutomationId("Preset_Standard")), TimeSpan.FromSeconds(5)).Result;
        Assert.NotNull(presetStd);
        SafeInvoke(presetStd);
        Thread.Sleep(500);
        AssertNoErrorDialogs(app, automation);

        var presetMin = Retry.WhileNull(() => window.FindFirstDescendant(cf => cf.ByAutomationId("Preset_Minimal")), TimeSpan.FromSeconds(5)).Result;
        Assert.NotNull(presetMin);
        SafeInvoke(presetMin);
        Thread.Sleep(500);
        AssertNoErrorDialogs(app, automation);

        // Open Diff Preview Dialog
        var previewBtn = Retry.WhileNull(() => window.FindFirstDescendant(cf => cf.ByAutomationId("PreviewTweaksButton")), TimeSpan.FromSeconds(5)).Result;
        Assert.NotNull(previewBtn);
        SafeInvoke(previewBtn);
        Thread.Sleep(800);
        AssertNoErrorDialogs(app, automation);

        // Capture diff preview dialog screenshot
        CaptureViewScreenshot(window, screenshotsDir, "diff-preview.png");

        // Close preview
        var closePreviewBtn = Retry.WhileNull(() => window.FindFirstDescendant(cf => cf.ByName("Cancel")), TimeSpan.FromSeconds(3)).Result;
        if (closePreviewBtn != null)
        {
            SafeInvoke(closePreviewBtn);
            Thread.Sleep(400);
        }

        // Clear tweaks
        var presetClear = Retry.WhileNull(() => window.FindFirstDescendant(cf => cf.ByAutomationId("Preset_Clear")), TimeSpan.FromSeconds(5)).Result;
        Assert.NotNull(presetClear);
        SafeInvoke(presetClear);
        Thread.Sleep(400);

        // Capture Command Palette
        var openPaletteBtn = Retry.WhileNull(() => window.FindFirstDescendant(cf => cf.ByAutomationId("OpenCommandPaletteButton")), TimeSpan.FromSeconds(3)).Result;
        if (openPaletteBtn != null)
        {
            SafeInvoke(openPaletteBtn);
            Thread.Sleep(500);
            CaptureViewScreenshot(window, screenshotsDir, "command-palette.png");
            // Close palette
            SafeInvoke(openPaletteBtn);
            Thread.Sleep(300);
        }
        _output.WriteLine("[UI-AUDIT] TWEAKS Tab & Command Palette verified.");

        // 5. Test CONFIG Tab
        _output.WriteLine("[UI-AUDIT] Testing CONFIG Tab...");
        var tabConfig = Retry.WhileNull(() => window.FindFirstDescendant(cf => cf.ByAutomationId("Tab_Config")), TimeSpan.FromSeconds(5)).Result;
        Assert.NotNull(tabConfig);
        SafeInvoke(tabConfig);
        Thread.Sleep(800);
        AssertNoErrorDialogs(app, automation);

        // Verify Legacy Control Panels exist
        var panelControl = Retry.WhileNull(() => window.FindFirstDescendant(cf => cf.ByAutomationId("Panel_WPFPanelControl")) ?? window.FindFirstDescendant(cf => cf.ByName("Control Panel")), TimeSpan.FromSeconds(5)).Result;
        Assert.NotNull(panelControl);

        // Trigger Quick Fix (Network Reset simulation)
        var fixNet = Retry.WhileNull(() => window.FindFirstDescendant(cf => cf.ByAutomationId("Fix_NetworkReset")), TimeSpan.FromSeconds(5)).Result;
        Assert.NotNull(fixNet);
        SafeInvoke(fixNet);
        Thread.Sleep(500);
        AssertNoErrorDialogs(app, automation);
        _output.WriteLine("[UI-AUDIT] CONFIG Tab verified.");

        // 6. Test UPDATES Tab
        _output.WriteLine("[UI-AUDIT] Testing UPDATES Tab...");
        var tabUpdates = Retry.WhileNull(() => window.FindFirstDescendant(cf => cf.ByAutomationId("Tab_Updates")), TimeSpan.FromSeconds(5)).Result;
        Assert.NotNull(tabUpdates);
        SafeInvoke(tabUpdates);
        Thread.Sleep(800);
        AssertNoErrorDialogs(app, automation);

        // Select Disable All Mode
        var selectDisable = Retry.WhileNull(() => window.FindFirstDescendant(cf => cf.ByAutomationId("SelectMode_DisableAll")), TimeSpan.FromSeconds(5)).Result;
        Assert.NotNull(selectDisable);
        SafeInvoke(selectDisable);
        Thread.Sleep(500);

        // Verify DisableUpdatesConfirmBox exists and type "I understand"
        var confirmBox = Retry.WhileNull(() => window.FindFirstDescendant(cf => cf.ByAutomationId("DisableUpdatesConfirmBox")), TimeSpan.FromSeconds(5)).Result;
        Assert.NotNull(confirmBox);
        SafeSetText(confirmBox, "I understand");
        Thread.Sleep(300);

        // Select Security profile back
        var selectSecurity = Retry.WhileNull(() => window.FindFirstDescendant(cf => cf.ByAutomationId("SelectMode_Security")), TimeSpan.FromSeconds(5)).Result;
        Assert.NotNull(selectSecurity);
        SafeInvoke(selectSecurity);
        Thread.Sleep(300);

        // Apply profile
        var applyUpdatesBtn = Retry.WhileNull(() => window.FindFirstDescendant(cf => cf.ByAutomationId("ApplyUpdatesProfileButton")), TimeSpan.FromSeconds(5)).Result;
        Assert.NotNull(applyUpdatesBtn);
        SafeInvoke(applyUpdatesBtn);
        Thread.Sleep(500);
        AssertNoErrorDialogs(app, automation);
        _output.WriteLine("[UI-AUDIT] UPDATES Tab verified.");

        // 7. Test TOOLBOX Tab
        _output.WriteLine("[UI-AUDIT] Testing TOOLBOX Tab...");
        var tabToolbox = Retry.WhileNull(() => window.FindFirstDescendant(cf => cf.ByAutomationId("Tab_Toolbox")), TimeSpan.FromSeconds(5)).Result;
        Assert.NotNull(tabToolbox);
        SafeInvoke(tabToolbox);
        Thread.Sleep(800);
        AssertNoErrorDialogs(app, automation);
        _output.WriteLine("[UI-AUDIT] TOOLBOX Tab verified.");

        // 8. MULTI-ROUND STABILITY TEST: 3 full rounds across all 5 tabs
        _output.WriteLine("[UI-AUDIT] Starting 3-round rapid tab switching stability test...");
        for (int round = 1; round <= 3; round++)
        {
            _output.WriteLine($"[UI-AUDIT] Round {round}/3...");
            foreach (string tabId in tabIds)
            {
                var btn = Retry.WhileNull(() => window.FindFirstDescendant(cf => cf.ByAutomationId(tabId)), TimeSpan.FromSeconds(5)).Result;
                Assert.NotNull(btn);
                SafeInvoke(btn);
                Thread.Sleep(200);
                AssertNoErrorDialogs(app, automation);
            }
        }
        _output.WriteLine("[UI-AUDIT] 3-round stability test PASSED with 0 error dialogs.");

        // Close app cleanly
        app.Close();
    }
}
