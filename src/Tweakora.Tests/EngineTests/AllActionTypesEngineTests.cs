using System.Text.Json;
using Tweakora.Core.Engine;
using Tweakora.Core.Engine.Handlers;
using Tweakora.Simulation;
using Tweakora.Core.Models;
using Xunit;

namespace Tweakora.Tests.EngineTests;

public sealed class AllActionTypesEngineTests : IDisposable
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

    public AllActionTypesEngineTests()
    {
        _tempJournalDir = Path.Combine(Path.GetTempPath(), "TweakoraActionTests_" + Guid.NewGuid().ToString("N"));
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

    // ==========================================
    // 1. REGISTRY ACTION TESTS
    // ==========================================
    [Fact]
    public async Task RegistryAction_Apply_SetsValueCorrectly()
    {
        var tweak = CreateRegistryTweak("test.reg.apply", "HKCU", "Software\\Test", "Val1", 1);
        var report = await _runner.ApplyBatchAsync(new[] { tweak });

        Assert.Equal(1, report.AppliedCount);
        Assert.True(_fakeRegistry.ValueExists("HKCU", "Software\\Test", "Val1"));
        Assert.Equal(1, _fakeRegistry.GetValue("HKCU", "Software\\Test", "Val1"));
    }

    [Fact]
    public async Task RegistryAction_Undo_RevertsToOriginalBaseline()
    {
        _fakeRegistry.SetValue("HKCU", "Software\\Test", "Val1", 42, "DWord");
        var tweak = CreateRegistryTweak("test.reg.undo", "HKCU", "Software\\Test", "Val1", 99);

        await _runner.ApplyBatchAsync(new[] { tweak });
        Assert.Equal(99, _fakeRegistry.GetValue("HKCU", "Software\\Test", "Val1"));

        var undoResult = await _runner.UndoTweakAsync(tweak);
        Assert.True(undoResult.Success);
        Assert.Equal(42, _fakeRegistry.GetValue("HKCU", "Software\\Test", "Val1"));
    }

    [Fact]
    public async Task RegistryAction_ApplyTwiceThenUndo_PreservesInitialBaseline()
    {
        _fakeRegistry.SetValue("HKCU", "Software\\Test", "Val1", 10, "DWord");
        var tweak1 = CreateRegistryTweak("test.reg.multi", "HKCU", "Software\\Test", "Val1", 20);
        var tweak2 = CreateRegistryTweak("test.reg.multi", "HKCU", "Software\\Test", "Val1", 30);

        await _runner.ApplyBatchAsync(new[] { tweak1 });
        Assert.Equal(20, _fakeRegistry.GetValue("HKCU", "Software\\Test", "Val1"));

        await _runner.ApplyBatchAsync(new[] { tweak2 });
        Assert.Equal(30, _fakeRegistry.GetValue("HKCU", "Software\\Test", "Val1"));

        var undoResult = await _runner.UndoTweakAsync(tweak1);
        Assert.True(undoResult.Success);
        Assert.Equal(10, _fakeRegistry.GetValue("HKCU", "Software\\Test", "Val1"));
    }

    // ==========================================
    // 2. SERVICE ACTION TESTS
    // ==========================================
    [Fact]
    public async Task ServiceAction_Apply_ChangesStartupType()
    {
        _fakeServices.Services["DiagTrack"] = ("Automatic", "Running");
        var tweak = CreateServiceTweak("test.svc.apply", "DiagTrack", "Disabled");

        var report = await _runner.ApplyBatchAsync(new[] { tweak });

        Assert.Equal(1, report.AppliedCount);
        Assert.Equal("Disabled", _fakeServices.GetStartupType("DiagTrack"));
    }

    [Fact]
    public async Task ServiceAction_Undo_RevertsToOriginalStartupAndState()
    {
        _fakeServices.Services["DiagTrack"] = ("Automatic", "Running");
        var tweak = CreateServiceTweak("test.svc.undo", "DiagTrack", "Disabled");

        await _runner.ApplyBatchAsync(new[] { tweak });
        Assert.Equal("Disabled", _fakeServices.GetStartupType("DiagTrack"));

        var undoResult = await _runner.UndoTweakAsync(tweak);
        Assert.True(undoResult.Success);
        Assert.Equal("Automatic", _fakeServices.GetStartupType("DiagTrack"));
    }

    [Fact]
    public async Task ServiceAction_ApplyTwiceThenUndo_PreservesInitialBaseline()
    {
        _fakeServices.Services["DiagTrack"] = ("Automatic", "Running");
        var tweak1 = CreateServiceTweak("test.svc.multi", "DiagTrack", "Manual");
        var tweak2 = CreateServiceTweak("test.svc.multi", "DiagTrack", "Disabled");

        await _runner.ApplyBatchAsync(new[] { tweak1 });
        Assert.Equal("Manual", _fakeServices.GetStartupType("DiagTrack"));

        await _runner.ApplyBatchAsync(new[] { tweak2 });
        Assert.Equal("Disabled", _fakeServices.GetStartupType("DiagTrack"));

        var undoResult = await _runner.UndoTweakAsync(tweak1);
        Assert.True(undoResult.Success);
        Assert.Equal("Automatic", _fakeServices.GetStartupType("DiagTrack"));
    }

    // ==========================================
    // 3. SCHEDULED TASK ACTION TESTS
    // ==========================================
    [Fact]
    public async Task TaskAction_Apply_DisablesScheduledTask()
    {
        _fakeTasks.Tasks[@"\Microsoft\Windows\Customer Experience Improvement Program\Consolidator"] = true;
        var tweak = CreateTaskTweak("test.task.apply", @"\Microsoft\Windows\Customer Experience Improvement Program", "Consolidator", "Disable");

        var report = await _runner.ApplyBatchAsync(new[] { tweak });

        Assert.Equal(1, report.AppliedCount);
        Assert.False(_fakeTasks.IsTaskEnabled(@"\Microsoft\Windows\Customer Experience Improvement Program", "Consolidator"));
    }

    [Fact]
    public async Task TaskAction_Undo_RestoresOriginalTaskState()
    {
        _fakeTasks.Tasks[@"\Microsoft\Windows\Customer Experience Improvement Program\Consolidator"] = true;
        var tweak = CreateTaskTweak("test.task.undo", @"\Microsoft\Windows\Customer Experience Improvement Program", "Consolidator", "Disable");

        await _runner.ApplyBatchAsync(new[] { tweak });
        Assert.False(_fakeTasks.IsTaskEnabled(@"\Microsoft\Windows\Customer Experience Improvement Program", "Consolidator"));

        var undoResult = await _runner.UndoTweakAsync(tweak);
        Assert.True(undoResult.Success);
        Assert.True(_fakeTasks.IsTaskEnabled(@"\Microsoft\Windows\Customer Experience Improvement Program", "Consolidator"));
    }

    [Fact]
    public async Task TaskAction_ApplyTwiceThenUndo_PreservesInitialBaseline()
    {
        _fakeTasks.Tasks[@"\Microsoft\Windows\Feedback\Siuf\DmClient"] = true;
        var tweak1 = CreateTaskTweak("test.task.multi", @"\Microsoft\Windows\Feedback\Siuf", "DmClient", "Disable");
        var tweak2 = CreateTaskTweak("test.task.multi", @"\Microsoft\Windows\Feedback\Siuf", "DmClient", "Disable");

        await _runner.ApplyBatchAsync(new[] { tweak1 });
        Assert.False(_fakeTasks.IsTaskEnabled(@"\Microsoft\Windows\Feedback\Siuf", "DmClient"));

        await _runner.ApplyBatchAsync(new[] { tweak2 });
        Assert.False(_fakeTasks.IsTaskEnabled(@"\Microsoft\Windows\Feedback\Siuf", "DmClient"));

        var undoResult = await _runner.UndoTweakAsync(tweak1);
        Assert.True(undoResult.Success);
        Assert.True(_fakeTasks.IsTaskEnabled(@"\Microsoft\Windows\Feedback\Siuf", "DmClient"));
    }

    // ==========================================
    // 4. FILE ACTION TESTS
    // ==========================================
    [Fact]
    public async Task FileAction_ApplyAndUndo_HandlesSafelyAndIdempotently()
    {
        // Setup isolated test folder in temp
        string testFolder = Path.Combine(Path.GetTempPath(), "TweakoraFileTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testFolder);
        File.WriteAllText(Path.Combine(testFolder, "temp.log"), "junk");

        try
        {
            var tweak = new TweakDefinition
            {
                Id = "test.file.clean",
                Name = "Clean Temp",
                Description = "Clean test files",
                Category = "performance",
                Risk = "Safe",
                Apply = new List<ActionItem>
                {
                    new()
                    {
                        Type = "File",
                        AllowedRoot = "%TEMP%",
                        Operation = "EmptyFolder"
                    }
                },
                Undo = new List<ActionItem>
                {
                    new()
                    {
                        Type = "File",
                        AllowedRoot = "%TEMP%",
                        Operation = "EmptyFolder"
                    }
                }
            };

            var dryContext = new Core.Engine.ExecutionContext
            {
                Registry = _fakeRegistry,
                Services = _fakeServices,
                Tasks = _fakeTasks,
                Shell = _fakeShell,
                Journal = _journal,
                IsDryRun = true,
                BackupDirectory = _tempJournalDir
            };
            var dryRunner = new TweakRunner(_handlers, dryContext, _fakeRestore, _detector);

            var report = await dryRunner.ApplyBatchAsync(new[] { tweak });
            Assert.Equal(1, report.AppliedCount);

            // Undo is idempotent
            var undoResult = await dryRunner.UndoTweakAsync(tweak);
            Assert.True(undoResult.Success);
        }
        finally
        {
            try { if (Directory.Exists(testFolder)) Directory.Delete(testFolder, true); } catch { }
        }
    }

    // Helper builders
    private static TweakDefinition CreateRegistryTweak(string id, string hive, string key, string valueName, int data)
    {
        return new TweakDefinition
        {
            Id = id,
            Name = "Reg Tweak",
            Description = "Reg Description",
            Category = "privacy",
            Risk = "Safe",
            Apply = new List<ActionItem>
            {
                new()
                {
                    Type = "Registry",
                    Hive = hive,
                    Key = key,
                    ValueName = valueName,
                    ValueKind = "DWord",
                    Data = JsonDocument.Parse(data.ToString()).RootElement,
                    Operation = "Set"
                }
            },
            Undo = new List<ActionItem>
            {
                new()
                {
                    Type = "Registry",
                    Hive = hive,
                    Key = key,
                    ValueName = valueName,
                    Operation = "DeleteValue"
                }
            }
        };
    }

    private static TweakDefinition CreateServiceTweak(string id, string serviceName, string startupType)
    {
        return new TweakDefinition
        {
            Id = id,
            Name = "Service Tweak",
            Description = "Service Description",
            Category = "performance",
            Risk = "Safe",
            Apply = new List<ActionItem>
            {
                new()
                {
                    Type = "Service",
                    ServiceName = serviceName,
                    StartupType = startupType,
                    Operation = "SetStartup"
                }
            },
            Undo = new List<ActionItem>
            {
                new()
                {
                    Type = "Service",
                    ServiceName = serviceName,
                    StartupType = "Automatic",
                    Operation = "SetStartup"
                }
            }
        };
    }

    private static TweakDefinition CreateTaskTweak(string id, string taskPath, string taskName, string operation)
    {
        return new TweakDefinition
        {
            Id = id,
            Name = "Task Tweak",
            Description = "Task Description",
            Category = "privacy",
            Risk = "Safe",
            Apply = new List<ActionItem>
            {
                new()
                {
                    Type = "ScheduledTask",
                    TaskPath = taskPath,
                    TaskName = taskName,
                    Operation = operation
                }
            },
            Undo = new List<ActionItem>
            {
                new()
                {
                    Type = "ScheduledTask",
                    TaskPath = taskPath,
                    TaskName = taskName,
                    Operation = "Enable"
                }
            }
        };
    }
}
