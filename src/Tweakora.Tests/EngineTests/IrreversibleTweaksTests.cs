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
using Tweakora.Modules.Tweaks;
using Tweakora.Simulation;
using Xunit;

namespace Tweakora.Tests.EngineTests;

[Collection("CliConsoleTests")]
public sealed class IrreversibleTweaksTests : IDisposable
{
    private readonly string _tempJournalDir;

    public IrreversibleTweaksTests()
    {
        _tempJournalDir = Path.Combine(Path.GetTempPath(), "TweakoraIrreversibleTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempJournalDir);
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
    public void Catalog_IrreversibleTweaks_HaveReversibleFalseAndNonEmptyReason()
    {
        string? solutionRoot = FindSolutionRoot();
        Assert.NotNull(solutionRoot);
        string tweaksDir = Path.Combine(solutionRoot, "Catalog", "Tweaks");
        Assert.True(Directory.Exists(tweaksDir), $"Catalog directory not found at {tweaksDir}");

        List<TweakDefinition> allTweaks = new();
        foreach (var file in Directory.GetFiles(tweaksDir, "*.json", SearchOption.AllDirectories))
        {
            string json = File.ReadAllText(file);
            var tweaks = JsonSerializer.Deserialize<List<TweakDefinition>>(json);
            if (tweaks != null) allTweaks.AddRange(tweaks);
        }

        string[] expectedIrreversible =
        {
            "advanced.removeedge",
            "advanced.removeonedrive",
            "advanced.windowsai",
            "essential.widget",
            "essential.diskcleanup",
            "essential.deletetempfiles"
        };

        foreach (var id in expectedIrreversible)
        {
            var tweak = allTweaks.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(tweak);
            Assert.False(tweak.Reversible, $"Tweak {id} should have Reversible = false");
            Assert.False(string.IsNullOrWhiteSpace(tweak.IrreversibleReason), $"Tweak {id} must have non-empty IrreversibleReason");
        }

        // Validate that NO tweak in the entire catalog has Reversible == false with missing reason
        foreach (var tweak in allTweaks.Where(t => !t.Reversible))
        {
            Assert.False(string.IsNullOrWhiteSpace(tweak.IrreversibleReason),
                $"Tweak {tweak.Id} is marked irreversible but lacks IrreversibleReason.");
        }
    }

    [Fact]
    public async Task Cli_RefusesIrreversibleTweak_WithoutFlag()
    {
        bool prev = TestModeManager.IsTestMode;
        try
        {
            TestModeManager.IsTestMode = true;

            var originalOut = Console.Out;
            using var sw = new StringWriter();
            Console.SetOut(sw);

            try
            {
                int exitCode = await Program.Main(new[] { "--test-mode", "--tweaks", "advanced.removeedge" });
                string output = sw.ToString();

                Assert.Equal(1, exitCode);
                Assert.Contains("[ERROR] The following tweak(s) are irreversible", output);
                Assert.Contains("advanced.removeedge", output);
                Assert.Contains("--yes-irreversible", output);
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
    public async Task Cli_AppliesIrreversibleTweak_WithFlag()
    {
        bool prev = TestModeManager.IsTestMode;
        try
        {
            TestModeManager.IsTestMode = true;

            var originalOut = Console.Out;
            using var sw = new StringWriter();
            Console.SetOut(sw);

            try
            {
                int exitCode = await Program.Main(new[] { "--test-mode", "--tweaks", "advanced.removeedge", "--yes-irreversible" });
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

    [Fact]
    public async Task TweaksViewModel_ConfirmationModal_Flow()
    {
        var tweak = new TweakDefinition
        {
            Id = "test.irreversible",
            Name = "Permanent Test Removal",
            Category = "Advanced",
            Reversible = false,
            IrreversibleReason = "Permanent test deletion that cannot be restored."
        };

        var fakeTweakService = new FakeIrreversibleTweakService(new List<TweakDefinition> { tweak });
        var vm = new TweaksViewModel(fakeTweakService, new OperationRunner());

        // Initial state
        Assert.False(vm.IsIrreversibleDialogOpen);
        Assert.Empty(vm.IrreversibleWarningItems);

        // 1. Request confirmation and cancel
        var cancelTask = vm.RequestIrreversibleConfirmationAsync(new[] { tweak });
        Assert.True(vm.IsIrreversibleDialogOpen);
        Assert.Single(vm.IrreversibleWarningItems);
        Assert.Equal("test.irreversible", vm.IrreversibleWarningItems[0].Id);
        Assert.Equal("Permanent Test Removal", vm.IrreversibleWarningItems[0].Name);
        Assert.Equal("Permanent test deletion that cannot be restored.", vm.IrreversibleWarningItems[0].Reason);

        vm.CancelIrreversible();
        bool cancelResult = await cancelTask;
        Assert.False(cancelResult);
        Assert.False(vm.IsIrreversibleDialogOpen);

        // 2. Request confirmation and confirm
        var confirmTask = vm.RequestIrreversibleConfirmationAsync(new[] { tweak });
        Assert.True(vm.IsIrreversibleDialogOpen);

        vm.ConfirmIrreversible();
        bool confirmResult = await confirmTask;
        Assert.True(confirmResult);
        Assert.False(vm.IsIrreversibleDialogOpen);
    }

    [Fact]
    public async Task DashboardViewModel_ConfirmationModal_Flow()
    {
        var tweak = new TweakDefinition
        {
            Id = "test.dashboard.irreversible",
            Name = "Permanent Clean",
            Category = "Essential",
            Reversible = false,
            IrreversibleReason = "Deletes temp data forever."
        };

        var fakeReg = new FakeRegistryService();
        var fakeShell = new FakeShellService();
        var fakeTweakService = new FakeIrreversibleTweakService(new List<TweakDefinition> { tweak });
        var fakeRestore = new FakeRestoreService();
        var advisor = new Modules.Advisor.SmartAdvisorService(fakeReg, fakeShell);
        var memOpt = new Modules.Memory.MemoryOptimizerService();

        var vm = new DashboardViewModel(fakeTweakService, new OperationRunner(), fakeRestore, memOpt, advisor);

        // Initial state
        Assert.False(vm.IsIrreversibleDialogOpen);
        Assert.Empty(vm.IrreversibleWarningItems);

        // Request and cancel
        var cancelTask = vm.RequestIrreversibleConfirmationAsync(new[] { tweak });
        Assert.True(vm.IsIrreversibleDialogOpen);
        Assert.Single(vm.IrreversibleWarningItems);
        Assert.Equal("test.dashboard.irreversible", vm.IrreversibleWarningItems[0].Id);

        vm.CancelIrreversible();
        bool cancelResult = await cancelTask;
        Assert.False(cancelResult);
        Assert.False(vm.IsIrreversibleDialogOpen);

        // Request and confirm
        var confirmTask = vm.RequestIrreversibleConfirmationAsync(new[] { tweak });
        Assert.True(vm.IsIrreversibleDialogOpen);

        vm.ConfirmIrreversible();
        bool confirmResult = await confirmTask;
        Assert.True(confirmResult);
        Assert.False(vm.IsIrreversibleDialogOpen);
    }

    private static string? FindSolutionRoot()
    {
        string? current = AppContext.BaseDirectory;
        while (current != null)
        {
            if (File.Exists(Path.Combine(current, "Tweakora.sln")))
            {
                return current;
            }
            current = Directory.GetParent(current)?.FullName;
        }
        return null;
    }

    private sealed class FakeIrreversibleTweakService : ITweakService
    {
        private readonly List<TweakDefinition> _catalog;

        public FakeIrreversibleTweakService(List<TweakDefinition> catalog)
        {
            _catalog = catalog;
            BackupManager = new BackupManager(new FakeRegistryService(), new FakeServicesService(), new FakeRestoreService(), Path.GetTempPath());
        }

        public BackupManager BackupManager { get; }
        public IReadOnlyList<TweakDefinition> LoadCatalog() => _catalog;
        public bool DetectState(TweakDefinition tweak) => false;
        public Task<bool> DetectStateAsync(TweakDefinition tweak, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<TweakState> DetectTweakStateAsync(TweakDefinition tweak, CancellationToken cancellationToken = default) => Task.FromResult(TweakState.NotApplied);
        public Task<string> GenerateDryRunDiffAsync(IEnumerable<TweakDefinition> tweaks) => Task.FromResult(string.Empty);

        public Task<OperationExecutionReport> ApplyTweaksAsync(IReadOnlyList<TweakDefinition> tweaks, IOperationRunner runner, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new OperationExecutionReport
            {
                Title = "Apply",
                StartTime = DateTime.UtcNow,
                EndTime = DateTime.UtcNow,
                Results = tweaks.Select(t => new OperationItemResult
                {
                    Id = t.Id,
                    Name = t.Name,
                    Status = OperationItemStatus.Succeeded
                }).ToList()
            });
        }

        public Task<OperationExecutionReport> UndoTweaksAsync(IReadOnlyList<TweakDefinition> tweaks, IOperationRunner runner, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new OperationExecutionReport
            {
                Title = "Undo",
                StartTime = DateTime.UtcNow,
                EndTime = DateTime.UtcNow,
                Results = tweaks.Select(t => new OperationItemResult
                {
                    Id = t.Id,
                    Name = t.Name,
                    Status = OperationItemStatus.Restored
                }).ToList()
            });
        }

        public Task<bool> SetPerformancePlanAsync(string planName) => Task.FromResult(true);
        public Task<bool> SetDnsAsync(string primary, string secondary) => Task.FromResult(true);
    }
}
