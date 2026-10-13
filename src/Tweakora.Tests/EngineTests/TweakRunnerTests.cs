using System.Text.Json;
using Tweakora.Core.Engine;
using Tweakora.Core.Engine.Handlers;
using Tweakora.Simulation;
using Tweakora.Core.Models;
using Xunit;

namespace Tweakora.Tests.EngineTests;

public sealed class TweakRunnerTests : IDisposable
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

    public TweakRunnerTests()
    {
        _tempJournalDir = Path.Combine(Path.GetTempPath(), "TweakoraTests_" + Guid.NewGuid().ToString("N"));
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

    [Fact]
    public async Task ApplyTweak_WhenValueDoesNotExist_RecordsExistedBeforeFalseAndRollbackDeletesIt()
    {
        // Arrange
        TweakDefinition tweak = CreateSampleTweak("test.reg.new", "HKCU", "Software\\TweakoraTest", "TestVal", 1);

        // Act
        BatchExecutionReport report = await _runner.ApplyBatchAsync(new[] { tweak });

        // Assert Apply
        Assert.Equal(1, report.AppliedCount);
        Assert.True(_fakeRegistry.ValueExists("HKCU", "Software\\TweakoraTest", "TestVal"));
        Assert.Equal(1, _fakeRegistry.GetValue("HKCU", "Software\\TweakoraTest", "TestVal"));

        // Act Undo
        ActionResult undoResult = await _runner.UndoTweakAsync(tweak);

        // Assert Undo
        Assert.True(undoResult.Success);
        Assert.False(_fakeRegistry.ValueExists("HKCU", "Software\\TweakoraTest", "TestVal"));
    }

    [Fact]
    public async Task ApplyTweak_WhenValueAlreadyExisted_RollbackRestoresExactOriginalValue()
    {
        // Arrange: Value initially existed with 42
        _fakeRegistry.SetValue("HKLM", "Software\\TweakoraTest", "ExistingVal", 42, "DWord");
        TweakDefinition tweak = CreateSampleTweak("test.reg.existing", "HKLM", "Software\\TweakoraTest", "ExistingVal", 0);

        // Act: Apply sets to 0
        BatchExecutionReport report = await _runner.ApplyBatchAsync(new[] { tweak });
        Assert.Equal(1, report.AppliedCount);
        Assert.Equal(0, _fakeRegistry.GetValue("HKLM", "Software\\TweakoraTest", "ExistingVal"));

        // Act: Rollback
        ActionResult undoResult = await _runner.UndoTweakAsync(tweak);

        // Assert: Restored back to 42
        Assert.True(undoResult.Success);
        Assert.Equal(42, _fakeRegistry.GetValue("HKLM", "Software\\TweakoraTest", "ExistingVal"));
    }

    [Fact]
    public async Task ApplyTweak_BaselinePreservation_DoesNotOverwriteOriginalBaselineIfReapplied()
    {
        // Arrange: Original value is 100
        _fakeRegistry.SetValue("HKCU", "Software\\TweakoraTest", "Speed", 100, "DWord");
        TweakDefinition tweak1 = CreateSampleTweak("test.baseline", "HKCU", "Software\\TweakoraTest", "Speed", 200);
        TweakDefinition tweak2 = CreateSampleTweak("test.baseline", "HKCU", "Software\\TweakoraTest", "Speed", 300);

        // Act: Apply first time -> value becomes 200
        await _runner.ApplyBatchAsync(new[] { tweak1 });
        Assert.Equal(200, _fakeRegistry.GetValue("HKCU", "Software\\TweakoraTest", "Speed"));

        // Act: Apply second time with different value -> value becomes 300
        await _runner.ApplyBatchAsync(new[] { tweak2 });
        Assert.Equal(300, _fakeRegistry.GetValue("HKCU", "Software\\TweakoraTest", "Speed"));

        // Act: Rollback
        await _runner.UndoTweakAsync(tweak1);

        // Assert: Original baseline 100 is restored, NOT 200!
        Assert.Equal(100, _fakeRegistry.GetValue("HKCU", "Software\\TweakoraTest", "Speed"));
    }

    [Fact]
    public async Task ApplyTweak_WhenActionFails_PerformsTransactionRollbackForThatTweak()
    {
        // Arrange: Action 1 succeeds, Action 2 has invalid action type
        TweakDefinition tweak = new()
        {
            Id = "test.transaction.fail",
            Name = "Tx Fail",
            Description = "Transaction test",
            Category = "system",
            Risk = "Safe",
            Impacts = new List<string> { "system" },
            Apply = new List<ActionItem>
            {
                new()
                {
                    Type = "Registry",
                    Hive = "HKCU",
                    Key = "Software\\TweakoraTest",
                    ValueName = "TxVal",
                    ValueKind = "DWord",
                    Data = JsonDocument.Parse("1").RootElement,
                    Operation = "Set"
                },
                new()
                {
                    Type = "InvalidUnknownType",
                    Operation = "Run"
                }
            },
            Undo = new List<ActionItem>(),
            Source = JsonDocument.Parse("\"https://microsoft.com\"").RootElement,
            VerifiedOnBuild = "22631"
        };

        // Act
        BatchExecutionReport report = await _runner.ApplyBatchAsync(new[] { tweak });

        // Assert: Tweak marked Failed, and Action 1 was rolled back
        Assert.Equal(1, report.FailedCount);
        Assert.False(_fakeRegistry.ValueExists("HKCU", "Software\\TweakoraTest", "TxVal"));
    }

    [Fact]
    public async Task ApplyTweak_WhenRestorePointThrottled_PromptsUserAndSkipsIfDenied()
    {
        // Arrange: Restore point simulated as throttled (24-hour frequency limit)
        _fakeRestore.SimulateThrottled = true;
        TweakDefinition tweak = CreateSampleTweak("test.restore.check", "HKCU", "Software\\TweakoraTest", "Val", 1);

        bool promptTriggered = false;

        // Act: User answers NO (cancel)
        BatchExecutionReport report = await _runner.ApplyBatchAsync(
            new[] { tweak },
            onRestorePointPromptAsync: prompt =>
            {
                promptTriggered = true;
                return Task.FromResult(false); // User decides not to proceed
            }
        );

        // Assert: Prompt was shown, tweak was skipped, registry was NOT touched
        Assert.True(promptTriggered);
        Assert.Equal(1, report.SkippedCount);
        Assert.False(_fakeRegistry.ValueExists("HKCU", "Software\\TweakoraTest", "Val"));
    }

    private static TweakDefinition CreateSampleTweak(string id, string hive, string key, string valueName, int data)
    {
        return new TweakDefinition
        {
            Id = id,
            Name = "Sample",
            Description = "Sample description",
            Category = "privacy",
            Risk = "Safe",
            Impacts = new List<string> { "privacy" },
            Detect = new DetectStructure
            {
                Type = "Registry",
                Hive = hive,
                Key = key,
                ValueName = valueName,
                MatchType = "Equals",
                ExpectedValue = JsonDocument.Parse(data.ToString()).RootElement
            },
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
            },
            Source = JsonDocument.Parse("\"https://microsoft.com\"").RootElement,
            VerifiedOnBuild = "22631"
        };
    }
}
