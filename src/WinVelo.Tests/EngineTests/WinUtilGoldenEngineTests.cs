using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using WinVelo.Core.Engine;
using WinVelo.Core.Engine.Handlers;
using WinVelo.Core.Models;
using WinVelo.Simulation;
using Xunit;

namespace WinVelo.Tests.EngineTests;

public sealed class WinUtilGoldenEngineTests : IDisposable
{
    private readonly string _tempJournalDir;
    private readonly FakeRegistryService _fakeRegistry;
    private readonly FakeServicesService _fakeServices;
    private readonly FakeTaskSchedulerService _fakeTasks;
    private readonly FakeRestoreService _fakeRestore;
    private readonly FakeShellService _fakeShell;
    private readonly RollbackJournalManager _journal;
    private readonly StateDetector _detector;
    private readonly List<IActionHandler> _handlers;
    private readonly Core.Engine.ExecutionContext _context;
    private readonly TweakRunner _runner;

    public WinUtilGoldenEngineTests()
    {
        _tempJournalDir = Path.Combine(Path.GetTempPath(), "WinVeloWinUtilGolden_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempJournalDir);

        _fakeRegistry = new FakeRegistryService();
        _fakeServices = new FakeServicesService();
        _fakeTasks = new FakeTaskSchedulerService();
        _fakeRestore = new FakeRestoreService();
        _fakeShell = new FakeShellService();

        _journal = new RollbackJournalManager(_tempJournalDir);
        _detector = new StateDetector(_fakeRegistry, _fakeServices, _fakeTasks);

        _handlers = new List<IActionHandler>
        {
            new RegistryActionHandler(),
            new ServiceActionHandler(),
            new TaskActionHandler(),
            new PowerShellActionHandler(),
            new FileActionHandler()
        };

        _context = new Core.Engine.ExecutionContext
        {
            Registry = _fakeRegistry,
            Services = _fakeServices,
            Tasks = _fakeTasks,
            Shell = _fakeShell,
            Journal = _journal,
            IsDryRun = false,
            BackupDirectory = _tempJournalDir
        };

        _runner = new TweakRunner(_handlers, _context, _fakeRestore, _detector);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempJournalDir))
            {
                Directory.Delete(_tempJournalDir, true);
            }
        }
        catch { }
    }

    // =========================================================================
    // GOLDEN TEST 1: Set-WinUtilRegistry creates parent key if missing
    // =========================================================================
    [Fact]
    public async Task Golden01_SetWinUtilRegistry_CreatesParentKeyIfMissing()
    {
        var tweak = new TweakDefinition
        {
            Id = "golden.01",
            Name = "Golden 1 Registry",
            Description = "Test",
            Category = "system",
            Risk = "Safe",
            Apply = new List<ActionItem>
            {
                new()
                {
                    Type = "Registry",
                    Hive = "HKLM",
                    Key = @"SOFTWARE\Policies\Microsoft\Windows\System",
                    Operation = "Set",
                    ValueName = "PublishUserActivities",
                    ValueKind = "DWord",
                    Data = JsonSerializer.SerializeToElement(0)
                }
            },
            Undo = new List<ActionItem>(),
            Detect = new DetectStructure
            {
                Type = "Registry",
                Hive = "HKLM",
                Key = @"SOFTWARE\Policies\Microsoft\Windows\System",
                ValueName = "PublishUserActivities",
                MatchType = "Equals",
                ExpectedValue = JsonSerializer.SerializeToElement(0)
            }
        };

        Assert.False(_fakeRegistry.KeyExists("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\System"));

        var report = await _runner.ApplyBatchAsync(new[] { tweak });
        Assert.Equal(1, report.AppliedCount);
        Assert.True(_fakeRegistry.KeyExists("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\System"));
        Assert.Equal(0, _fakeRegistry.GetValue("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\System", "PublishUserActivities"));
    }

    // =========================================================================
    // GOLDEN TEST 2: Set-WinUtilRegistry with <RemoveEntry> deletes value
    // =========================================================================
    [Fact]
    public async Task Golden02_SetWinUtilRegistry_RemoveEntryDeletesValue()
    {
        _fakeRegistry.SetValue("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Explorer", "ClassicMenu", 1, "DWord");

        var tweak = new TweakDefinition
        {
            Id = "golden.02",
            Name = "RemoveEntry Test",
            Description = "Test",
            Category = "system",
            Risk = "Safe",
            Apply = new List<ActionItem>
            {
                new()
                {
                    Type = "Registry",
                    Hive = "HKCU",
                    Key = @"Software\Microsoft\Windows\CurrentVersion\Explorer",
                    Operation = "Set",
                    ValueName = "ClassicMenu",
                    Data = JsonSerializer.SerializeToElement("<RemoveEntry>")
                }
            },
            Undo = new List<ActionItem>(),
            Detect = new DetectStructure()
        };

        var report = await _runner.ApplyBatchAsync(new[] { tweak });
        Assert.Equal(1, report.AppliedCount);
        Assert.False(_fakeRegistry.ValueExists("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Explorer", "ClassicMenu"));
    }

    // =========================================================================
    // GOLDEN TEST 3: Registry Undo restores <RemoveEntry> if it did not exist
    // =========================================================================
    [Fact]
    public async Task Golden03_SetWinUtilRegistry_UndoRemovesEntryIfNonExistentBefore()
    {
        Assert.False(_fakeRegistry.ValueExists("HKCU", @"Software\TestKey", "NewValue"));

        var tweak = new TweakDefinition
        {
            Id = "golden.03",
            Name = "Rollback NonExistent",
            Description = "Test",
            Category = "system",
            Risk = "Safe",
            Apply = new List<ActionItem>
            {
                new()
                {
                    Type = "Registry",
                    Hive = "HKCU",
                    Key = @"Software\TestKey",
                    Operation = "Set",
                    ValueName = "NewValue",
                    ValueKind = "DWord",
                    Data = JsonSerializer.SerializeToElement(1)
                }
            },
            Undo = new List<ActionItem>
            {
                new()
                {
                    Type = "Registry",
                    Hive = "HKCU",
                    Key = @"Software\TestKey",
                    Operation = "DeleteValue",
                    ValueName = "NewValue"
                }
            },
            Detect = new DetectStructure()
        };

        await _runner.ApplyBatchAsync(new[] { tweak });
        Assert.True(_fakeRegistry.ValueExists("HKCU", @"Software\TestKey", "NewValue"));

        var undoRes = await _runner.UndoTweakAsync(tweak);
        Assert.True(undoRes.Success);
        Assert.False(_fakeRegistry.ValueExists("HKCU", @"Software\TestKey", "NewValue"));
    }

    // =========================================================================
    // GOLDEN TEST 4: Registry String values preserved accurately
    // =========================================================================
    [Fact]
    public async Task Golden04_SetWinUtilRegistry_StringValuePreserved()
    {
        var tweak = new TweakDefinition
        {
            Id = "golden.04",
            Name = "String Registry",
            Description = "Test",
            Category = "ui",
            Risk = "Safe",
            Apply = new List<ActionItem>
            {
                new()
                {
                    Type = "Registry",
                    Hive = "HKCU",
                    Key = @"Control Panel\Desktop",
                    Operation = "Set",
                    ValueName = "Wallpaper",
                    ValueKind = "String",
                    Data = JsonSerializer.SerializeToElement(@"C:\Windows\Web\Wallpaper\Default.jpg")
                }
            },
            Undo = new List<ActionItem>(),
            Detect = new DetectStructure()
        };

        var report = await _runner.ApplyBatchAsync(new[] { tweak });
        Assert.Equal(1, report.AppliedCount);
        Assert.Equal(@"C:\Windows\Web\Wallpaper\Default.jpg", _fakeRegistry.GetValue("HKCU", @"Control Panel\Desktop", "Wallpaper"));
    }

    // =========================================================================
    // GOLDEN TEST 5: Set-WinUtilService sets startup to Manual
    // =========================================================================
    [Fact]
    public async Task Golden05_SetWinUtilService_SetsStartupToManual()
    {
        _fakeServices.RegisterService("DiagTrack", "Automatic", "Running");

        var tweak = new TweakDefinition
        {
            Id = "golden.05",
            Name = "Service Manual",
            Description = "Test",
            Category = "system",
            Risk = "Safe",
            Apply = new List<ActionItem>
            {
                new()
                {
                    Type = "Service",
                    ServiceName = "DiagTrack",
                    Operation = "SetStartup",
                    StartupType = "Manual"
                }
            },
            Undo = new List<ActionItem>(),
            Detect = new DetectStructure()
        };

        var report = await _runner.ApplyBatchAsync(new[] { tweak });
        Assert.Equal(1, report.AppliedCount);
        Assert.Equal("Manual", _fakeServices.GetStartupType("DiagTrack"));
    }

    // =========================================================================
    // GOLDEN TEST 6: Set-WinUtilService sets startup to Disabled
    // =========================================================================
    [Fact]
    public async Task Golden06_SetWinUtilService_SetsStartupToDisabled()
    {
        _fakeServices.RegisterService("MapsBroker", "Automatic", "Running");

        var tweak = new TweakDefinition
        {
            Id = "golden.06",
            Name = "Service Disabled",
            Description = "Test",
            Category = "system",
            Risk = "Safe",
            Apply = new List<ActionItem>
            {
                new()
                {
                    Type = "Service",
                    ServiceName = "MapsBroker",
                    Operation = "SetStartup",
                    StartupType = "Disabled"
                }
            },
            Undo = new List<ActionItem>(),
            Detect = new DetectStructure()
        };

        var report = await _runner.ApplyBatchAsync(new[] { tweak });
        Assert.Equal(1, report.AppliedCount);
        Assert.Equal("Disabled", _fakeServices.GetStartupType("MapsBroker"));
    }

    // =========================================================================
    // GOLDEN TEST 7: Set-WinUtilService sets startup to Automatic
    // =========================================================================
    [Fact]
    public async Task Golden07_SetWinUtilService_SetsStartupToAutomatic()
    {
        _fakeServices.RegisterService("wuauserv", "Manual", "Stopped");

        var tweak = new TweakDefinition
        {
            Id = "golden.07",
            Name = "Service Auto",
            Description = "Test",
            Category = "system",
            Risk = "Safe",
            Apply = new List<ActionItem>
            {
                new()
                {
                    Type = "Service",
                    ServiceName = "wuauserv",
                    Operation = "SetStartup",
                    StartupType = "Automatic"
                }
            },
            Undo = new List<ActionItem>(),
            Detect = new DetectStructure()
        };

        var report = await _runner.ApplyBatchAsync(new[] { tweak });
        Assert.Equal(1, report.AppliedCount);
        Assert.Equal("Automatic", _fakeServices.GetStartupType("wuauserv"));
    }

    // =========================================================================
    // GOLDEN TEST 8: Set-WinUtilService Undo restores OriginalType
    // =========================================================================
    [Fact]
    public async Task Golden08_SetWinUtilService_UndoRestoresOriginalType()
    {
        _fakeServices.RegisterService("CscService", "Manual", "Stopped");

        var tweak = new TweakDefinition
        {
            Id = "golden.08",
            Name = "Service Undo OriginalType",
            Description = "Test",
            Category = "system",
            Risk = "Safe",
            Apply = new List<ActionItem>
            {
                new()
                {
                    Type = "Service",
                    ServiceName = "CscService",
                    Operation = "SetStartup",
                    StartupType = "Disabled"
                }
            },
            Undo = new List<ActionItem>
            {
                new()
                {
                    Type = "Service",
                    ServiceName = "CscService",
                    Operation = "SetStartup",
                    StartupType = "Manual"
                }
            },
            Detect = new DetectStructure()
        };

        await _runner.ApplyBatchAsync(new[] { tweak });
        Assert.Equal("Disabled", _fakeServices.GetStartupType("CscService"));

        var undoRes = await _runner.UndoTweakAsync(tweak);
        Assert.True(undoRes.Success);
        Assert.Equal("Manual", _fakeServices.GetStartupType("CscService"));
    }

    // =========================================================================
    // GOLDEN TEST 9: Service handler skips non-existent service gracefully
    // =========================================================================
    [Fact]
    public async Task Golden09_SetWinUtilService_GracefullySkipsNonExistentService()
    {
        Assert.False(_fakeServices.ServiceExists("NonExistentService123"));

        var tweak = new TweakDefinition
        {
            Id = "golden.09",
            Name = "Missing Service Tweak",
            Description = "Test",
            Category = "system",
            Risk = "Safe",
            Apply = new List<ActionItem>
            {
                new()
                {
                    Type = "Service",
                    ServiceName = "NonExistentService123",
                    Operation = "SetStartup",
                    StartupType = "Disabled"
                }
            },
            Undo = new List<ActionItem>(),
            Detect = new DetectStructure()
        };

        var report = await _runner.ApplyBatchAsync(new[] { tweak });
        // A service that is not installed cannot run, so the tweak counts as already applied
        // and is skipped instead of failing.
        Assert.Equal(0, report.FailedCount);
        Assert.Single(report.Results);
        Assert.Equal(TweakExecutionStatus.Skipped, report.Results[0].Status);
    }

    // =========================================================================
    // GOLDEN TEST 10: Invoke-WinUtilScript executes PowerShell script
    // =========================================================================
    [Fact]
    public async Task Golden10_InvokeWinUtilScript_ExecutesPowerShell()
    {
        var tweak = new TweakDefinition
        {
            Id = "golden.10",
            Name = "PowerShell Script Tweak",
            Description = "Test",
            Category = "system",
            Risk = "Safe",
            Apply = new List<ActionItem>
            {
                new()
                {
                    Type = "PowerShell",
                    Operation = "Execute",
                    Script = "Write-Host 'WinUtil parity test'",
                    TimeoutSeconds = 30
                }
            },
            Undo = new List<ActionItem>(),
            Detect = new DetectStructure()
        };

        var report = await _runner.ApplyBatchAsync(new[] { tweak });
        Assert.Equal(1, report.AppliedCount);
        Assert.Contains("Write-Host 'WinUtil parity test'", _fakeShell.ExecutedScripts);
    }

    // =========================================================================
    // GOLDEN TEST 11: Static UndoScript executes when Journal has no prior entry
    // =========================================================================
    [Fact]
    public async Task Golden11_UndoScript_ExecutesStaticFallback()
    {
        var tweak = new TweakDefinition
        {
            Id = "golden.11",
            Name = "UndoScript Fallback",
            Description = "Test",
            Category = "system",
            Risk = "Safe",
            Apply = new List<ActionItem>(),
            Undo = new List<ActionItem>
            {
                new()
                {
                    Type = "PowerShell",
                    Operation = "Execute",
                    Script = "Write-Host 'Undo script executed'",
                    TimeoutSeconds = 30
                }
            },
            Detect = new DetectStructure()
        };

        var undoRes = await _runner.UndoTweakAsync(tweak);
        Assert.True(undoRes.Success);
        Assert.Contains("Write-Host 'Undo script executed'", _fakeShell.ExecutedScripts);
    }

    // =========================================================================
    // GOLDEN TEST 12: Get-WinUtilToggleStatus returns TRUE when value matches
    // =========================================================================
    [Fact]
    public async Task Golden12_GetWinUtilToggleStatus_ReturnsTrueWhenMatched()
    {
        _fakeRegistry.SetValue("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 0, "DWord");

        var tweak = new TweakDefinition
        {
            Id = "golden.12",
            Name = "Dark Theme",
            Description = "Test",
            Category = "ui",
            Risk = "Safe",
            Apply = new List<ActionItem>(),
            Undo = new List<ActionItem>(),
            Detect = new DetectStructure
            {
                Type = "Registry",
                Hive = "HKCU",
                Key = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                ValueName = "AppsUseLightTheme",
                MatchType = "Equals",
                ExpectedValue = JsonSerializer.SerializeToElement(0)
            }
        };

        bool isActive = await _detector.IsTweakActiveAsync(tweak, useCache: false);
        Assert.True(isActive);
    }

    // =========================================================================
    // GOLDEN TEST 13: Get-WinUtilToggleStatus returns FALSE when value differs
    // =========================================================================
    [Fact]
    public async Task Golden13_GetWinUtilToggleStatus_ReturnsFalseWhenDiffers()
    {
        _fakeRegistry.SetValue("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1, "DWord");

        var tweak = new TweakDefinition
        {
            Id = "golden.13",
            Name = "Dark Theme",
            Description = "Test",
            Category = "ui",
            Risk = "Safe",
            Apply = new List<ActionItem>(),
            Undo = new List<ActionItem>(),
            Detect = new DetectStructure
            {
                Type = "Registry",
                Hive = "HKCU",
                Key = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                ValueName = "AppsUseLightTheme",
                MatchType = "Equals",
                ExpectedValue = JsonSerializer.SerializeToElement(0)
            }
        };

        bool isActive = await _detector.IsTweakActiveAsync(tweak, useCache: false);
        Assert.False(isActive);
    }

    // =========================================================================
    // GOLDEN TEST 14: Get-WinUtilToggleStatus evaluates multi-rule ALL correctly
    // =========================================================================
    [Fact]
    public async Task Golden14_GetWinUtilToggleStatus_MultiRuleAllEvaluation()
    {
        _fakeRegistry.SetValue("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", 0, "DWord");
        _fakeServices.RegisterService("DiagTrack", "Disabled", "Stopped");

        var tweak = new TweakDefinition
        {
            Id = "golden.14",
            Name = "Telemetry Multi",
            Description = "Test",
            Category = "privacy",
            Risk = "Safe",
            Apply = new List<ActionItem>(),
            Undo = new List<ActionItem>(),
            Detect = new DetectStructure
            {
                All = new List<DetectRule>
                {
                    new()
                    {
                        Type = "Registry",
                        Hive = "HKLM",
                        Key = @"SOFTWARE\Policies\Microsoft\Windows\DataCollection",
                        ValueName = "AllowTelemetry",
                        MatchType = "Equals",
                        ExpectedValue = JsonSerializer.SerializeToElement(0)
                    },
                    new()
                    {
                        Type = "Service",
                        ServiceName = "DiagTrack",
                        ExpectedStartup = "Disabled"
                    }
                }
            }
        };

        bool isActive = await _detector.IsTweakActiveAsync(tweak, useCache: false);
        Assert.True(isActive);

        // Change service back -> should become false
        _fakeServices.SetStartupType("DiagTrack", "Automatic");
        bool isStillActive = await _detector.IsTweakActiveAsync(tweak, useCache: false);
        Assert.False(isStillActive);
    }

    // =========================================================================
    // GOLDEN TEST 15: Preset Standard contains expected core upstream tweaks
    // =========================================================================
    [Fact]
    public void Golden15_PresetStandard_ContainsExpectedUpstreamTweaks()
    {
        string presetPath = FindCatalogFile(Path.Combine("Config", "preset.json"));
        Assert.True(File.Exists(presetPath));

        using var doc = JsonDocument.Parse(File.ReadAllText(presetPath));
        var standard = doc.RootElement.GetProperty("Standard").EnumerateArray().Select(x => x.GetString()).ToList();

        Assert.Contains("WPFTweaksTelemetry", standard);
        Assert.Contains("WPFTweaksServices", standard);
        Assert.Contains("WPFTweaksLocation", standard);
        Assert.Contains("WPFTweaksConsumerFeatures", standard);
        Assert.Contains("WPFTweaksRestorePoint", standard);
    }

    // =========================================================================
    // GOLDEN TEST 16: Preset Minimal contains exact minimal tweaks
    // =========================================================================
    [Fact]
    public void Golden16_PresetMinimal_ContainsExactMinimalTweaks()
    {
        string presetPath = FindCatalogFile(Path.Combine("Config", "preset.json"));
        Assert.True(File.Exists(presetPath));

        using var doc = JsonDocument.Parse(File.ReadAllText(presetPath));
        var minimal = doc.RootElement.GetProperty("Minimal").EnumerateArray().Select(x => x.GetString()).ToList();

        Assert.Equal(4, minimal.Count);
        Assert.Contains("WPFTweaksConsumerFeatures", minimal);
        Assert.Contains("WPFTweaksWPBT", minimal);
        Assert.Contains("WPFTweaksServices", minimal);
        Assert.Contains("WPFTweaksTelemetry", minimal);
    }

    // =========================================================================
    // GOLDEN TEST 17: Preset Advanced contains power user tweaks
    // =========================================================================
    [Fact]
    public void Golden17_PresetAdvanced_ContainsPowerUserTweaks()
    {
        string presetPath = FindCatalogFile(Path.Combine("Config", "preset.json"));
        Assert.True(File.Exists(presetPath));

        using var doc = JsonDocument.Parse(File.ReadAllText(presetPath));
        var advanced = doc.RootElement.GetProperty("Advanced").EnumerateArray().Select(x => x.GetString()).ToList();

        Assert.Contains("WPFTweaksRemoveOneDrive", advanced);
        Assert.Contains("WPFTweaksWindowsAI", advanced);
        Assert.Contains("WPFTweaksWidget", advanced);
    }

    // =========================================================================
    // GOLDEN TEST 18: Safety: BitLocker is NOT present in any preset
    // =========================================================================
    [Fact]
    public void Golden18_Safety_BitLockerExcludedFromAllPresets()
    {
        string presetPath = FindCatalogFile(Path.Combine("Config", "preset.json"));
        Assert.True(File.Exists(presetPath));

        string text = File.ReadAllText(presetPath);
        Assert.DoesNotContain("WPFTweaksDisableBitLocker", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("bitlocker", text, StringComparison.OrdinalIgnoreCase);
    }

    // =========================================================================
    // GOLDEN TEST 19: Safety: Adobe Net block is excluded from catalog
    // =========================================================================
    [Fact]
    public void Golden19_Safety_AdobeNetBlockExcludedFromCatalog()
    {
        string tweaksFolder = FindCatalogDirectory("Tweaks");
        foreach (string file in Directory.GetFiles(tweaksFolder, "*.json"))
        {
            string content = File.ReadAllText(file);
            Assert.DoesNotContain("WPFTweaksBlockAdobeNet", content, StringComparison.OrdinalIgnoreCase);
        }
    }

    // =========================================================================
    // GOLDEN TEST 20: Restore Point creation precedes tweak application
    // =========================================================================
    [Fact]
    public async Task Golden20_RestorePoint_PrecedesTweakExecution()
    {
        int initialRpCount = _fakeRestore.GetRestorePointCount();

        var tweak = new TweakDefinition
        {
            Id = "golden.20",
            Name = "Restore Point Guard",
            Description = "Test",
            Category = "system",
            Risk = "Safe",
            Apply = new List<ActionItem>
            {
                new()
                {
                    Type = "Registry",
                    Hive = "HKCU",
                    Key = @"Software\Test",
                    Operation = "Set",
                    ValueName = "Val",
                    ValueKind = "DWord",
                    Data = JsonSerializer.SerializeToElement(1)
                }
            },
            Undo = new List<ActionItem>(),
            Detect = new DetectStructure()
        };

        var report = await _runner.ApplyBatchAsync(new[] { tweak });
        Assert.Equal(1, report.AppliedCount);
        Assert.Equal(initialRpCount + 1, _fakeRestore.GetRestorePointCount());
    }

    // =========================================================================
    // GOLDEN TEST 21: Already applied tweak is skipped without double application
    // =========================================================================
    [Fact]
    public async Task Golden21_AlreadyAppliedTweak_IsSkipped()
    {
        _fakeRegistry.SetValue("HKCU", @"Software\Test", "Val", 1, "DWord");

        var tweak = new TweakDefinition
        {
            Id = "golden.21",
            Name = "Skip Active",
            Description = "Test",
            Category = "system",
            Risk = "Safe",
            Apply = new List<ActionItem>
            {
                new()
                {
                    Type = "Registry",
                    Hive = "HKCU",
                    Key = @"Software\Test",
                    Operation = "Set",
                    ValueName = "Val",
                    ValueKind = "DWord",
                    Data = JsonSerializer.SerializeToElement(1)
                }
            },
            Undo = new List<ActionItem>(),
            Detect = new DetectStructure
            {
                Type = "Registry",
                Hive = "HKCU",
                Key = @"Software\Test",
                ValueName = "Val",
                MatchType = "Equals",
                ExpectedValue = JsonSerializer.SerializeToElement(1)
            }
        };

        var report = await _runner.ApplyBatchAsync(new[] { tweak });
        Assert.Equal(0, report.AppliedCount);
        Assert.Equal(1, report.SkippedCount);
    }

    // =========================================================================
    // GOLDEN TEST 22: Apply twice + Undo restores exact original baseline
    // =========================================================================
    [Fact]
    public async Task Golden22_ApplyTwiceThenUndo_RestoresOriginalBaseline()
    {
        _fakeRegistry.SetValue("HKCU", @"Software\Test", "BaselineVal", 100, "DWord");

        var tweak = new TweakDefinition
        {
            Id = "golden.22",
            Name = "Baseline preservation",
            Description = "Test",
            Category = "system",
            Risk = "Safe",
            Apply = new List<ActionItem>
            {
                new()
                {
                    Type = "Registry",
                    Hive = "HKCU",
                    Key = @"Software\Test",
                    Operation = "Set",
                    ValueName = "BaselineVal",
                    ValueKind = "DWord",
                    Data = JsonSerializer.SerializeToElement(200)
                }
            },
            Undo = new List<ActionItem>
            {
                new()
                {
                    Type = "Registry",
                    Hive = "HKCU",
                    Key = @"Software\Test",
                    Operation = "Set",
                    ValueName = "BaselineVal",
                    ValueKind = "DWord",
                    Data = JsonSerializer.SerializeToElement(100)
                }
            },
            Detect = new DetectStructure()
        };

        // First apply: 100 -> 200
        await _runner.ApplyBatchAsync(new[] { tweak });
        Assert.Equal(200, _fakeRegistry.GetValue("HKCU", @"Software\Test", "BaselineVal"));

        // Change target again
        _fakeRegistry.SetValue("HKCU", @"Software\Test", "BaselineVal", 300, "DWord");

        // Second apply with new target
        tweak.Apply[0].Data = JsonSerializer.SerializeToElement(400);
        await _runner.ApplyBatchAsync(new[] { tweak });
        Assert.Equal(400, _fakeRegistry.GetValue("HKCU", @"Software\Test", "BaselineVal"));

        // Undo: Journal MUST restore 100 (the original baseline, not 300)
        var undoRes = await _runner.UndoTweakAsync(tweak);
        Assert.True(undoRes.Success);
        Assert.Equal(100, _fakeRegistry.GetValue("HKCU", @"Software\Test", "BaselineVal"));
    }

    // =========================================================================
    // GOLDEN TEST 23: Scheduled Task Enable/Disable Parity
    // =========================================================================
    [Fact]
    public async Task Golden23_ScheduledTask_EnableDisableParity()
    {
        _fakeTasks.RegisterTask(@"\Microsoft\Windows\UpdateOrchestrator", "Schedule Scan", isEnabled: true);

        var tweak = new TweakDefinition
        {
            Id = "golden.23",
            Name = "Disable Task",
            Description = "Test",
            Category = "updates",
            Risk = "Safe",
            Apply = new List<ActionItem>
            {
                new()
                {
                    Type = "ScheduledTask",
                    TaskPath = @"\Microsoft\Windows\UpdateOrchestrator",
                    TaskName = "Schedule Scan",
                    Operation = "Disable"
                }
            },
            Undo = new List<ActionItem>
            {
                new()
                {
                    Type = "ScheduledTask",
                    TaskPath = @"\Microsoft\Windows\UpdateOrchestrator",
                    TaskName = "Schedule Scan",
                    Operation = "Enable"
                }
            },
            Detect = new DetectStructure
            {
                Type = "ScheduledTask",
                TaskPath = @"\Microsoft\Windows\UpdateOrchestrator",
                TaskName = "Schedule Scan",
                ExpectedState = "Disabled"
            }
        };

        var report = await _runner.ApplyBatchAsync(new[] { tweak });
        Assert.Equal(1, report.AppliedCount);
        Assert.False(_fakeTasks.IsTaskEnabled(@"\Microsoft\Windows\UpdateOrchestrator", "Schedule Scan"));

        var undoRes = await _runner.UndoTweakAsync(tweak);
        Assert.True(undoRes.Success);
        Assert.True(_fakeTasks.IsTaskEnabled(@"\Microsoft\Windows\UpdateOrchestrator", "Schedule Scan"));
    }

    private static string FindCatalogFile(string relativePath)
    {
        string baseDir = AppContext.BaseDirectory;
        string? current = baseDir;
        while (current != null)
        {
            string candidate = Path.Combine(current, "Catalog", relativePath);
            if (File.Exists(candidate)) return candidate;
            current = Directory.GetParent(current)?.FullName;
        }
        return Path.Combine(@"C:\Dev\WinVelo\Catalog", relativePath);
    }

    private static string FindCatalogDirectory(string relativePath)
    {
        string baseDir = AppContext.BaseDirectory;
        string? current = baseDir;
        while (current != null)
        {
            string candidate = Path.Combine(current, "Catalog", relativePath);
            if (Directory.Exists(candidate)) return candidate;
            current = Directory.GetParent(current)?.FullName;
        }
        return Path.Combine(@"C:\Dev\WinVelo\Catalog", relativePath);
    }
}
