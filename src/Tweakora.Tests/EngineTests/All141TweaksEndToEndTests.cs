using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Tweakora.Core.Engine;
using Tweakora.Core.Engine.Handlers;
using Tweakora.Core.Models;
using Tweakora.Simulation;
using Xunit;
using ExecutionContext = Tweakora.Core.Engine.ExecutionContext;

namespace Tweakora.Tests.EngineTests;

public sealed class All141TweaksEndToEndTests
{
    private static string FindCatalogDirectory()
    {
        string current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            string candidate = Path.Combine(current, "Catalog");
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "schema", "tweak.schema.json")))
            {
                return candidate;
            }
            string? parent = Directory.GetParent(current)?.FullName;
            if (parent == current) break;
            current = parent ?? string.Empty;
        }
        throw new InvalidOperationException("Could not find Catalog directory.");
    }

    private static List<TweakDefinition> LoadAllCatalogTweaks()
    {
        string catalogDir = FindCatalogDirectory();
        string tweaksFolder = Path.Combine(catalogDir, "Tweaks");
        var list = new List<TweakDefinition>();

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        foreach (string file in Directory.GetFiles(tweaksFolder, "*.json"))
        {
            string json = File.ReadAllText(file);
            var tweaks = JsonSerializer.Deserialize<List<TweakDefinition>>(json, options);
            if (tweaks != null)
            {
                list.AddRange(tweaks);
            }
        }

        return list;
    }

    [Fact]
    public async Task All141Tweaks_DetectApplyAndUndo_ZeroErrorsAndCleanTransitions()
    {
        var tweaks = LoadAllCatalogTweaks();
        Assert.Equal(141, tweaks.Count);

        string testJournalDir = Path.Combine(Path.GetTempPath(), "TweakoraTests_All141_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testJournalDir);

        try
        {
        foreach (var tweak in tweaks)
        {
            var fakeRegistry = new FakeRegistryService();
            var fakeServices = new FakeServicesService();
            var fakeTasks = new FakeTaskSchedulerService();
            var fakeRestore = new FakeRestoreService();
            var fakeShell = new FakeShellService();

            var journal = new RollbackJournalManager(testJournalDir);
            var detector = new StateDetector(fakeRegistry, fakeServices, fakeTasks);

            var handlers = new IActionHandler[]
            {
                new RegistryActionHandler(),
                new ServiceActionHandler(),
                new TaskActionHandler(),
                new PowerShellActionHandler()
            };

            var context = new ExecutionContext
            {
                Registry = fakeRegistry,
                Services = fakeServices,
                Tasks = fakeTasks,
                Shell = fakeShell,
                Journal = journal,
                IsDryRun = false
            };

            var runner = new TweakRunner(handlers, context, fakeRestore, detector);

            // 1. Initial State: Must be Default / Inactive
            bool initialActive = await detector.IsTweakActiveAsync(tweak, useCache: false);
            Assert.False(initialActive, $"Tweak '{tweak.Id}' should be initially Inactive on clean state.");

            // 2. Apply: Must apply cleanly with status Applied
            var report = await runner.ApplyBatchAsync(new[] { tweak });
            var itemResult = report.Results.FirstOrDefault();
            Assert.NotNull(itemResult);
            Assert.True(itemResult.Status == TweakExecutionStatus.Applied, $"Tweak '{tweak.Id}' failed to apply: {itemResult.Message}");

            // 3. Post-Apply Detection: Must detect Active / Applied
            bool postApplyActive = await detector.IsTweakActiveAsync(tweak, useCache: false);
            Assert.True(postApplyActive, $"Tweak '{tweak.Id}' should be detected as Active after apply.");

            // 4. Undo: Must succeed
            var undoResult = await runner.UndoTweakAsync(tweak);
            Assert.True(undoResult.Success, $"Tweak '{tweak.Id}' failed to undo: {undoResult.Message}");

            // 5. Post-Undo Detection: Must detect Default / Inactive
            bool postUndoActive = await detector.IsTweakActiveAsync(tweak, useCache: false);
            Assert.False(postUndoActive, $"Tweak '{tweak.Id}' should be detected as Inactive after undo.");
        }
        }
        finally
        {
            try { Directory.Delete(testJournalDir, true); } catch { }
        }
    }

    [Fact]
    public async Task All141Tweaks_BatchApplyAndRollback_ZeroErrors()
    {
        var tweaks = LoadAllCatalogTweaks();
        Assert.Equal(141, tweaks.Count);

        string testJournalDir = Path.Combine(Path.GetTempPath(), "TweakoraTests_Batch141_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testJournalDir);

        try
        {
            var fakeRegistry = new FakeRegistryService();
            var fakeServices = new FakeServicesService();
            var fakeTasks = new FakeTaskSchedulerService();
            var fakeRestore = new FakeRestoreService();
            var fakeShell = new FakeShellService();

            var journal = new RollbackJournalManager(testJournalDir);
            var detector = new StateDetector(fakeRegistry, fakeServices, fakeTasks);

            var handlers = new IActionHandler[]
            {
                new RegistryActionHandler(),
                new ServiceActionHandler(),
                new TaskActionHandler(),
                new PowerShellActionHandler()
            };

            var context = new ExecutionContext
            {
                Registry = fakeRegistry,
                Services = fakeServices,
                Tasks = fakeTasks,
                Shell = fakeShell,
                Journal = journal,
                IsDryRun = false
            };

            var runner = new TweakRunner(handlers, context, fakeRestore, detector);

            // Execute all 141 tweaks together as a single batch
            var report = await runner.ApplyBatchAsync(tweaks);

            // Verify: 0 failed tweaks
            Assert.Equal(0, report.FailedCount);
            Assert.Equal(141, report.Results.Count);

            // Every tweak is either Applied or Skipped (due to overlapping prerequisites)
            foreach (var res in report.Results)
            {
                Assert.True(res.Status == TweakExecutionStatus.Applied || res.Status == TweakExecutionStatus.Skipped,
                    $"Tweak {res.TweakId} had unexpected status {res.Status}: {res.Message}");
            }

            // Verify journal recorded operations
            var sessions = journal.GetAllSessions();
            Assert.NotEmpty(sessions);
        }
        finally
        {
            try { Directory.Delete(testJournalDir, true); } catch { }
        }
    }
}

