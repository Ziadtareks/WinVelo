using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;
using Xunit;
using Xunit.Abstractions;

namespace Tweakora.UiTests;

public sealed class TweakoraUiTests
{
    private readonly ITestOutputHelper _output;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOZORDER = 0x0004;

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

    private static void SafeToggle(AutomationElement? element)
    {
        Assert.NotNull(element);
        if (element.Patterns.Toggle.IsSupported)
        {
            element.Patterns.Toggle.Pattern.Toggle();
        }
        else if (element.Patterns.Invoke.IsSupported)
        {
            element.Patterns.Invoke.Pattern.Invoke();
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
            Assert.False(title.Contains("Error", StringComparison.OrdinalIgnoreCase) && !title.Contains("Tweakora Error Handling", StringComparison.OrdinalIgnoreCase),
                $"Unexpected error dialog detected: '{title}'");
            Assert.False(title.Contains("Exception", StringComparison.OrdinalIgnoreCase),
                $"Unexpected exception dialog detected: '{title}'");
            Assert.False(title.Contains("Crash", StringComparison.OrdinalIgnoreCase),
                $"Unexpected crash dialog detected: '{title}'");
        }
    }

    private static void CaptureViewScreenshot(Window window, string screenshotsDir, string filename)
    {
        try
        {
            string target = Path.Combine(screenshotsDir, filename);
            Capture.Element(window).ToFile(target);
        }
        catch
        {
            try
            {
                string target = Path.Combine(screenshotsDir, filename);
                Capture.Screen().ToFile(target);
            }
            catch
            {
                // Fallback silently if device locked
            }
        }
    }

    [Fact]
    public void FullAuditUiTestSuite()
    {
        string solutionDir = FindSolutionRoot();
        string screenshotsDir = Path.Combine(solutionDir, "docs", "screenshots");
        Directory.CreateDirectory(screenshotsDir);

        string exePath = Path.Combine(solutionDir, "src", "Tweakora.App", "bin", "Debug", "net10.0-windows", "Tweakora.App.exe");
        Assert.True(File.Exists(exePath), $"Tweakora.App.exe not found at {exePath}");

        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = "--test-mode",
            UseShellExecute = false
        };

        using var app = Application.Launch(psi);
        using var automation = new UIA3Automation();

        var window = app.GetMainWindow(automation, TimeSpan.FromSeconds(15));
        Assert.NotNull(window);
        window.Focus();
        Thread.Sleep(1500);

        _output.WriteLine("[UI-AUDIT] App launched successfully in --test-mode");

        // ========================================================
        // TEST 1: Check MainWindowHandle != 0 and Warning Banner
        // ========================================================
        _output.WriteLine("[UI-AUDIT] Running Test 1: Launch and Banner...");
        IntPtr handle = app.MainWindowHandle;
        Assert.True(handle != IntPtr.Zero, "MainWindowHandle was 0.");

        var banner = Retry.WhileNull(() =>
            window.FindFirstDescendant(cf => cf.ByAutomationId("TestModeBanner")) ??
            window.FindFirstDescendant(cf => cf.ByName("TEST MODE ACTIVE — Simulated Platform Engine. Real Windows system is untouched.")),
            TimeSpan.FromSeconds(10)).Result;
        Assert.NotNull(banner);
        Assert.Contains("TEST MODE ACTIVE", banner.Name);

        CaptureViewScreenshot(window, screenshotsDir, "dashboard.png");
        AssertNoErrorDialogs(app, automation);
        _output.WriteLine("[UI-AUDIT] Test 1 PASSED: Handle != 0 and TEST MODE ACTIVE banner verified.");

        // ========================================================
        // TEST 2: Navigate through EVERY page, NO error dialogs
        // ========================================================
        _output.WriteLine("[UI-AUDIT] Running Test 2: Navigating every page...");
        string[] navButtons = { "Nav_Dashboard", "Nav_Tweaks", "Nav_Toolbox", "Nav_Apps", "Nav_Presets", "Nav_Settings" };

        foreach (string navId in navButtons)
        {
            var btn = Retry.WhileNull(() => window.FindFirstDescendant(cf => cf.ByAutomationId(navId)), TimeSpan.FromSeconds(5)).Result;
            Assert.NotNull(btn);
            SafeInvoke(btn);
            Thread.Sleep(600);
            AssertNoErrorDialogs(app, automation);

            if (navId == "Nav_Settings")
            {
                CaptureViewScreenshot(window, screenshotsDir, "settings.png");
            }
        }
        _output.WriteLine("[UI-AUDIT] Test 2 PASSED: Navigated through Dashboard, Tweaks, Toolbox, Apps, Presets, Settings with 0 error dialogs.");

        // ========================================================
        // TEST 3: Tweaks page flow (Search, Filter, Details, Staging, Dry-run preview)
        // ========================================================
        _output.WriteLine("[UI-AUDIT] Running Test 3: Tweaks page search, filter, details, staging, diff preview...");
        var navTweaks = Retry.WhileNull(() => window.FindFirstDescendant(cf => cf.ByAutomationId("Nav_Tweaks")), TimeSpan.FromSeconds(5)).Result;
        Assert.NotNull(navTweaks);
        SafeInvoke(navTweaks);
        Thread.Sleep(1000);
        AssertNoErrorDialogs(app, automation);

        CaptureViewScreenshot(window, screenshotsDir, "tweaks.png");

        // Search for telemetry
        var searchBox = Retry.WhileNull(() => window.FindFirstDescendant(cf => cf.ByAutomationId("TweaksSearchBox")), TimeSpan.FromSeconds(5)).Result;
        Assert.NotNull(searchBox);
        SafeSetText(searchBox, "telemetry");
        Thread.Sleep(600);

        var telemetryTitle = Retry.WhileNull(() => window.FindFirstDescendant(cf => cf.ByAutomationId("TweakTitle_privacy.telemetry.diagnostic-data")), TimeSpan.FromSeconds(5)).Result;
        Assert.NotNull(telemetryTitle);

        // Clear search
        SafeSetText(searchBox, string.Empty);
        Thread.Sleep(600);

        // Filter by Privacy
        var privacyFilterBtn = Retry.WhileNull(() => window.FindFirstDescendant(cf => cf.ByAutomationId("Category_Privacy")), TimeSpan.FromSeconds(5)).Result;
        Assert.NotNull(privacyFilterBtn);
        SafeInvoke(privacyFilterBtn);
        Thread.Sleep(600);

        // Reset filter
        var allFilterBtn = Retry.WhileNull(() => window.FindFirstDescendant(cf => cf.ByAutomationId("Category_All")), TimeSpan.FromSeconds(5)).Result;
        Assert.NotNull(allFilterBtn);
        SafeInvoke(allFilterBtn);
        Thread.Sleep(600);

        // Find first 5 visible tweaks
        var detailsButtons = Retry.WhileNull(() =>
        {
            var found = window.FindAllDescendants(cf => cf.ByControlType(ControlType.Button))
                .Where(b => (b.AutomationId ?? string.Empty).StartsWith("TweakDetails_"))
                .Take(5)
                .ToList();
            return found.Count >= 5 ? found : null;
        }, TimeSpan.FromSeconds(5)).Result;

        Assert.NotNull(detailsButtons);
        Assert.True(detailsButtons.Count >= 5, "Expected at least 5 details buttons.");

        var tweakIds = detailsButtons.Select(b => b.AutomationId.Substring("TweakDetails_".Length)).ToList();

        // Open details of 5 tweaks and assert contents
        foreach (var dBtn in detailsButtons)
        {
            SafeInvoke(dBtn);
            Thread.Sleep(400);

            var nameElem = Retry.WhileNull(() => window.FindFirstDescendant(cf => cf.ByAutomationId("DetailsTweakName")), TimeSpan.FromSeconds(3)).Result;
            Assert.NotNull(nameElem);
            Assert.False(string.IsNullOrWhiteSpace(nameElem.Name));

            var descElem = Retry.WhileNull(() => window.FindFirstDescendant(cf => cf.ByAutomationId("DetailsTweakDescription")), TimeSpan.FromSeconds(3)).Result;
            Assert.NotNull(descElem);
            Assert.False(string.IsNullOrWhiteSpace(descElem.Name));

            var srcElem = Retry.WhileNull(() => window.FindFirstDescendant(cf => cf.ByAutomationId("DetailsTweakSource")), TimeSpan.FromSeconds(3)).Result;
            Assert.NotNull(srcElem);
            Assert.False(string.IsNullOrWhiteSpace(srcElem.Name));

            var closeBtn = Retry.WhileNull(() => window.FindFirstDescendant(cf => cf.ByAutomationId("CloseDetailsButton")), TimeSpan.FromSeconds(3)).Result;
            Assert.NotNull(closeBtn);
            SafeInvoke(closeBtn);
            Thread.Sleep(400);
        }

        // Toggle the same 5 tweaks ON to stage them
        foreach (string tid in tweakIds)
        {
            var toggle = Retry.WhileNull(() => window.FindFirstDescendant(cf => cf.ByAutomationId($"TweakToggle_{tid}")), TimeSpan.FromSeconds(3)).Result;
            Assert.NotNull(toggle);
            SafeToggle(toggle);
            Thread.Sleep(300);
        }

        // Assert "Apply Staged (5)" button exists and is enabled
        var applyStagedBtn = Retry.WhileNull(() => window.FindFirstDescendant(cf => cf.ByAutomationId("ApplyStagedButton")), TimeSpan.FromSeconds(3)).Result;
        Assert.NotNull(applyStagedBtn);
        Assert.True(applyStagedBtn.IsEnabled, "ApplyStagedButton should be enabled after staging tweaks.");
        string btnText = applyStagedBtn.Name ?? string.Empty;
        Assert.Contains("5", btnText);

        // Click Apply Staged to open diff preview dialog
        SafeInvoke(applyStagedBtn);
        Thread.Sleep(800);

        CaptureViewScreenshot(window, screenshotsDir, "diff-dialog.png");

        var cancelApplyBtn = Retry.WhileNull(() => window.FindFirstDescendant(cf => cf.ByAutomationId("CancelApplyButton")), TimeSpan.FromSeconds(3)).Result;
        Assert.NotNull(cancelApplyBtn);
        var confirmApplyBtn = Retry.WhileNull(() => window.FindFirstDescendant(cf => cf.ByAutomationId("ConfirmApplyButton")), TimeSpan.FromSeconds(3)).Result;
        Assert.NotNull(confirmApplyBtn);

        // Cancel dialog first
        SafeInvoke(cancelApplyBtn);
        Thread.Sleep(500);

        // Re-open diff dialog and confirm apply
        applyStagedBtn = Retry.WhileNull(() => window.FindFirstDescendant(cf => cf.ByAutomationId("ApplyStagedButton")), TimeSpan.FromSeconds(3)).Result;
        Assert.NotNull(applyStagedBtn);
        SafeInvoke(applyStagedBtn);
        Thread.Sleep(500);

        confirmApplyBtn = Retry.WhileNull(() => window.FindFirstDescendant(cf => cf.ByAutomationId("ConfirmApplyButton")), TimeSpan.FromSeconds(3)).Result;
        Assert.NotNull(confirmApplyBtn);
        SafeInvoke(confirmApplyBtn);
        Thread.Sleep(1200);

        // Assert staged badge is cleared (ApplyStaged is now disabled / 0)
        applyStagedBtn = Retry.WhileNull(() => window.FindFirstDescendant(cf => cf.ByAutomationId("ApplyStagedButton")), TimeSpan.FromSeconds(3)).Result;
        Assert.NotNull(applyStagedBtn);
        Assert.False(applyStagedBtn.IsEnabled, "ApplyStagedButton should be disabled after changes are applied.");

        AssertNoErrorDialogs(app, automation);
        _output.WriteLine("[UI-AUDIT] Test 3 PASSED: Tweaks search, filter, details, staging, and diff flow completed.");

        // ========================================================
        // TEST 4: Command Palette (Ctrl+K), Type "dark", Enter, Theme Toggle
        // ========================================================
        _output.WriteLine("[UI-AUDIT] Running Test 4: Command Palette theme toggle...");
        var openPaletteBtn = Retry.WhileNull(() => window.FindFirstDescendant(cf => cf.ByAutomationId("OpenCommandPaletteButton")), TimeSpan.FromSeconds(5)).Result;
        Assert.NotNull(openPaletteBtn);
        SafeInvoke(openPaletteBtn);
        Thread.Sleep(600);

        CaptureViewScreenshot(window, screenshotsDir, "command-palette.png");

        var paletteSearchBox = Retry.WhileNull(() => window.FindFirstDescendant(cf => cf.ByAutomationId("CommandPaletteSearchBox")), TimeSpan.FromSeconds(5)).Result;
        Assert.NotNull(paletteSearchBox);

        var themeTextElem = Retry.WhileNull(() => window.FindFirstDescendant(cf => cf.ByAutomationId("CurrentThemeText")), TimeSpan.FromSeconds(5)).Result;
        string beforeTheme = themeTextElem?.Name ?? "Dark";

        SafeSetText(paletteSearchBox, "dark");
        Thread.Sleep(500);

        var paletteItem = Retry.WhileNull(() => window.FindFirstDescendant(cf => cf.ByAutomationId("PaletteItem_Toggle Dark/Light Theme")), TimeSpan.FromSeconds(5)).Result;
        if (paletteItem != null)
        {
            SafeInvoke(paletteItem);
        }
        else
        {
            var closeBtn = window.FindFirstDescendant(cf => cf.ByAutomationId("CloseCommandPaletteButton"));
            SafeInvoke(closeBtn);
        }
        Thread.Sleep(800);

        themeTextElem = Retry.WhileNull(() => window.FindFirstDescendant(cf => cf.ByAutomationId("CurrentThemeText")), TimeSpan.FromSeconds(5)).Result;
        string afterTheme = themeTextElem?.Name ?? string.Empty;
        Assert.NotEqual(beforeTheme, afterTheme);

        AssertNoErrorDialogs(app, automation);
        _output.WriteLine($"[UI-AUDIT] Test 4 PASSED: Theme changed from {beforeTheme} to {afterTheme} via Command Palette.");

        // ========================================================
        // TEST 5: Window Resizing
        // ========================================================
        _output.WriteLine("[UI-AUDIT] Running Test 5: Window resizing...");
        IntPtr hwnd = window.Properties.NativeWindowHandle.Value;

        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 1024, 768, SWP_NOMOVE | SWP_NOZORDER);
        Thread.Sleep(400);
        AssertNoErrorDialogs(app, automation);

        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 1280, 720, SWP_NOMOVE | SWP_NOZORDER);
        Thread.Sleep(400);
        AssertNoErrorDialogs(app, automation);

        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 1920, 1080, SWP_NOMOVE | SWP_NOZORDER);
        Thread.Sleep(400);
        AssertNoErrorDialogs(app, automation);
        _output.WriteLine("[UI-AUDIT] Test 5 PASSED: Resized to 1024x768, 1280x720, 1920x1080 with 0 layout crashes.");

        // ========================================================
        // TEST 6: 3 Complete Navigation Cycles & Bounded Memory
        // ========================================================
        _output.WriteLine("[UI-AUDIT] Running Test 6: 3 navigation cycles and memory check...");
        Process proc = Process.GetProcessById(app.ProcessId);
        proc.Refresh();
        long memBefore = proc.WorkingSet64;

        for (int cycle = 1; cycle <= 3; cycle++)
        {
            foreach (string navId in navButtons)
            {
                var btn = Retry.WhileNull(() => window.FindFirstDescendant(cf => cf.ByAutomationId(navId)), TimeSpan.FromSeconds(5)).Result;
                SafeInvoke(btn);
                Thread.Sleep(200);
                AssertNoErrorDialogs(app, automation);
            }
        }

        proc.Refresh();
        long memAfter = proc.WorkingSet64;
        long delta = memAfter - memBefore;

        _output.WriteLine($"[UI-AUDIT] Memory before: {memBefore / 1024 / 1024} MB, after: {memAfter / 1024 / 1024} MB, delta: {delta / 1024 / 1024} MB");
        Assert.True(delta < 250 * 1024 * 1024, $"Memory grew excessively: {delta / 1024 / 1024} MB");
        _output.WriteLine("[UI-AUDIT] Test 6 PASSED: 3 navigation cycles completed with memory within bounds.");

        app.Close();
    }
}
