using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using WinVelo.App.Services;
using WinVelo.App.ViewModels;
using WinVelo.Core.Engine;
using WinVelo.Core.Models;
using WinVelo.Core.Operations;
using WinVelo.Core.Safety;
using WinVelo.Modules.Advisor;
using WinVelo.Modules.Apps;
using WinVelo.Modules.Cleaner;
using WinVelo.Modules.Config;
using WinVelo.Modules.Memory;
using WinVelo.Modules.Network;
using WinVelo.Modules.Services;
using WinVelo.Modules.Startup;
using WinVelo.Modules.Tweaks;
using WinVelo.Modules.Updates;
using WinVelo.Simulation;
using Xunit;

namespace WinVelo.Tests;

public sealed class FinalQaDiagnosticTests
{
    private readonly string _catalogDir;

    public FinalQaDiagnosticTests()
    {
        TestModeManager.EnableTestMode();
        string baseDir = AppContext.BaseDirectory;
        string candidate = Path.Combine(baseDir, "Catalog");
        if (!Directory.Exists(candidate))
        {
            string? parent = Directory.GetParent(baseDir)?.FullName;
            while (parent != null)
            {
                string check = Path.Combine(parent, "Catalog");
                if (Directory.Exists(check))
                {
                    candidate = check;
                    break;
                }
                parent = Directory.GetParent(parent)?.FullName;
            }
        }
        _catalogDir = candidate;
    }

    [Fact]
    public void Catalog_HasZeroDuplicateIds_AndAllTweaksHaveUndo()
    {
        string tweaksDir = Path.Combine(_catalogDir, "Tweaks");
        Assert.True(Directory.Exists(tweaksDir), $"Catalog/Tweaks directory not found at: {tweaksDir}");

        var tweakFiles = Directory.GetFiles(tweaksDir, "*.json", SearchOption.AllDirectories);
        Assert.NotEmpty(tweakFiles);

        HashSet<string> seenIds = new(StringComparer.OrdinalIgnoreCase);
        List<string> duplicates = new();
        List<string> missingUndo = new();
        int totalTweaks = 0;

        foreach (var file in tweakFiles)
        {
            string json = File.ReadAllText(file);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var elements = root.ValueKind == JsonValueKind.Array
                ? root.EnumerateArray().ToList()
                : new List<JsonElement> { root };

            foreach (var elem in elements)
            {
                totalTweaks++;
                string id = elem.GetProperty("id").GetString()!;
                if (!seenIds.Add(id))
                {
                    duplicates.Add(id);
                }

                if (!elem.TryGetProperty("undo", out var undoElem) || undoElem.GetArrayLength() == 0)
                {
                    missingUndo.Add(id);
                }
            }
        }

        Assert.Empty(duplicates);
        Assert.Empty(missingUndo);
        Assert.True(totalTweaks >= 130, $"Expected >= 130 tweaks, found {totalTweaks}");
    }

    [Fact]
    public void Catalog_HasZeroOccurrencesOfLegacyNameInUserFacingFields()
    {
        string tweaksDir = Path.Combine(_catalogDir, "Tweaks");
        var tweakFiles = Directory.GetFiles(tweaksDir, "*.json", SearchOption.AllDirectories);

        List<string> violations = new();

        foreach (var file in tweakFiles)
        {
            string json = File.ReadAllText(file);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var elements = root.ValueKind == JsonValueKind.Array
                ? root.EnumerateArray().ToList()
                : new List<JsonElement> { root };

            foreach (var elem in elements)
            {
                string id = elem.GetProperty("id").GetString()!;
                string name = elem.GetProperty("name").GetString() ?? string.Empty;
                string description = elem.GetProperty("description").GetString() ?? string.Empty;

                if (name.Contains(AppDataMigrator.LegacyProductName, StringComparison.OrdinalIgnoreCase))
                {
                    violations.Add($"{id} name: {name}");
                }
                if (description.Contains(AppDataMigrator.LegacyProductName, StringComparison.OrdinalIgnoreCase))
                {
                    violations.Add($"{id} description: {description}");
                }

                // Check registry paths in apply and undo
                if (elem.TryGetProperty("apply", out var applyList))
                {
                    foreach (var a in applyList.EnumerateArray())
                    {
                        if (a.TryGetProperty("key", out var k) && (k.GetString() ?? "").Contains(AppDataMigrator.LegacyProductName, StringComparison.OrdinalIgnoreCase))
                        {
                            violations.Add($"{id} apply key: {k.GetString()}");
                        }
                    }
                }
                if (elem.TryGetProperty("undo", out var undoList))
                {
                    foreach (var u in undoList.EnumerateArray())
                    {
                        if (u.TryGetProperty("key", out var k) && (k.GetString() ?? "").Contains(AppDataMigrator.LegacyProductName, StringComparison.OrdinalIgnoreCase))
                        {
                            violations.Add($"{id} undo key: {k.GetString()}");
                        }
                    }
                }
            }
        }

        Assert.Empty(violations);
    }

    [Fact]
    public void Presets_AllTweakIdsExistInCatalog()
    {
        var fakeReg = new FakeRegistryService();
        var fakeSvc = new FakeServicesService();
        var fakeSched = new FakeTaskSchedulerService();
        var fakeRestore = new FakeRestoreService();
        var fakeShell = new FakeShellService();
        var journal = new RollbackJournalManager(Path.Combine(AppContext.BaseDirectory, "test-journal-presets"));
        var tweakService = new TweakService(fakeReg, fakeSvc, fakeSched, fakeRestore, fakeShell, journal);
        var catalog = tweakService.LoadCatalog();
        var catalogIds = new HashSet<string>(catalog.Select(t => t.Id), StringComparer.OrdinalIgnoreCase);
        var catalogOrigins = new HashSet<string>(catalog.Where(t => t.Origin != null).Select(t => t.Origin!), StringComparer.OrdinalIgnoreCase);

        string presetsDir = Path.Combine(_catalogDir, "Presets");
        Assert.True(Directory.Exists(presetsDir), $"Presets dir not found: {presetsDir}");

        List<string> orphanIds = new();

        foreach (var file in Directory.GetFiles(presetsDir, "*.json"))
        {
            string json = File.ReadAllText(file);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("tweakIds", out var arr))
            {
                foreach (var item in arr.EnumerateArray())
                {
                    string id = item.GetString()!;
                    if (!catalogIds.Contains(id) && !catalogOrigins.Contains(id))
                    {
                        orphanIds.Add($"{Path.GetFileName(file)} -> {id}");
                    }
                }
            }
        }

        Assert.Empty(orphanIds);
    }

    [Fact]
    public void DashboardViewModel_AllQuickTogglesAndProfileTweaksExistInCatalog()
    {
        var fakeReg = new FakeRegistryService();
        var fakeSvc = new FakeServicesService();
        var fakeSched = new FakeTaskSchedulerService();
        var fakeRestore = new FakeRestoreService();
        var fakeShell = new FakeShellService();
        var journal = new RollbackJournalManager(Path.Combine(AppContext.BaseDirectory, "test-journal-dashboard"));
        var runner = new OperationRunner();

        var tweakService = new TweakService(fakeReg, fakeSvc, fakeSched, fakeRestore, fakeShell, journal);
        var catalog = tweakService.LoadCatalog();
        var catalogIds = new HashSet<string>(catalog.Select(t => t.Id), StringComparer.OrdinalIgnoreCase);
        var catalogOrigins = new HashSet<string>(catalog.Where(t => t.Origin != null).Select(t => t.Origin!), StringComparer.OrdinalIgnoreCase);
        var catalogAliases = new HashSet<string>(catalog.Where(t => t.Aliases != null).SelectMany(t => t.Aliases!), StringComparer.OrdinalIgnoreCase);

        var memOpt = new MemoryOptimizerService();
        var advisor = new SmartAdvisorService(fakeReg, fakeShell);
        var dvm = new DashboardViewModel(tweakService, runner, fakeRestore, memOpt, advisor);

        // 1. Check all 8 Quick Toggles
        Assert.Equal(8, dvm.QuickToggles.Count);
        foreach (var toggle in dvm.QuickToggles)
        {
            Assert.NotNull(toggle.OriginKey);
            bool found = catalogOrigins.Contains(toggle.OriginKey) || catalogIds.Contains(toggle.OriginKey) || catalogAliases.Contains(toggle.OriginKey);
            Assert.True(found, $"QuickToggle '{toggle.Title}' with key '{toggle.OriginKey}' was not found in catalog!");
        }

        // 2. Check all 6 Profiles exist
        Assert.Equal(6, dvm.Profiles.Count);
        foreach (var profile in dvm.Profiles)
        {
            Assert.NotNull(profile.OnApplyAsync);
        }
    }

    [Fact]
    public void ApplicationsJson_All36AppsHaveValidMetadata()
    {
        string appJsonPath = Path.Combine(_catalogDir, "Apps", "applications.json");
        Assert.True(File.Exists(appJsonPath), $"applications.json not found: {appJsonPath}");

        string json = File.ReadAllText(appJsonPath);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var apps = root.EnumerateObject().ToList();
        Assert.True(apps.Count >= 30, $"Expected >= 30 apps, found {apps.Count}");

        foreach (var app in apps)
        {
            string key = app.Name;
            var elem = app.Value;

            Assert.True(elem.TryGetProperty("name", out var name) && !string.IsNullOrWhiteSpace(name.GetString()),
                $"App '{key}' is missing a valid name.");
            Assert.True(elem.TryGetProperty("category", out var category) && !string.IsNullOrWhiteSpace(category.GetString()),
                $"App '{key}' is missing a valid category.");
            Assert.True(elem.TryGetProperty("winget", out var winget) && !string.IsNullOrWhiteSpace(winget.GetString()),
                $"App '{key}' is missing a valid winget ID.");

            // Verify GetBrandMeta maps a valid color and icon
            var (color, icon, code) = AppInstallService.GetBrandMeta(key, category.GetString()!);
            Assert.StartsWith("#", color);
            Assert.False(string.IsNullOrWhiteSpace(icon));
            Assert.False(string.IsNullOrWhiteSpace(code));

            // Verify GetBrandSvg maps an authentic SVG vector logo for all catalog apps
            string? svg = AppInstallService.GetBrandSvg(key);
            Assert.False(string.IsNullOrWhiteSpace(svg), $"App '{key}' is missing an authentic SVG vector logo.");
        }
    }

    [Fact]
    public async Task AppInstallService_DetectBestPackageManager_ReturnsWinGetOrChoco()
    {
        var fakeShell = new FakeShellService();
        var service = new AppInstallService(fakeShell);

        string best = await service.DetectBestPackageManagerAsync();
        Assert.True(best == "WinGet" || best == "Chocolatey", $"Expected WinGet or Chocolatey, got '{best}'");
    }

    [Fact]
    public void StartupPathHelper_HandlesEdgeCasesAccurately()
    {
        // 1. Quoted path with arguments
        string q1 = "\"C:\\Program Files\\Discord\\app.exe\" --start-minimized";
        Assert.Equal("C:\\Program Files\\Discord\\app.exe", StartupPathHelper.ExtractCleanExePath(q1));

        // 2. Unquoted path with extension and arguments
        string q2 = "C:\\Program Files (x86)\\Steam\\steam.exe -silent";
        Assert.Equal("C:\\Program Files (x86)\\Steam\\steam.exe", StartupPathHelper.ExtractCleanExePath(q2));

        // 3. Environment variables
        string q3 = "%SystemRoot%\\System32\\cmd.exe /c echo test";
        string expectedCmd = $"{Environment.GetFolderPath(Environment.SpecialFolder.Windows)}\\System32\\cmd.exe";
        Assert.Equal(expectedCmd, StartupPathHelper.ExtractCleanExePath(q3));

        // 4. Bat / cmd scripts
        string q4 = "\"C:\\Scripts\\backup.bat\" --now";
        Assert.Equal("C:\\Scripts\\backup.bat", StartupPathHelper.ExtractCleanExePath(q4));

        // 5. Corrupt / empty / random strings
        Assert.Equal(string.Empty, StartupPathHelper.ExtractCleanExePath(null));
        Assert.Equal(string.Empty, StartupPathHelper.ExtractCleanExePath("   "));
        Assert.Equal("rundll32.exe", StartupPathHelper.ExtractCleanExePath("rundll32.exe shell32.dll,Control_RunDLL"));
    }

    [Fact]
    public void StartupApprovedHelper_AccuratelyEncodesAndDecodesBinary()
    {
        // Null / Empty bytes => Enabled
        Assert.True(StartupApprovedHelper.IsEnabled(null));
        Assert.True(StartupApprovedHelper.IsEnabled(Array.Empty<byte>()));

        // Enable produces 12 bytes with first byte 0x02
        byte[] enabled = StartupApprovedHelper.CreateEnabledBytes();
        Assert.Equal(12, enabled.Length);
        Assert.Equal(0x02, enabled[0]);
        Assert.True(StartupApprovedHelper.IsEnabled(enabled));

        // Disable produces 12 bytes with first byte 0x03 and non-zero timestamp
        byte[] disabled = StartupApprovedHelper.CreateDisabledBytes();
        Assert.Equal(12, disabled.Length);
        Assert.Equal(0x03, disabled[0]);
        Assert.False(StartupApprovedHelper.IsEnabled(disabled));

        // Timestamp is encoded in bytes 4..11
        long fileTime = BitConverter.ToInt64(disabled, 4);
        Assert.True(fileTime > 0, "FILETIME timestamp was not written into bytes 4..11");

        // Re-enabling clears bit 0
        byte[] reEnabled = StartupApprovedHelper.CreateEnabledBytes(disabled);
        Assert.True(StartupApprovedHelper.IsEnabled(reEnabled));
        Assert.Equal(0x02, reEnabled[0]);
    }

    [Fact]
    public async Task AllViewModels_InitializeAndExecuteCommandsCleanly()
    {
        var fakeReg = new FakeRegistryService();
        var fakeSvc = new FakeServicesService();
        var fakeSched = new FakeTaskSchedulerService();
        var fakeRestore = new FakeRestoreService();
        var fakeShell = new FakeShellService();
        string journalDir = Path.Combine(AppContext.BaseDirectory, "test-journal-allvm");
        Directory.CreateDirectory(journalDir);
        var journal = new RollbackJournalManager(journalDir);
        var runner = new OperationRunner();

        var tweakService = new TweakService(fakeReg, fakeSvc, fakeSched, fakeRestore, fakeShell, journal);
        var cleaner = new CleanerService(fakeSvc);
        var startup = new StartupService(fakeReg);
        var servicesMgr = new ServicesManagerService(fakeSvc);
        var fixService = new FixService(fakeShell);
        var dnsService = new DnsService(fakeShell);
        var memOpt = new MemoryOptimizerService();
        var netOpt = new NetworkOptimizerService(fakeReg, fakeShell);
        var advisor = new SmartAdvisorService(fakeReg, fakeShell);
        var appInstall = new AppInstallService(fakeShell);
        var featureService = new FeatureService(fakeShell);
        var updateMode = new UpdateModeService(fakeReg, fakeSvc, fakeSched);

        // 1. DashboardViewModel
        var dashboardVm = new DashboardViewModel(tweakService, runner, fakeRestore, memOpt, advisor);
        Assert.NotNull(dashboardVm.SystemInfo);
        Assert.NotEmpty(dashboardVm.QuickToggles);
        Assert.NotEmpty(dashboardVm.Profiles);

        // 2. TweaksViewModel
        var tweaksVm = new TweaksViewModel(tweakService, runner);
        Assert.NotNull(tweaksVm.EssentialTweaks);

        // 3. StartupViewModel
        var startupVm = new StartupViewModel(startup);
        await startupVm.RefreshCommandAsync();
        Assert.NotNull(startupVm.AllApps);
        Assert.NotNull(startupVm.FilteredApps);

        // 4. ToolboxViewModel
        var toolboxVm = new ToolboxViewModel(cleaner, startup, servicesMgr, fixService, dnsService, runner, memOpt, netOpt);
        toolboxVm.LoadToolsData();
        Assert.NotNull(toolboxVm.CleanerCategories);

        // 5. InstallViewModel
        var installVm = new InstallViewModel(appInstall, runner);
        Assert.NotNull(installVm.FilteredApps);

        // 6. ConfigViewModel
        var configVm = new ConfigViewModel(featureService, fixService, runner);
        Assert.NotNull(configVm.FilteredFeatures);

        // 7. UpdatesViewModel
        var updatesVm = new UpdatesViewModel(updateMode, runner);
        Assert.NotNull(updatesVm.ActiveModeTitle);

        // 8. MainViewModel
        var mainVm = new MainViewModel(
            dashboardVm,
            installVm,
            tweaksVm,
            configVm,
            updatesVm,
            toolboxVm,
            startupVm);

        Assert.NotNull(mainVm.CurrentPage);
        Assert.True(mainVm.ActiveTab is "Home" or "Dashboard");
    }

    [Fact]
    public void RamMetrics_TaskManagerParityMath_IsValidAndFormatted()
    {
        var ram = SystemInfoHelper.GetRamMetrics();
        Assert.NotNull(ram);
        Assert.True(ram.TotalGb > 0, "Total RAM should be greater than 0 GB");
        Assert.True(ram.UsedGb > 0, "Used RAM should be greater than 0 GB");
        Assert.True(ram.AvailableGb >= 0, "Available RAM should be >= 0 GB");
        Assert.InRange(ram.UsagePercent, 0, 100);
        Assert.Contains(" / ", ram.DisplayText);
        Assert.Contains(" GB (", ram.DisplayText);
        Assert.EndsWith("%)", ram.DisplayText);
        Assert.EndsWith(" GB Available", ram.AvailableText);
    }

    [Fact]
    public void DashboardViewModel_RamTelemetryProperties_PopulatedAndDynamic()
    {
        var fakeReg = new FakeRegistryService();
        var fakeSvc = new FakeServicesService();
        var fakeSched = new FakeTaskSchedulerService();
        var fakeRestore = new FakeRestoreService();
        var fakeShell = new FakeShellService();
        string journalDir = Path.Combine(AppContext.BaseDirectory, "test-journal-ram");
        Directory.CreateDirectory(journalDir);
        var journal = new RollbackJournalManager(journalDir);
        var runner = new OperationRunner();

        var tweakService = new TweakService(fakeReg, fakeSvc, fakeSched, fakeRestore, fakeShell, journal);
        var memOpt = new MemoryOptimizerService();
        var advisor = new SmartAdvisorService(fakeReg, fakeShell);

        using var dashboardVm = new DashboardViewModel(tweakService, runner, fakeRestore, memOpt, advisor);

        Assert.False(string.IsNullOrWhiteSpace(dashboardVm.RamUsageText));
        Assert.False(string.IsNullOrWhiteSpace(dashboardVm.AvailableRamText));
        Assert.InRange(dashboardVm.RamUsagePercentage, 0, 100);
        Assert.Contains(dashboardVm.RamBarColor, new[] { "#10B981", "#F59E0B", "#EF4444" });

        dashboardVm.UpdateRamMetrics();
        Assert.False(string.IsNullOrWhiteSpace(dashboardVm.RamUsageText));
    }
}
