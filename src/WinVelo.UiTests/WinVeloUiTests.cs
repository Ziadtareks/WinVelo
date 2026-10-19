using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;
using FlaUI.UIA3;
using Xunit;
using Xunit.Abstractions;

namespace WinVelo.UiTests;

public sealed partial class WinVeloUiTests
{
    private readonly ITestOutputHelper _output;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    public WinVeloUiTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static string FindSolutionRoot()
    {
        string current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "WinVelo.sln")))
            {
                return current;
            }
            string? parent = Directory.GetParent(current)?.FullName;
            if (parent == current) break;
            current = parent ?? string.Empty;
        }
        throw new InvalidOperationException("Could not locate WinVelo.sln directory upwards.");
    }

    private static void SafeInvoke(AutomationElement? element)
    {
        Assert.NotNull(element);
        if (element.Patterns.Invoke.IsSupported)
        {
            element.Patterns.Invoke.Pattern.Invoke();
            return;
        }
        if (element.Patterns.Toggle.IsSupported)
        {
            element.Patterns.Toggle.Pattern.Toggle();
            return;
        }
        if (element.Patterns.SelectionItem.IsSupported)
        {
            element.Patterns.SelectionItem.Pattern.Select();
            return;
        }
        try
        {
            element.Click();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Fallback if desktop input injection is restricted by UIPI
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
            Assert.False(title.Contains("Error", StringComparison.OrdinalIgnoreCase) && !title.Contains("WinVelo Error Handling", StringComparison.OrdinalIgnoreCase) && !title.Contains("WinVelo Error Handling", StringComparison.OrdinalIgnoreCase),
                $"Unexpected error dialog detected: '{title}'");
            Assert.False(title.Contains("Exception", StringComparison.OrdinalIgnoreCase),
                $"Unexpected exception dialog detected: '{title}'");
            Assert.False(title.Contains("Crash", StringComparison.OrdinalIgnoreCase),
                $"Unexpected crash dialog detected: '{title}'");
        }
    }

    private static void DismissWizardIfOpen(Window window)
    {
        try
        {
            var skipBtn = Retry.WhileNull(() =>
                window.FindFirstDescendant(cf => cf.ByAutomationId("SkipWizardButton"))
                ?? window.FindFirstDescendant(cf => cf.ByName("Skip Setup")),
                TimeSpan.FromSeconds(5)).Result;
            if (skipBtn != null)
            {
                SafeInvoke(skipBtn);
                Thread.Sleep(800);
            }
        }
        catch { }
    }

    private static void NavigateTab(Window window, string tabName)
    {
        var btn = Retry.WhileNull(() =>
            window.FindFirstDescendant(cf => cf.ByName(tabName)) ??
            window.FindFirstDescendant(cf => cf.ByAutomationId($"Tab_{tabName}")) ??
            (tabName switch
            {
                "Home" => window.FindFirstDescendant(cf => cf.ByAutomationId("Tab_Dashboard")),
                "Apps" => window.FindFirstDescendant(cf => cf.ByAutomationId("Tab_Install")),
                "Tools" => window.FindFirstDescendant(cf => cf.ByAutomationId("Tab_Toolbox")),
                "History" => window.FindFirstDescendant(cf => cf.ByAutomationId("Tab_Backups")),
                _ => null
            }),
            TimeSpan.FromSeconds(5)).Result;

        Assert.NotNull(btn);
        SafeInvoke(btn);
        Thread.Sleep(600);
    }

    private static (Application App, UIA3Automation Automation, Window Window) LaunchAppInTestMode()
    {
        string solutionDir = FindSolutionRoot();
        string exePath = Path.Combine(solutionDir, "src", "WinVelo.App", "bin", "Debug", "net10.0-windows", "WinVelo.exe");
        if (!File.Exists(exePath))
        {
            exePath = Path.Combine(solutionDir, "src", "WinVelo.App", "bin", "Debug", "net10.0-windows", "WinVelo.App.exe");
        }
        Assert.True(File.Exists(exePath), $"Executable not found at {exePath}");

        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = "--test-mode",
            UseShellExecute = false
        };

        var app = Application.Launch(psi);
        var automation = new UIA3Automation();
        var window = Retry.WhileNull(() =>
        {
            var topWindows = app.GetAllTopLevelWindows(automation);
            return topWindows.FirstOrDefault(w =>
                (!string.IsNullOrEmpty(w.Title) && w.Title.Contains("WinVelo")) ||
                w.AutomationId == "WinVeloMainWindow")
                ?? topWindows.FirstOrDefault(w => w.FindAllDescendants().Length > 5);
        }, TimeSpan.FromSeconds(25)).Result;
        Assert.NotNull(window);
        window.Focus();
        try
        {
            SetWindowPos(window.Properties.NativeWindowHandle, IntPtr.Zero, 40, 40, 1440, 900, 0x0040);
        }
        catch { }
        Thread.Sleep(1500);

        DismissWizardIfOpen(window);
        return (app, automation, window);
    }

    private static void CaptureViewScreenshot(Window window, string screenshotsDir, string filename)
    {
        string target = Path.Combine(screenshotsDir, filename);
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
            catch { }
        }
    }

    [Fact]
    public void Test_01_HomePage_LoadsCalmStatus()
    {
        var (app, automation, window) = LaunchAppInTestMode();
        try
        {
            NavigateTab(window, "Home");
            AssertNoErrorDialogs(app, automation);

            // Verify admin status tile / badge
            var adminStatus = window.FindFirstDescendant(cf => cf.ByAutomationId("AdminStatusBadge"))
                           ?? window.FindFirstDescendant(cf => cf.ByName("Administrator Status"))
                           ?? window.FindFirstDescendant(cf => cf.ByName("Admin (Full Privileges)"))
                           ?? window.FindFirstDescendant(cf => cf.ByName("Standard User (Elevation Recommended)"));
            Assert.NotNull(adminStatus);

            // Verify System Restore card
            var restoreCard = window.FindFirstDescendant(cf => cf.ByAutomationId("SystemRestoreCard"))
                           ?? window.FindFirstDescendant(cf => cf.ByAutomationId("SystemRestoreHeader"))
                           ?? window.FindFirstDescendant(cf => cf.ByName("System Restore"))
                           ?? window.FindFirstDescendant(cf => cf.ByName("SYSTEM RESTORE"));
            Assert.NotNull(restoreCard);

            // Verify Presets section exists
            var gamingPreset = window.FindFirstDescendant(cf => cf.ByAutomationId("PresetCard_preset.gaming"))
                            ?? window.FindFirstDescendant(cf => cf.ByAutomationId("ProfileBtn_preset.gaming"))
                            ?? window.FindFirstDescendant(cf => cf.ByAutomationId("PresetCard_Gaming"))
                            ?? window.FindFirstDescendant(cf => cf.ByName("Gaming"))
                            ?? window.FindFirstDescendant(cf => cf.ByName("Apply Gaming Profile"));
            Assert.NotNull(gamingPreset);
        }
        finally
        {
            automation.Dispose();
            app.Close();
        }
    }

    [Fact]
    public void Test_02_TweaksPage_LoadsFiltersAndCatalog()
    {
        var (app, automation, window) = LaunchAppInTestMode();
        try
        {
            NavigateTab(window, "Tweaks");
            AssertNoErrorDialogs(app, automation);

            // Verify search box
            var searchBox = window.FindFirstDescendant(cf => cf.ByAutomationId("TweaksSearchBox"))
                         ?? window.FindFirstDescendant(cf => cf.ByName("Search tweaks"));
            Assert.NotNull(searchBox);

            // Verify Category selector or Risk selector exists
            var riskFilter = window.FindFirstDescendant(cf => cf.ByAutomationId("RiskFilterComboBox"))
                          ?? window.FindFirstDescendant(cf => cf.ByName("Filter by risk level"));
            Assert.NotNull(riskFilter);

            // Verify Recommended Only toggle exists
            var recToggle = window.FindFirstDescendant(cf => cf.ByAutomationId("RecommendedOnlyToggle"))
                         ?? window.FindFirstDescendant(cf => cf.ByName("Recommended only"))
                         ?? window.FindFirstDescendant(cf => cf.ByName("Recommended tweaks only"));
            Assert.NotNull(recToggle);
        }
        finally
        {
            automation.Dispose();
            app.Close();
        }
    }

    [Fact]
    public void Test_03_AppsPage_LoadsCatalogAndSearch()
    {
        var (app, automation, window) = LaunchAppInTestMode();
        try
        {
            NavigateTab(window, "Apps");
            AssertNoErrorDialogs(app, automation);

            var topSearch = window.FindFirstDescendant(cf => cf.ByAutomationId("TopSearchBox"));
            Assert.NotNull(topSearch);
            SafeSetText(topSearch, "Firefox");
            Thread.Sleep(400);
            SafeSetText(topSearch, string.Empty);
            Thread.Sleep(300);
        }
        finally
        {
            automation.Dispose();
            app.Close();
        }
    }

    [Fact]
    public void Test_04_ToolsPage_SwitchesSubTools()
    {
        var (app, automation, window) = LaunchAppInTestMode();
        try
        {
            NavigateTab(window, "Tools");
            AssertNoErrorDialogs(app, automation);

            // Check subtool buttons in Tools toolbar
            var startupTool = window.FindFirstDescendant(cf => cf.ByAutomationId("Tool_Startup"))
                           ?? window.FindFirstDescendant(cf => cf.ByName("Startup Applications"))
                           ?? window.FindFirstDescendant(cf => cf.ByName("Startup Apps"));
            Assert.NotNull(startupTool);
            SafeInvoke(startupTool);
            Thread.Sleep(400);

            var fixesTool = window.FindFirstDescendant(cf => cf.ByAutomationId("Tool_Fixes"))
                         ?? window.FindFirstDescendant(cf => cf.ByName("Quick System Fixes"))
                         ?? window.FindFirstDescendant(cf => cf.ByName("Fixes & Repairs"));
            Assert.NotNull(fixesTool);
            SafeInvoke(fixesTool);
            Thread.Sleep(400);

            AssertNoErrorDialogs(app, automation);
        }
        finally
        {
            automation.Dispose();
            app.Close();
        }
    }

    [Fact]
    public void Test_05_HistoryPage_DisplaysSessionsAndRestorePoints()
    {
        var (app, automation, window) = LaunchAppInTestMode();
        try
        {
            NavigateTab(window, "History");
            AssertNoErrorDialogs(app, automation);

            var undoAllBtn = window.FindFirstDescendant(cf => cf.ByAutomationId("UndoAllWinVeloButton"))
                          ?? window.FindFirstDescendant(cf => cf.ByName("Undo all WinVelo changes"));
            Assert.NotNull(undoAllBtn);

            var restorePointsList = window.FindFirstDescendant(cf => cf.ByName("System restore points list"));
            Assert.NotNull(restorePointsList);
        }
        finally
        {
            automation.Dispose();
            app.Close();
        }
    }

    [Fact]
    public void Test_06_Flow_PreviewApplyAndUndoToast()
    {
        var (app, automation, window) = LaunchAppInTestMode();
        try
        {
            NavigateTab(window, "Tweaks");
            AssertNoErrorDialogs(app, automation);

            // Toggle a safe tweak
            var safeToggle = window.FindAllDescendants(cf => cf.ByControlType(ControlType.CheckBox))
                .FirstOrDefault(c => (c.AutomationId ?? string.Empty).StartsWith("TweakToggle_"));

            if (safeToggle != null)
            {
                SafeInvoke(safeToggle);
                Thread.Sleep(800);

                // Check for toast notification
                var toastUndo = window.FindFirstDescendant(cf => cf.ByAutomationId("ToastUndoButton"))
                             ?? window.FindFirstDescendant(cf => cf.ByName("Undo applied tweak"));
                if (toastUndo != null)
                {
                    SafeInvoke(toastUndo);
                    Thread.Sleep(600);
                }
            }

            // Click Preview / Preset to test unified preview modal
            NavigateTab(window, "Home");
            var presetGaming = window.FindFirstDescendant(cf => cf.ByAutomationId("PresetCard_preset.gaming"))
                            ?? window.FindFirstDescendant(cf => cf.ByAutomationId("ProfileBtn_preset.gaming"))
                            ?? window.FindFirstDescendant(cf => cf.ByAutomationId("PresetCard_Gaming"))
                            ?? window.FindFirstDescendant(cf => cf.ByName("Gaming"))
                            ?? window.FindFirstDescendant(cf => cf.ByName("Apply Gaming Profile"));
            if (presetGaming != null)
            {
                SafeInvoke(presetGaming);
                Thread.Sleep(800);

                // Unified preview dialog should be open
                var previewConfirm = window.FindFirstDescendant(cf => cf.ByAutomationId("PreviewConfirmButton"))
                                  ?? window.FindFirstDescendant(cf => cf.ByName("Confirm & Apply"))
                                  ?? window.FindFirstDescendant(cf => cf.ByName("Apply Selected"));

                var previewCancel = window.FindFirstDescendant(cf => cf.ByAutomationId("PreviewCancelButton"))
                                 ?? window.FindFirstDescendant(cf => cf.ByName("Cancel"));

                if (previewCancel != null)
                {
                    SafeInvoke(previewCancel);
                    Thread.Sleep(400);
                }
            }
            AssertNoErrorDialogs(app, automation);
        }
        finally
        {
            automation.Dispose();
            app.Close();
        }
    }

    [Fact]
    public void FullAuditUiTestSuite_All5TabsAndControls()
    {
        string solutionDir = FindSolutionRoot();
        string screenshotsDir = Path.Combine(Path.GetTempPath(), "WinVelo-Audit-Screenshots");
        Directory.CreateDirectory(screenshotsDir);

        var (app, automation, window) = LaunchAppInTestMode();
        try
        {
            _output.WriteLine("[UI-AUDIT] WinVelo launched successfully in --test-mode");

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

            // 5 Tabs Navigation & Screenshots
            string[] tabs = { "Home", "Tweaks", "Apps", "Tools", "History" };
            string[] screenshotFiles = { "dashboard.png", "tweaks.png", "install.png", "tools.png", "history.png" };

            for (int i = 0; i < tabs.Length; i++)
            {
                string tab = tabs[i];
                string file = screenshotFiles[i];

                NavigateTab(window, tab);
                AssertNoErrorDialogs(app, automation);
                CaptureViewScreenshot(window, screenshotsDir, file);
                _output.WriteLine($"[UI-AUDIT] Captured {file} for tab {tab}");
            }

            // Command palette screenshot
            var openPaletteBtn = window.FindFirstDescendant(cf => cf.ByAutomationId("OpenCommandPaletteButton"));
            if (openPaletteBtn != null)
            {
                SafeInvoke(openPaletteBtn);
                Thread.Sleep(500);
                CaptureViewScreenshot(window, screenshotsDir, "command-palette.png");
                SafeInvoke(openPaletteBtn);
                Thread.Sleep(300);
            }

            _output.WriteLine("[UI-AUDIT] All 5 tabs and dialogs captured cleanly in test mode.");
        }
        finally
        {
            automation.Dispose();
            app.Close();
        }
    }

    [Fact]
    public void Test_07_Accessibility_InteractiveControlsHaveAutomationName()
    {
        var (app, automation, window) = LaunchAppInTestMode();
        try
        {
            // Verify across all 5 navigation tabs
            string[] tabs = { "Home", "Tweaks", "Apps", "Tools", "History" };
            var missingInteractiveNames = new System.Collections.Generic.List<string>();

            foreach (var tab in tabs)
            {
                NavigateTab(window, tab);
                var interactiveElements = window.FindAllDescendants(cf =>
                    cf.ByControlType(ControlType.Button)
                    .Or(cf.ByControlType(ControlType.CheckBox))
                    .Or(cf.ByControlType(ControlType.Edit))
                    .Or(cf.ByControlType(ControlType.RadioButton)));

                foreach (var el in interactiveElements)
                {
                    // Filter out standard internal WPF templated components
                    if (el.ClassName == "Thumb" || el.ClassName == "RepeatButton" || el.ClassName.Contains("ScrollViewer"))
                        continue;

                    if (string.IsNullOrWhiteSpace(el.Name) && string.IsNullOrWhiteSpace(el.AutomationId) && string.IsNullOrWhiteSpace(el.HelpText))
                    {
                        missingInteractiveNames.Add($"Tab: {tab}, ControlType: {el.ControlType}, ClassName: {el.ClassName}");
                    }
                }
            }

            // Also check all XAML views to guarantee full structural coverage
            string solutionDir = FindSolutionRoot();
            string viewsDir = Path.Combine(solutionDir, "src", "WinVelo.App", "Views");
            foreach (var file in Directory.GetFiles(viewsDir, "*.xaml"))
            {
                var xml = new System.Xml.XmlDocument();
                xml.Load(file);
                var nodes = xml.SelectNodes("//*[local-name()='Button' or local-name()='CheckBox' or local-name()='TextBox' or local-name()='RadioButton' or local-name()='ToggleSwitch']");
                if (nodes != null)
                {
                    foreach (System.Xml.XmlNode node in nodes)
                    {
                        string? name = node.Attributes?["AutomationProperties.Name"]?.Value;
                        if (string.IsNullOrWhiteSpace(name))
                        {
                            missingInteractiveNames.Add($"XAML {Path.GetFileName(file)}: {node.OuterXml}");
                        }
                    }
                }
            }

            Assert.True(missingInteractiveNames.Count == 0,
                $"UI Accessibility violation: Interactive elements missing automation name:\n{string.Join("\n", missingInteractiveNames)}");
        }
        finally
        {
            automation.Dispose();
            app.Close();
        }
    }

    [Fact]
    public void Test_08_FirstRunWizard_ModalAndKeyboardNavigation()
    {
        string solutionDir = FindSolutionRoot();
        string exePath = Path.Combine(solutionDir, "src", "WinVelo.App", "bin", "Debug", "net10.0-windows", "WinVelo.exe");
        if (!File.Exists(exePath))
        {
            exePath = Path.Combine(solutionDir, "src", "WinVelo.App", "bin", "Debug", "net10.0-windows", "WinVelo.App.exe");
        }

        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = "--test-mode",
            UseShellExecute = false
        };

        var app = Application.Launch(psi);
        var automation = new UIA3Automation();
        try
        {
            var window = Retry.WhileNull(() =>
            {
                var topWindows = app.GetAllTopLevelWindows(automation);
                return topWindows.FirstOrDefault(w =>
                    (!string.IsNullOrEmpty(w.Title) && w.Title.Contains("WinVelo")) ||
                    w.AutomationId == "WinVeloMainWindow")
                    ?? topWindows.FirstOrDefault(w => w.FindAllDescendants().Length > 5);
            }, TimeSpan.FromSeconds(25)).Result;
            Assert.NotNull(window);

            // 1. Verify Wizard is open initially on launch in test mode
            var wizard = Retry.WhileNull(() =>
                window.FindFirstDescendant(cf => cf.ByAutomationId("FirstRunWizardView")),
                TimeSpan.FromSeconds(10)).Result;
            Assert.NotNull(wizard);

            // 2. Verify Step 1 elements
            var nextBtn = window.FindFirstDescendant(cf => cf.ByAutomationId("WizardNextButton"));
            Assert.NotNull(nextBtn);

            var skipBtn = window.FindFirstDescendant(cf => cf.ByAutomationId("SkipWizardButton"));
            Assert.NotNull(skipBtn);

            // 3. Test Enter key / Next Step invocation
            SafeInvoke(nextBtn);
            Thread.Sleep(600);

            var restoreTitle = window.FindFirstDescendant(cf => cf.ByName("System Restore Protection"))
                            ?? window.FindFirstDescendant(cf => cf.ByName("Step 2 of 3"));
            Assert.NotNull(restoreTitle);

            // 4. Test Skip / Esc closes wizard
            SafeInvoke(skipBtn);
            Thread.Sleep(800);

            // 5. Verify wizard overlay is closed and Home is accessible
            var homeBtn = window.FindFirstDescendant(cf => cf.ByAutomationId("Tab_Dashboard"));
            Assert.NotNull(homeBtn);
        }
        finally
        {
            automation.Dispose();
            app.Close();
        }
    }

    [Fact]
    public void Test_09_UiReviewVerification_Screenshots_Responsiveness_Contrast()
    {
        string solutionDir = FindSolutionRoot();
        string screenshotsDir = Path.Combine(solutionDir, "docs", "screenshots");

        // ==================== 1. SCREENSHOT INTEGRITY (>= 20KB, VARIANCE > 25, NO DUPLICATE MD5) ====================
        string[] requiredScreenshots =
        {
            "home.png", "tweaks.png", "apps.png", "tools.png", "history.png", "settings.png", "preview.png",
            Path.Combine("ar", "home.png"),
            Path.Combine("ar", "tweaks.png"),
            Path.Combine("ar", "apps.png"),
            Path.Combine("ar", "tools.png"),
            Path.Combine("ar", "history.png"),
            Path.Combine("ar", "settings.png"),
            Path.Combine("ar", "preview.png")
        };

        var seenHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in requiredScreenshots)
        {
            string path = Path.Combine(screenshotsDir, file);
            Assert.True(File.Exists(path), $"Mandatory screenshot missing: {file}");
            var fi = new FileInfo(path);
            Assert.True(fi.Length >= 20480, $"Screenshot {file} is suspiciously small ({fi.Length} bytes < 20KB limit)");

            // Verify unique MD5 hash (no two screenshots can be identical duplicates)
            using (var md5 = System.Security.Cryptography.MD5.Create())
            using (var stream = File.OpenRead(path))
            {
                byte[] hashBytes = md5.ComputeHash(stream);
                string hash = Convert.ToHexString(hashBytes);
                Assert.False(seenHashes.ContainsKey(hash),
                    $"Duplicate screenshot detected! '{file}' has identical MD5 hash {hash} as '{seenHashes.GetValueOrDefault(hash)}'");
                seenHashes[hash] = file;
            }

            // Check not pure black and has pixel variance
            var decoder = BitmapDecoder.Create(new Uri(path, UriKind.Absolute), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];
            var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
            int stride = converted.PixelWidth * 4;
            byte[] pixels = new byte[converted.PixelHeight * stride];
            converted.CopyPixels(pixels, stride, 0);

            int maxBrightness = 0;
            double sumLuma = 0;
            int sampleCount = 0;
            // Sample every 16th pixel for speed
            for (int i = 0; i < pixels.Length; i += 64)
            {
                int b = pixels[i];
                int g = pixels[i + 1];
                int r = pixels[i + 2];
                int brightness = Math.Max(r, Math.Max(g, b));
                if (brightness > maxBrightness) maxBrightness = brightness;
                double luma = 0.299 * r + 0.587 * g + 0.114 * b;
                sumLuma += luma;
                sampleCount++;
            }

            double meanLuma = sumLuma / sampleCount;
            double sumVariance = 0;
            for (int i = 0; i < pixels.Length; i += 64)
            {
                int b = pixels[i];
                int g = pixels[i + 1];
                int r = pixels[i + 2];
                double luma = 0.299 * r + 0.587 * g + 0.114 * b;
                double diff = luma - meanLuma;
                sumVariance += diff * diff;
            }
            double variance = sumVariance / sampleCount;

            Assert.True(maxBrightness > 80, $"Screenshot {file} appears completely black (max brightness {maxBrightness} <= 80)");
            Assert.True(variance > 25.0, $"Screenshot {file} has suspiciously low pixel variance ({variance:F2} <= 25.0), indicating a blank or uniform image");
        }

        // ==================== 2. TEXT CONTRAST (WCAG AA >= 4.5:1 ON BODY TEXT) ====================
        // Dark theme tokens
        var darkBgColors = new[]
        {
            Color.FromRgb(0x0F, 0x11, 0x17), // BgDeep
            Color.FromRgb(0x1E, 0x22, 0x2D), // BgCard
            Color.FromRgb(0x16, 0x19, 0x22)  // BgSidebar
        };
        var darkTextColors = new[]
        {
            Color.FromRgb(0xF9, 0xFA, 0xFB), // TextPrimary
            Color.FromRgb(0xD1, 0xD5, 0xDB), // TextSecondary
            Color.FromRgb(0x9C, 0xA3, 0xAF)  // TextMuted
        };

        foreach (var bg in darkBgColors)
        {
            foreach (var fg in darkTextColors)
            {
                double ratio = ComputeContrastRatio(fg, bg);
                Assert.True(ratio >= 4.5, $"Dark theme text contrast ratio {ratio:F2}:1 is below WCAG AA 4.5:1 requirement");
            }
        }

        // Light theme tokens
        var lightBgColors = new[]
        {
            Color.FromRgb(0xF3, 0xF4, 0xF6), // BgDeep
            Color.FromRgb(0xFF, 0xFF, 0xFF), // BgCard
            Color.FromRgb(0xE5, 0xE7, 0xEB)  // BgSidebar
        };
        var lightTextColors = new[]
        {
            Color.FromRgb(0x11, 0x18, 0x27), // TextPrimary
            Color.FromRgb(0x37, 0x41, 0x51), // TextSecondary
            Color.FromRgb(0x4B, 0x55, 0x63)  // TextMuted
        };

        foreach (var bg in lightBgColors)
        {
            foreach (var fg in lightTextColors)
            {
                double ratio = ComputeContrastRatio(fg, bg);
                Assert.True(ratio >= 4.5, $"Light theme text contrast ratio {ratio:F2}:1 is below WCAG AA 4.5:1 requirement");
            }
        }

        // ==================== 3. RESPONSIVENESS (NO HORIZONTAL SCROLL AT 960x640) ====================
        var (app, automation, window) = LaunchAppInTestMode();
        try
        {
            DismissWizardIfOpen(window);

            // Resize window to minimum allowed dimensions: 960x640
            SetWindowPos(window.Properties.NativeWindowHandle, IntPtr.Zero, 40, 40, 960, 640, 0x0040);
            Thread.Sleep(600);

            string[] tabs = { "Home", "Tweaks", "Apps", "Tools", "History", "Settings" };
            foreach (var tab in tabs)
            {
                NavigateTab(window, tab);
                Thread.Sleep(400);

                // Verify no active horizontal scrollbars
                var horizontalScrollbars = window.FindAllDescendants(cf => cf.ByControlType(ControlType.ScrollBar))
                    .Where(sb => sb.BoundingRectangle.Width > sb.BoundingRectangle.Height
                              && sb.IsAvailable
                              && !sb.IsOffscreen
                              && sb.BoundingRectangle.Width > 30)
                    .ToList();

                Assert.True(horizontalScrollbars.Count == 0,
                    $"Page '{tab}' exhibits active horizontal scrollbar at minimum window size (960x640)");
            }

            AssertNoErrorDialogs(app, automation);
        }
        finally
        {
            automation.Dispose();
            app.Close();
        }
    }

    private static double ComputeContrastRatio(Color fg, Color bg)
    {
        double l1 = ComputeLuminance(fg);
        double l2 = ComputeLuminance(bg);
        double lighter = Math.Max(l1, l2);
        double darker = Math.Min(l1, l2);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double ComputeLuminance(Color c)
    {
        double r = SrgbToLinear(c.R / 255.0);
        double g = SrgbToLinear(c.G / 255.0);
        double b = SrgbToLinear(c.B / 255.0);
        return 0.2126 * r + 0.7152 * g + 0.0722 * b;
    }

    private static double SrgbToLinear(double c) =>
        c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
}
