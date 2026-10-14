using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Tweakora.App.Services;
using Tweakora.App.ViewModels;
using Tweakora.Cli;
using Tweakora.Core.Engine;
using Tweakora.Core.Engine.Handlers;
using Tweakora.Core.Engine.Runners;
using Tweakora.Core.Interfaces;
using Tweakora.Core.Models;
using Tweakora.Core.Operations;
using Tweakora.Core.Safety;
using Tweakora.Modules.Advisor;
using Tweakora.Modules.Memory;
using Tweakora.Modules.Tweaks;
using Tweakora.Simulation;
using Xunit;

namespace Tweakora.Tests.EngineTests;

[Collection("CliConsoleTests")]
public sealed class HonestResultReportingTests : IDisposable
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

    public HonestResultReportingTests()
    {
        _tempJournalDir = Path.Combine(Path.GetTempPath(), "TweakoraHonestTests_" + Guid.NewGuid().ToString("N"));
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

    private sealed class FailingActionHandler : IActionHandler
    {
        public bool CanHandle(string actionType) => actionType == "FailAction";

        public Task<ActionResult> ExecuteAsync(ActionItem action, string tweakId, int actionIndex, Core.Engine.ExecutionContext context)
        {
            return Task.FromResult(new ActionResult
            {
                Success = false,
                Message = "Simulated intentional failure in test"
            });
        }

        public Task<ActionResult> UndoAsync(JournalActionRecord record, Core.Engine.ExecutionContext context)
        {
            return Task.FromResult(new ActionResult
            {
                Success = false,
                Message = "Simulated undo failure in test"
            });
        }

        public ActionPreview Preview(ActionItem action, Core.Engine.ExecutionContext context)
        {
            return new ActionPreview { ActionType = "FailAction", Target = "Test" };
        }
    }

    [Fact]
    public async Task TweakRunner_ApplyBatchAsync_WithPartialFailure_ReportsHonestCounts()
    {
        var handlers = new List<IActionHandler>(_handlers)
        {
            new FailingActionHandler()
        };
        var runner = new TweakRunner(handlers, _context, _fakeRestore, _detector);

        var tweakSuccess = new TweakDefinition
        {
            Id = "test.success",
            Name = "Success Tweak",
            Category = "Test",
            Apply = new List<ActionItem>
            {
                new()
                {
                    Type = "Registry",
                    Hive = "HKLM",
                    Key = @"Software\TestSuccess",
                    ValueName = "Flag",
                    ValueKind = "DWord",
                    Data = JsonDocument.Parse("1").RootElement,
                    Operation = "Set"
                }
            }
        };

        var tweakFailure = new TweakDefinition
        {
            Id = "test.failure",
            Name = "Failure Tweak",
            Category = "Test",
            Apply = new List<ActionItem>
            {
                new()
                {
                    Type = "FailAction",
                    Operation = "Execute"
                }
            }
        };

        var report = await runner.ApplyBatchAsync(new[] { tweakSuccess, tweakFailure });

        Assert.Equal(2, report.TotalRequested);
        Assert.Equal(1, report.AppliedCount);
        Assert.Equal(1, report.FailedCount);
        Assert.False(report.AllSucceeded);

        var failResult = report.Results.First(r => r.TweakId == "test.failure");
        Assert.True(failResult.IsFailure);
        Assert.Contains("Failed", failResult.DisplayStatus);
        Assert.Equal("Simulated intentional failure in test", failResult.Message);
    }

    [Fact]
    public async Task OperationRunner_BatchWithFailure_ProducesHonestSummaryAndFlags()
    {
        var opRunner = new OperationRunner();
        var items = new[] { "item1", "item2" };
        var report = await opRunner.RunAsync("TestBatch", items, (item, ct) =>
        {
            if (item == "item1")
            {
                return Task.FromResult(new OperationItemResult
                {
                    Id = "item1",
                    Name = "Step 1",
                    Status = OperationItemStatus.Applied
                });
            }
            return Task.FromResult(new OperationItemResult
            {
                Id = "item2",
                Name = "Step 2",
                Status = OperationItemStatus.Failed,
                Reason = "Failed intentionally"
            });
        });

        Assert.Equal(2, report.TotalRequested);
        Assert.Equal(1, report.SucceededCount);
        Assert.Equal(1, report.FailedCount);
        Assert.False(report.AllSucceeded);

        var failItem = report.Results.First(r => r.Name == "Step 2");
        Assert.True(failItem.IsFailure);
        Assert.Equal("Failed", failItem.DisplayStatus);
    }

    [Fact]
    public async Task TweaksViewModel_BatchApplyWithFailures_DoesNotMarkFailedCardAsActive()
    {
        var tweakSuccess = new TweakDefinition
        {
            Id = "tweak.good",
            Name = "Good Tweak",
            Category = "Test",
            Apply = new List<ActionItem>()
        };

        var tweakFail = new TweakDefinition
        {
            Id = "tweak.bad",
            Name = "Bad Tweak",
            Category = "Test",
            Apply = new List<ActionItem>()
        };

        var fakeTweakService = new FakeReportingTweakService(new List<TweakDefinition> { tweakSuccess, tweakFail });
        fakeTweakService.SetApplyResults(new List<OperationItemResult>
        {
            new() { Id = "tweak.good", Name = "Good Tweak", Status = OperationItemStatus.Applied },
            new() { Id = "tweak.bad", Name = "Bad Tweak", Status = OperationItemStatus.Failed, Reason = "Access denied" }
        });

        var opRunner = new OperationRunner();
        var vm = new TweaksViewModel(fakeTweakService, opRunner);

        // Select both tweaks
        foreach (var card in vm.FilteredTweaks)
        {
            card.IsSelected = true;
        }

        await vm.ApplySelectedTweaksAsync();

        // Honest status message checks
        Assert.Contains("Applied 1 of 2 — 1 failed (details)", vm.StatusMessage);
        Assert.True(vm.HasFailedDetails);
        Assert.Single(vm.ExecutionDetailsItems);
        Assert.Equal("Bad Tweak", vm.ExecutionDetailsItems[0].Name);

        // Succeeded card must be Active, failed card must NOT be active
        var goodCard = vm.FilteredTweaks.First(t => t.Id == "tweak.good");
        var badCard = vm.FilteredTweaks.First(t => t.Id == "tweak.bad");
        Assert.True(goodCard.IsActive);
        Assert.False(badCard.IsActive);
    }

    [Fact]
    public async Task CliProgram_ExecutesTweaks_ReturnsNonZeroAndPrintsHonestSummary_WhenFailuresOccur()
    {
        bool prev = TestModeManager.IsTestMode;
        try
        {
            TestModeManager.EnableTestMode();

            var originalOut = Console.Out;
            using var sw = new StringWriter();
            Console.SetOut(sw);

            try
            {
                // Run CLI in test-mode with one valid tweak and one invalid tweak ID
                int exitCode = await Program.Main(new[] { "--test-mode", "--tweaks", "essential.activity,nonexistent.invalid.tweak" });

                string output = sw.ToString();

                // Non-zero exit code required on failure
                Assert.Equal(1, exitCode);
                Assert.Contains("[Failed] nonexistent.invalid.tweak: Tweak not found in catalog", output);
                Assert.Contains("Applied 1 of 2 — 1 failed", output);
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }
        finally
        {
            TestModeManager.IsTestMode = prev;
        }
    }

    [Fact]
    public async Task CliProgram_ExecutesTweaks_ReturnsZeroAndPrintsSuccess_WhenAllSucceed()
    {
        bool prev = TestModeManager.IsTestMode;
        try
        {
            TestModeManager.EnableTestMode();

            var originalOut = Console.Out;
            using var sw = new StringWriter();
            Console.SetOut(sw);

            try
            {
                int exitCode = await Program.Main(new[] { "--test-mode", "--tweaks", "essential.activity" });
                string output = sw.ToString();

                Assert.Equal(0, exitCode);
                Assert.Contains("Applied 1 of 1 — 0 failed", output);
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }
        finally
        {
            TestModeManager.IsTestMode = prev;
        }
    }

    private sealed class FakeReportingTweakService : ITweakService
    {
        private readonly List<TweakDefinition> _catalog;
        private List<OperationItemResult> _resultsToReturn = new();

        public FakeReportingTweakService(List<TweakDefinition> catalog)
        {
            _catalog = catalog;
            BackupManager = new BackupManager(new FakeRegistryService(), new FakeServicesService(), new FakeRestoreService(), Path.GetTempPath());
        }

        public BackupManager BackupManager { get; }

        public void SetApplyResults(List<OperationItemResult> results) => _resultsToReturn = results;

        public IReadOnlyList<TweakDefinition> LoadCatalog() => _catalog;

        public bool DetectState(TweakDefinition tweak) => false;
        public Task<bool> DetectStateAsync(TweakDefinition tweak, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<TweakState> DetectTweakStateAsync(TweakDefinition tweak, CancellationToken cancellationToken = default) => Task.FromResult(TweakState.NotApplied);
        public Task<string> GenerateDryRunDiffAsync(IEnumerable<TweakDefinition> tweaks) => Task.FromResult(string.Empty);

        public Task<OperationExecutionReport> ApplyTweaksAsync(IReadOnlyList<TweakDefinition> tweaks, IOperationRunner runner, CancellationToken cancellationToken = default)
        {
            var report = new OperationExecutionReport
            {
                Title = "ApplyTweaks",
                StartTime = DateTime.UtcNow,
                EndTime = DateTime.UtcNow,
                Results = _resultsToReturn
            };
            return Task.FromResult(report);
        }

        public Task<OperationExecutionReport> UndoTweaksAsync(IReadOnlyList<TweakDefinition> tweaks, IOperationRunner runner, CancellationToken cancellationToken = default)
        {
            var report = new OperationExecutionReport
            {
                Title = "UndoTweaks",
                StartTime = DateTime.UtcNow,
                EndTime = DateTime.UtcNow,
                Results = _resultsToReturn
            };
            return Task.FromResult(report);
        }

        public Task<bool> SetPerformancePlanAsync(string planName) => Task.FromResult(true);
        public Task<bool> SetDnsAsync(string primary, string secondary) => Task.FromResult(true);
    }
}
