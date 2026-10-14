using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Tweakora.Core.Engine;
using Tweakora.Core.Engine.Handlers;
using Tweakora.Core.Interfaces;
using Tweakora.Core.Models;
using Tweakora.Simulation;
using Xunit;

namespace Tweakora.Tests.EngineTests;

public sealed class UndoEngineTests : IDisposable
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

    public UndoEngineTests()
    {
        _tempJournalDir = Path.Combine(Path.GetTempPath(), "TweakoraUndoTests_" + Guid.NewGuid().ToString("N"));
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
            new FileActionHandler(),
            new PowerShellActionHandler()
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
    public async Task Apply_Undo_Apply_Undo_CapturesFreshBaselineAndAvoidsStaleReplay()
    {
        // 1. Initial system state: Registry value is 100
        _fakeRegistry.SetValue("HKCU", @"Software\WinVeloTest", "TestParam", 100, "DWord");

        var tweak = new TweakDefinition
        {
            Id = "test.lifecycle",
            Name = "Lifecycle Test",
            Category = "Test",
            Apply = new List<ActionItem>
            {
                new()
                {
                    Type = "Registry",
                    Hive = "HKCU",
                    Key = @"Software\WinVeloTest",
                    ValueName = "TestParam",
                    ValueKind = "DWord",
                    Data = JsonDocument.Parse("200").RootElement,
                    Operation = "Set"
                }
            },
            Undo = new List<ActionItem>
            {
                new()
                {
                    Type = "Registry",
                    Hive = "HKCU",
                    Key = @"Software\WinVeloTest",
                    ValueName = "TestParam",
                    ValueKind = "DWord",
                    Data = JsonDocument.Parse("999").RootElement, // Stale/catalog default, should NOT overwrite journal baseline!
                    Operation = "Set"
                }
            }
        };

        // --- FIRST APPLY ---
        var applyReport1 = await _runner.ApplyBatchAsync(new[] { tweak });
        Assert.Equal(1, applyReport1.AppliedCount);
        Assert.Equal(200, _fakeRegistry.GetValue("HKCU", @"Software\WinVeloTest", "TestParam"));

        // --- FIRST UNDO ---
        var undoResult1 = await _runner.UndoTweakAsync(tweak);
        Assert.True(undoResult1.Success);
        // Restored exact initial baseline 100, NOT the catalog undo value 999
        Assert.Equal(100, _fakeRegistry.GetValue("HKCU", @"Software\WinVeloTest", "TestParam"));
        Assert.NotEmpty(undoResult1.ExecutedSteps);

        // Verify journal records are now marked reverted
        var allSessions = _journal.GetAllSessions();
        var records = allSessions.SelectMany(s => s.Records).Where(r => r.TweakId == tweak.Id).ToList();
        Assert.All(records, r => Assert.True(r.IsReverted));

        // --- EXTERNAL STATE CHANGE ---
        // Suppose user or another app changed the setting to 300
        _fakeRegistry.SetValue("HKCU", @"Software\WinVeloTest", "TestParam", 300, "DWord");

        // --- SECOND APPLY ---
        // Must capture 300 as the new fresh baseline, not the stale 100
        var applyReport2 = await _runner.ApplyBatchAsync(new[] { tweak });
        Assert.Equal(1, applyReport2.AppliedCount);
        Assert.Equal(200, _fakeRegistry.GetValue("HKCU", @"Software\WinVeloTest", "TestParam"));

        // --- SECOND UNDO ---
        var undoResult2 = await _runner.UndoTweakAsync(tweak);
        Assert.True(undoResult2.Success);
        // Must restore the second baseline 300!
        Assert.Equal(300, _fakeRegistry.GetValue("HKCU", @"Software\WinVeloTest", "TestParam"));
    }

    [Fact]
    public async Task Undo_StatefulAndStatelessHybrid_RestoresRegistryFromJournalAndRunsPowerShellScript()
    {
        _fakeRegistry.SetValue("HKLM", @"Software\WinVeloTest", "Flag", 10, "DWord");

        var tweak = new TweakDefinition
        {
            Id = "test.hybrid",
            Name = "Hybrid Test",
            Category = "Test",
            Apply = new List<ActionItem>
            {
                new()
                {
                    Type = "Registry",
                    Hive = "HKLM",
                    Key = @"Software\WinVeloTest",
                    ValueName = "Flag",
                    ValueKind = "DWord",
                    Data = JsonDocument.Parse("20").RootElement,
                    Operation = "Set"
                },
                new()
                {
                    Type = "PowerShell",
                    Script = "Write-Output 'Applying hybrid tweak'",
                    Operation = "Run"
                }
            },
            Undo = new List<ActionItem>
            {
                new()
                {
                    Type = "Registry",
                    Hive = "HKLM",
                    Key = @"Software\WinVeloTest",
                    ValueName = "Flag",
                    ValueKind = "DWord",
                    Data = JsonDocument.Parse("555").RootElement, // Catalog fallback (must not overwrite journal baseline 10)
                    Operation = "Set"
                },
                new()
                {
                    Type = "PowerShell",
                    Script = "Write-Output 'Reinstalling component via PowerShell undo'",
                    Operation = "Run"
                }
            }
        };

        // Apply
        var applyReport = await _runner.ApplyBatchAsync(new[] { tweak });
        Assert.Equal(1, applyReport.AppliedCount);
        Assert.Equal(20, _fakeRegistry.GetValue("HKLM", @"Software\WinVeloTest", "Flag"));

        // Undo
        var undoResult = await _runner.UndoTweakAsync(tweak);
        Assert.True(undoResult.Success);

        // Registry must be restored to 10 (journal baseline), not 555
        Assert.Equal(10, _fakeRegistry.GetValue("HKLM", @"Software\WinVeloTest", "Flag"));

        // ExecutedSteps must contain both the journal restore and the catalog script execution
        Assert.Contains(undoResult.ExecutedSteps, s => s.Contains("Restored Registry") && s.Contains("from journal"));
        Assert.Contains(undoResult.ExecutedSteps, s => s.Contains("Executed catalog undo script: PowerShell"));
    }

    [Fact]
    public async Task Undo_WithoutPriorJournal_FallsBackToCatalogUndoActions()
    {
        _fakeRegistry.SetValue("HKCU", @"Software\WinVeloTest", "FallbackFlag", 0, "DWord");

        var tweak = new TweakDefinition
        {
            Id = "test.nojournal",
            Name = "No Journal Test",
            Category = "Test",
            Apply = new List<ActionItem>(),
            Undo = new List<ActionItem>
            {
                new()
                {
                    Type = "Registry",
                    Hive = "HKCU",
                    Key = @"Software\WinVeloTest",
                    ValueName = "FallbackFlag",
                    ValueKind = "DWord",
                    Data = JsonDocument.Parse("42").RootElement,
                    Operation = "Set"
                }
            }
        };

        // Undo directly without applying first (no journal exists)
        var undoResult = await _runner.UndoTweakAsync(tweak);
        Assert.True(undoResult.Success);
        Assert.Equal(42, _fakeRegistry.GetValue("HKCU", @"Software\WinVeloTest", "FallbackFlag"));
        Assert.Contains(undoResult.ExecutedSteps, s => s.Contains("Executed catalog fallback undo"));
    }

    [Fact]
    public async Task Apply_PartialFailure_RollsBackSuccessfullyAndReportsFailedRolledBack()
    {
        _fakeRegistry.SetValue("HKLM", @"Software\WinVeloTest", "PreExisting", 7, "DWord");

        var handlers = new List<IActionHandler>(_handlers)
        {
            new FaultyHandler(failOnExecute: true, failOnUndo: false)
        };
        var runner = new TweakRunner(handlers, _context, _fakeRestore, _detector);

        var tweak = new TweakDefinition
        {
            Id = "test.partial.rollback",
            Name = "Partial Rollback Test",
            Category = "Test",
            Apply = new List<ActionItem>
            {
                // Action 1: Sets registry (will succeed)
                new()
                {
                    Type = "Registry",
                    Hive = "HKLM",
                    Key = @"Software\WinVeloTest",
                    ValueName = "PreExisting",
                    ValueKind = "DWord",
                    Data = JsonDocument.Parse("99").RootElement,
                    Operation = "Set"
                },
                // Action 2: Faulty handler (will fail)
                new()
                {
                    Type = "FaultyAction",
                    Operation = "Execute"
                }
            }
        };

        var report = await runner.ApplyBatchAsync(new[] { tweak });

        Assert.Equal(1, report.FailedCount);
        Assert.Equal(0, report.AppliedCount);

        var res = report.Results.First();
        Assert.Equal(TweakExecutionStatus.FailedRolledBack, res.Status);
        Assert.Equal("Failed (rolled back)", res.DisplayStatus);
        Assert.Contains("rolled back successfully", res.Message);

        // Registry value should have been rolled back to original 7
        Assert.Equal(7, _fakeRegistry.GetValue("HKLM", @"Software\WinVeloTest", "PreExisting"));
    }

    [Fact]
    public async Task Apply_PartialFailure_RollbackFails_ReportsFailedRollbackIncomplete()
    {
        _fakeRegistry.SetValue("HKLM", @"Software\WinVeloTest", "PreExisting", 7, "DWord");

        var handlers = new List<IActionHandler>
        {
            new FaultyHandler(failOnExecute: false, failOnUndo: true), // Step 1 succeeds on execute, fails on undo
            new FaultyHandler2(failOnExecute: true) // Step 2 fails on execute
        };
        var runner = new TweakRunner(handlers, _context, _fakeRestore, _detector);

        var tweak = new TweakDefinition
        {
            Id = "test.incomplete.rollback",
            Name = "Incomplete Rollback Test",
            Category = "Test",
            Apply = new List<ActionItem>
            {
                new()
                {
                    Type = "FaultyAction",
                    Operation = "Execute"
                },
                new()
                {
                    Type = "FaultyAction2",
                    Operation = "Execute"
                }
            }
        };

        var report = await runner.ApplyBatchAsync(new[] { tweak });

        Assert.Equal(1, report.FailedCount);
        var res = report.Results.First();
        Assert.Equal(TweakExecutionStatus.FailedRollbackIncomplete, res.Status);
        Assert.Equal("Failed (rollback incomplete)", res.DisplayStatus);
        Assert.Contains("Rollback incomplete", res.Message);
    }

    private sealed class FaultyHandler : IActionHandler
    {
        private readonly bool _failOnExecute;
        private readonly bool _failOnUndo;

        public FaultyHandler(bool failOnExecute, bool failOnUndo)
        {
            _failOnExecute = failOnExecute;
            _failOnUndo = failOnUndo;
        }

        public bool CanHandle(string actionType) => actionType == "FaultyAction";

        public Task<ActionResult> ExecuteAsync(ActionItem action, string tweakId, int actionIndex, Core.Engine.ExecutionContext context)
        {
            var record = new JournalActionRecord
            {
                TweakId = tweakId,
                ActionIndex = actionIndex,
                ActionType = "FaultyAction",
                TargetIdentifier = "faulty://target",
                IsBaseline = true
            };
            context.Journal.RecordActionWriteAhead(record);

            if (_failOnExecute)
            {
                return Task.FromResult(ActionResult.Fail("Simulated execute failure"));
            }
            return Task.FromResult(new ActionResult { Success = true, JournalRecord = record });
        }

        public Task<ActionResult> UndoAsync(JournalActionRecord record, Core.Engine.ExecutionContext context)
        {
            if (_failOnUndo)
            {
                return Task.FromResult(ActionResult.Fail("Simulated rollback undo failure"));
            }
            return Task.FromResult(ActionResult.Ok());
        }

        public ActionPreview Preview(ActionItem action, Core.Engine.ExecutionContext context)
        {
            return new ActionPreview { ActionType = "FaultyAction", Target = "Faulty" };
        }
    }

    private sealed class FaultyHandler2 : IActionHandler
    {
        private readonly bool _failOnExecute;

        public FaultyHandler2(bool failOnExecute)
        {
            _failOnExecute = failOnExecute;
        }

        public bool CanHandle(string actionType) => actionType == "FaultyAction2";

        public Task<ActionResult> ExecuteAsync(ActionItem action, string tweakId, int actionIndex, Core.Engine.ExecutionContext context)
        {
            if (_failOnExecute)
            {
                return Task.FromResult(ActionResult.Fail("Simulated second action failure"));
            }
            return Task.FromResult(ActionResult.Ok());
        }

        public Task<ActionResult> UndoAsync(JournalActionRecord record, Core.Engine.ExecutionContext context)
        {
            return Task.FromResult(ActionResult.Ok());
        }

        public ActionPreview Preview(ActionItem action, Core.Engine.ExecutionContext context)
        {
            return new ActionPreview { ActionType = "FaultyAction2", Target = "Faulty2" };
        }
    }
}
