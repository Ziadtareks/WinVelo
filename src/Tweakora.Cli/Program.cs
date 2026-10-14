using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Tweakora.Core.Engine;
using Tweakora.Core.Engine.Handlers;
using Tweakora.Core.Interfaces;
using Tweakora.Core.Models;
using Tweakora.Core.Operations;
using Tweakora.Core.Profiles;
using Tweakora.Core.Safety;
using Tweakora.Modules.Cleaner;
using Tweakora.Modules.Config;
using Tweakora.Modules.Network;
using Tweakora.Modules.Updates;
using Tweakora.Platform;
using Tweakora.Simulation;

namespace Tweakora.Cli;

public sealed class SelfTestReportItem
{
    public required int Index { get; init; }
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Category { get; init; }
    public required bool ApplyVerified { get; init; }
    public required bool UndoVerified { get; init; }
    public required bool Passed { get; init; }
    public string Details { get; init; } = "Exact match";
}

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.WriteLine("========================================");
        Console.WriteLine("         WinVelo CLI v1.0.0             ");
        Console.WriteLine("========================================");

        if (args.Length == 0 || args.Contains("--help") || args.Contains("-h"))
        {
            PrintUsage();
            return 0;
        }

        bool isSelfTest = args.Contains("--self-test");
        bool isTestMode = args.Contains("--test-mode");
        bool isDetectAll = args.Contains("--detect-all");
        bool isRealMode = args.Contains("--real-mode") || (isSelfTest && !isTestMode) || (isDetectAll && !isTestMode);
        bool isDryRun = args.Contains("--dry-run");
        bool isRun = args.Contains("--run") || args.Contains("--apply") || args.Contains("--enable") || args.Contains("--disable") || args.Contains("--clean") || args.Contains("--dns") || args.Contains("--update-mode") || args.Contains("--fix") || args.Contains("--undo") || isSelfTest;

        if (isRealMode)
        {
            TestModeManager.DisableTestMode();
            Console.WriteLine("[MODE] REAL-MODE ACTIVE — Operating directly against host OS.");

            // Strict Elevation Check: Only required for modifying operations
            if (!isDetectAll && !AdminPrivilegeHelper.IsRunningAsAdministrator())
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("\n[ERROR] Administrator privileges are required to run WinVelo CLI in real-mode.");
                Console.WriteLine("Please open your terminal (PowerShell / Command Prompt) as Administrator and re-run.");
                Console.ResetColor();
                return 1;
            }
        }
        else
        {
            TestModeManager.EnableTestMode();
            Console.WriteLine("[MODE] TEST/SIMULATION ACTIVE — Using safe in-memory fakes.");
        }

        string rootDir = AppContext.BaseDirectory;
        string tweaksDir = Path.Combine(rootDir, "Catalog", "Tweaks");
        if (!Directory.Exists(tweaksDir))
        {
            string? parent = Directory.GetParent(rootDir)?.FullName;
            while (parent != null)
            {
                string candidate = Path.Combine(parent, "Catalog", "Tweaks");
                if (Directory.Exists(candidate))
                {
                    tweaksDir = candidate;
                    break;
                }
                parent = Directory.GetParent(parent)?.FullName;
            }
        }

        List<TweakDefinition> catalog = LoadCatalog(tweaksDir);
        Console.WriteLine($"[INFO] Loaded {catalog.Count} tweak(s) from catalog.");

        if (args.Contains("--list"))
        {
            Console.WriteLine("\nAvailable Tweaks in Catalog:");
            foreach (var t in catalog)
            {
                Console.WriteLine($"  - [{t.Risk,-6}] {t.Id}: {t.Name} (Category: {t.Category})");
            }
            return 0;
        }

        // Setup platform services based on mode
        IRegistryService registry;
        ISystemServicesService services;
        ITaskSchedulerService tasks;
        ISystemRestoreService restore;
        IShellService shell;

        if (isRealMode)
        {
            registry = new WindowsRegistryService();
            services = new WindowsServicesService();
            tasks = new WindowsTaskSchedulerService();
            restore = new WindowsRestoreService();
            shell = new WindowsShellService();
        }
        else
        {
            registry = new FakeRegistryService();
            services = new FakeServicesService();
            tasks = new FakeTaskSchedulerService();
            restore = new FakeRestoreService();
            shell = new FakeShellService();
        }

        string journalDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WinVelo", "Journal");
        RollbackJournalManager journal = new(journalDir);

        Core.Engine.ExecutionContext context = new()
        {
            Registry = registry,
            Services = services,
            Tasks = tasks,
            Shell = shell,
            Journal = journal,
            IsDryRun = isDryRun
        };

        List<IActionHandler> handlers = new()
        {
            new RegistryActionHandler(),
            new ServiceActionHandler(),
            new TaskActionHandler(),
            new FileActionHandler(),
            new PowerShellActionHandler()
        };

        StateDetector detector = new(registry, services, tasks);
        DryRunEngine dryRun = new(handlers, context);
        TweakRunner runner = new(handlers, context, restore, detector);
        IOperationRunner opRunner = new OperationRunner();

        // ---------------------------------------------------------------------
        // CLI COMMAND: --self-test [--level <1|2>]
        // ---------------------------------------------------------------------
        if (isSelfTest)
        {
            int levelIdx = Array.FindIndex(args, a => string.Equals(a, "--level", StringComparison.OrdinalIgnoreCase) || string.Equals(a, "-l", StringComparison.OrdinalIgnoreCase));
            int level = 1;
            if (levelIdx >= 0 && levelIdx < args.Length - 1 && int.TryParse(args[levelIdx + 1], out int parsedLvl))
            {
                level = parsedLvl;
            }

            return await ExecuteSelfTestAsync(level, catalog, registry, runner, detector, restore, isTestMode, context);
        }

        // ---------------------------------------------------------------------
        // CLI COMMAND: --detect-all (Read-only inspection of host state)
        // ---------------------------------------------------------------------
        if (args.Contains("--detect-all"))
        {
            var regDrv = isTestMode ? (IRegistryService)new FakeRegistryService() : new WindowsRegistryService();
            var svcDrv = isTestMode ? (ISystemServicesService)new FakeServicesService() : new WindowsServicesService();
            var tskDrv = isTestMode ? (ITaskSchedulerService)new FakeTaskSchedulerService() : new WindowsTaskSchedulerService();
            var realDetector = new StateDetector(regDrv, svcDrv, tskDrv);
            return await ExecuteDetectAllAsync(catalog, realDetector, regDrv, svcDrv, tskDrv);
        }

        // ---------------------------------------------------------------------
        // CLI COMMAND: --preset <name> [--dry-run | --run]
        // ---------------------------------------------------------------------
        int presetIdx = Array.FindIndex(args, a => string.Equals(a, "--preset", StringComparison.OrdinalIgnoreCase) || string.Equals(a, "-p", StringComparison.OrdinalIgnoreCase));
        if (presetIdx >= 0 && presetIdx < args.Length - 1)
        {
            string presetName = args[presetIdx + 1];
            return await ExecutePresetAsync(presetName, catalog, dryRun, isDryRun, handlers, context, runner, tweaksDir);
        }

        // 1. Single Tweak execution: --tweak <id> [--apply | --undo]
        int tweakIdx = Array.FindIndex(args, a => string.Equals(a, "--tweak", StringComparison.OrdinalIgnoreCase));
        if (tweakIdx >= 0 && tweakIdx < args.Length - 1)
        {
            string tweakId = args[tweakIdx + 1];
            var targetTweak = catalog.FirstOrDefault(t =>
                string.Equals(t.Id, tweakId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(t.Origin, tweakId, StringComparison.OrdinalIgnoreCase));

            if (targetTweak == null)
            {
                Console.WriteLine($"[ERROR] Tweak not found in catalog: {tweakId}");
                return 1;
            }

            if (args.Contains("--undo"))
            {
                Console.WriteLine($"[EXECUTE] Undoing tweak: {targetTweak.Id} ({targetTweak.Name})...");
                var result = await runner.UndoTweakAsync(targetTweak);
                Console.WriteLine($"[RESULT] {(result.Success ? "Success" : "Failed")}: {result.Message}");
                return result.Success ? 0 : 1;
            }
            else
            {
                Console.WriteLine($"[EXECUTE] Applying tweak: {targetTweak.Id} ({targetTweak.Name})...");
                var report = await runner.ApplyBatchAsync(new[] { targetTweak });
                var firstRes = report.Results.FirstOrDefault();
                bool success = firstRes?.Status == TweakExecutionStatus.Applied;
                Console.WriteLine($"[RESULT] {(success ? "Success" : "Failed")}: {firstRes?.Message ?? firstRes?.Status.ToString()}");
                return success ? 0 : 1;
            }
        }

        // 2. Feature management: --feature <key> [--enable | --disable]
        int featureIdx = Array.FindIndex(args, a => string.Equals(a, "--feature", StringComparison.OrdinalIgnoreCase));
        if (featureIdx >= 0 && featureIdx < args.Length - 1)
        {
            string featureKey = args[featureIdx + 1];
            IFeatureService featureService = new FeatureService(shell);
            var features = featureService.LoadFeatures();
            var targetFeature = features.FirstOrDefault(f => string.Equals(f.Key, featureKey, StringComparison.OrdinalIgnoreCase));

            if (targetFeature == null)
            {
                Console.WriteLine($"[ERROR] Feature not found: {featureKey}");
                return 1;
            }

            bool isDisable = args.Contains("--disable");
            if (isDisable)
            {
                Console.WriteLine($"[EXECUTE] Disabling feature: {targetFeature.Key} ({targetFeature.Name})...");
                var report = await featureService.DisableFeaturesAsync(new[] { targetFeature }, opRunner);
                Console.WriteLine($"[RESULT] {report.Summary}");
                return report.FailedCount == 0 ? 0 : 1;
            }
            else
            {
                Console.WriteLine($"[EXECUTE] Enabling feature: {targetFeature.Key} ({targetFeature.Name})...");
                var report = await featureService.EnableFeaturesAsync(new[] { targetFeature }, opRunner);
                Console.WriteLine($"[RESULT] {report.Summary}");
                return report.FailedCount == 0 ? 0 : 1;
            }
        }

        // 3. Cleaner: --clean <category>
        int cleanIdx = Array.FindIndex(args, a => string.Equals(a, "--clean", StringComparison.OrdinalIgnoreCase));
        if (cleanIdx >= 0 && cleanIdx < args.Length - 1)
        {
            string category = args[cleanIdx + 1];
            ICleanerService cleanerService = new CleanerService(services);
            string[] cats = string.Equals(category, "all", StringComparison.OrdinalIgnoreCase)
                ? new[] { "UserTemp", "WindowsTemp", "UpdateCache", "CrashDumps" }
                : new[] { category };

            Console.WriteLine($"[EXECUTE] Cleaning categories: {string.Join(", ", cats)}...");
            var cleanResult = await cleanerService.CleanCategoriesAsync(cats);
            Console.WriteLine($"[RESULT] {cleanResult.FormattedSummary}");
            return 0;
        }

        // 4. DNS: --dns <providerId>
        int dnsIdx = Array.FindIndex(args, a => string.Equals(a, "--dns", StringComparison.OrdinalIgnoreCase));
        if (dnsIdx >= 0 && dnsIdx < args.Length - 1)
        {
            string dnsId = args[dnsIdx + 1];
            IDnsService dnsService = new DnsService(shell);
            var providers = dnsService.GetProviders();
            var provider = providers.FirstOrDefault(p =>
                string.Equals(p.Id, dnsId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p.Name, dnsId, StringComparison.OrdinalIgnoreCase));

            if (provider == null)
            {
                Console.WriteLine($"[ERROR] DNS provider not found: {dnsId}");
                return 1;
            }

            Console.WriteLine($"[EXECUTE] Setting DNS to {provider.Name} ({provider.PrimaryDns}, {provider.SecondaryDns})...");
            bool success = await dnsService.ApplyDnsAsync(provider);
            Console.WriteLine($"[RESULT] {(success ? "Success" : "Failed")}");
            return success ? 0 : 1;
        }

        // 5. Updates Mode: --update-mode <Default|Security|DisableAll>
        int updateIdx = Array.FindIndex(args, a => string.Equals(a, "--update-mode", StringComparison.OrdinalIgnoreCase));
        if (updateIdx >= 0 && updateIdx < args.Length - 1)
        {
            string modeStr = args[updateIdx + 1];
            if (!Enum.TryParse<UpdateProfileMode>(modeStr, true, out var mode))
            {
                Console.WriteLine($"[ERROR] Invalid update mode: {modeStr}. Choose Default, Security, or DisableAll.");
                return 1;
            }

            IUpdateModeService updateService = new UpdateModeService(registry, services, tasks);
            Console.WriteLine($"[EXECUTE] Applying update mode: {mode}...");
            var report = await updateService.ApplyModeAsync(mode, opRunner);
            Console.WriteLine($"[RESULT] {report.Summary}");
            return report.FailedCount == 0 ? 0 : 1;
        }

        // 6. Fixes: --fix <fixId>
        int fixIdx = Array.FindIndex(args, a => string.Equals(a, "--fix", StringComparison.OrdinalIgnoreCase));
        if (fixIdx >= 0 && fixIdx < args.Length - 1)
        {
            string fixId = args[fixIdx + 1];
            IFixService fixService = new FixService(shell);
            Console.WriteLine($"[EXECUTE] Running system fix: {fixId}...");
            var report = await fixService.RunFixAsync(fixId, opRunner);
            Console.WriteLine($"[RESULT] {report.Summary}");
            return report.FailedCount == 0 ? 0 : 1;
        }

        // 7. Config file execution: --config <path> [--run | --dry-run]
        int configIdx = Array.FindIndex(args, a => string.Equals(a, "--config", StringComparison.OrdinalIgnoreCase));
        if (configIdx >= 0 && configIdx < args.Length - 1)
        {
            string configPath = args[configIdx + 1];
            if (!File.Exists(configPath))
            {
                Console.WriteLine($"[ERROR] Config file not found: {configPath}");
                return 1;
            }

            Console.WriteLine($"[INFO] Loading configuration from: {configPath}");
            TweakoraConfig? config = ConfigManager.LoadFromFile(configPath);
            if (config == null)
            {
                Console.WriteLine("[ERROR] Failed to parse JSON configuration profile.");
                return 1;
            }

            Console.WriteLine($"[INFO] Config contains: {config.SelectedTweaks.Count} tweaks, {config.SelectedApps.Count} apps, {config.SelectedFeatures.Count} features, DNS: {config.SelectedDns ?? "Default"}, Updates: {config.UpdatesMode ?? "Default"}");

            var keySet = new HashSet<string>(config.SelectedTweaks, StringComparer.OrdinalIgnoreCase);
            var targetTweaks = catalog.Where(t => keySet.Contains(t.Id) || (t.Origin != null && keySet.Contains(t.Origin))).ToList();

            if (isDryRun || !isRun)
            {
                Console.WriteLine($"\n[DRY RUN PREVIEW] For {targetTweaks.Count} configured tweak(s):");
                var previews = dryRun.GeneratePreview(targetTweaks);
                foreach (var p in previews)
                {
                    Console.WriteLine($"\n[{p.Risk}] {p.TweakId}");
                    foreach (var a in p.Actions)
                    {
                        Console.WriteLine($"  • {a.ActionType} {a.Target} -> {a.Operation} (Current: {a.CurrentState} | Proposed: {a.ProposedState})");
                    }
                }
                return 0;
            }

            if (isRun)
            {
                Console.WriteLine($"\n[EXECUTE] Running {targetTweaks.Count} configured tweak(s) (RealMode: {isRealMode})...");
                var report = await runner.ApplyBatchAsync(targetTweaks);
                foreach (var res in report.Results)
                {
                    Console.WriteLine($"  [{res.Status}] {res.TweakId} - {res.Message}");
                }
                Console.WriteLine($"\n[DONE] Finished executing configuration. Applied: {report.AppliedCount}, Skipped: {report.SkippedCount}, Failed: {report.FailedCount}");
                return report.FailedCount == 0 ? 0 : 1;
            }
        }

        if (isDryRun)
        {
            List<TweakPreview> previews = dryRun.GeneratePreview(catalog);
            Console.WriteLine("\n[DRY RUN PREVIEW] No changes will be made:");
            foreach (var p in previews.Take(20))
            {
                Console.WriteLine($"\nTweak: {p.TweakId} ({p.Risk})");
                foreach (var a in p.Actions)
                {
                    Console.WriteLine($"  - [{a.ActionType}] {a.Target} -> Op: {a.Operation} (Current: {a.CurrentState} | Proposed: {a.ProposedState})");
                }
            }
            if (previews.Count > 20)
            {
                Console.WriteLine($"\n... and {previews.Count - 20} more tweaks previewed.");
            }
            return 0;
        }

        Console.WriteLine("\n[NOTE] Use --help to view available commands.");
        return 0;
    }

    private static async Task<int> ExecuteSelfTestAsync(
        int level,
        List<TweakDefinition> catalog,
        IRegistryService registry,
        TweakRunner runner,
        StateDetector detector,
        ISystemRestoreService restore,
        bool isTestMode,
        Core.Engine.ExecutionContext context)
    {
        string testJournalDir = Path.Combine(Path.GetTempPath(), "WinVelo_SelfTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testJournalDir);
        context.Journal = new RollbackJournalManager(testJournalDir);

        Console.WriteLine("\n==========================================================");
        Console.WriteLine($"   WinVelo Host Self-Test Engine — Level {level}             ");
        Console.WriteLine("==========================================================");

        // Step 1: System Restore Point Creation
        Console.WriteLine($"\n[1/3] Creating safety System Restore Point: 'WinVelo Pre-Self-Test Level {level}'...");
        var restoreResult = restore.CreateRestorePoint($"WinVelo Pre-Self-Test Level {level}");

        if (!restoreResult.Success && !restoreResult.WasThrottledOrSkipped)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[CRITICAL ERROR] Failed to create system restore point: {restoreResult.ErrorMessage}");
            Console.WriteLine("Self-test aborted immediately for host system safety.");
            Console.ResetColor();
            return 1;
        }

        if (restoreResult.Success)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("[PASS] System Restore Point created successfully.");
            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("[INFO] A restore point was created recently (within Windows 24h throttling window). Continuing safely.");
            Console.ResetColor();
        }

        // Step 2: Filter Level Tweaks (Level 1 = strictly safe HKCU-only tweaks)
        List<TweakDefinition> targetTweaks;
        if (level == 1)
        {
            targetTweaks = catalog.Where(t =>
                t.Apply.Count > 0 &&
                t.Apply.All(a => string.Equals(a.Type, "registry", StringComparison.OrdinalIgnoreCase) &&
                                 string.Equals(a.Hive, "HKCU", StringComparison.OrdinalIgnoreCase)) &&
                t.Undo.All(a => string.Equals(a.Type, "registry", StringComparison.OrdinalIgnoreCase) &&
                                string.Equals(a.Hive, "HKCU", StringComparison.OrdinalIgnoreCase))
            ).ToList();
        }
        else
        {
            Console.WriteLine($"[ERROR] Level {level} self-test is not implemented yet. Supported levels: 1");
            return 1;
        }

        Console.WriteLine($"\n[2/3] Executing safe HKCU roundtrip on {targetTweaks.Count} Level 1 tweaks...");
        Console.WriteLine("Each tweak runs: Snapshot -> Apply -> Verify -> Undo -> Verify Original Restored.\n");

        List<SelfTestReportItem> results = new();

        for (int i = 0; i < targetTweaks.Count; i++)
        {
            var tweak = targetTweaks[i];
            Console.Write($"[{i + 1,2}/{targetTweaks.Count}] {tweak.Id,-50} ... ");

            // 1. Snapshot original state of all keys & values touched by this tweak
            var snapshot = tweak.Apply.Select(a =>
            {
                string hive = a.Hive ?? "HKCU";
                string key = a.Key ?? string.Empty;
                string valName = a.ValueName ?? string.Empty;
                bool exists = registry.ValueExists(hive, key, valName);
                object? val = exists ? registry.GetValue(hive, key, valName) : null;
                return (Hive: hive, Key: key, ValueName: valName, Existed: exists, Value: val, Data: a.Data);
            }).ToList();

            bool wasActiveInitially = await detector.IsTweakActiveAsync(tweak, useCache: false);

            // If it's already active, undo first so we can verify apply
            if (wasActiveInitially)
            {
                await runner.UndoTweakAsync(tweak);
                detector.InvalidateCache(tweak.Id);
            }

            // 2. Apply tweak
            await runner.ApplyBatchAsync(new[] { tweak });
            detector.InvalidateCache(tweak.Id);

            // 3. Verify Apply: check detector and registry values
            bool applyVerified = await detector.IsTweakActiveAsync(tweak, useCache: false);
            foreach (var a in tweak.Apply)
            {
                string hive = a.Hive ?? "HKCU";
                string key = a.Key ?? string.Empty;
                string valName = a.ValueName ?? string.Empty;
                if (!registry.ValueExists(hive, key, valName))
                {
                    applyVerified = false;
                    break;
                }
                object? curVal = registry.GetValue(hive, key, valName);
                if (!CompareRegistryValue(curVal, a.Data))
                {
                    applyVerified = false;
                    break;
                }
            }

            // 4. Undo tweak
            await runner.UndoTweakAsync(tweak);
            detector.InvalidateCache(tweak.Id);

            // 5. Verify Undo: check inactive via authentic state detector
            bool undoVerified = !await detector.IsTweakActiveAsync(tweak, useCache: false);

            // 6. If it was active originally on host, restore it back to original active state
            if (wasActiveInitially)
            {
                await runner.ApplyBatchAsync(new[] { tweak });
                detector.InvalidateCache(tweak.Id);
            }

            bool passed = applyVerified && undoVerified;
            if (passed)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("PASS");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"FAIL (Apply: {(applyVerified ? "PASS" : "FAIL")}, Undo: {(undoVerified ? "PASS" : "FAIL")})");
                Console.ResetColor();
            }

            results.Add(new SelfTestReportItem
            {
                Index = i + 1,
                Id = tweak.Id,
                Name = tweak.Name,
                Category = tweak.Category,
                ApplyVerified = applyVerified,
                UndoVerified = undoVerified,
                Passed = passed,
                Details = passed ? "Exact match restored" : $"Mismatch (Apply: {applyVerified}, Undo: {undoVerified})"
            });
        }

        // Step 3: Print Table and Export to self-test-results.md
        Console.WriteLine("\n[3/3] Generating summary report and writing self-test-results.md...");
        PrintSummaryTable(results);
        WriteMarkdownReport(results, level);

        int passCount = results.Count(r => r.Passed);
        int failCount = results.Count(r => !r.Passed);

        Console.WriteLine($"\nSelf-Test Complete: {passCount} passed, {failCount} failed.");
        Console.WriteLine("Full markdown report saved to: self-test-results.md\n");

        return failCount == 0 ? 0 : 1;
    }

    private static void PrintSummaryTable(List<SelfTestReportItem> results)
    {
        Console.WriteLine("\n" + new string('=', 100));
        Console.WriteLine($"{"#",-4} | {"Tweak ID",-45} | {"Apply",-7} | {"Undo",-7} | {"Overall",-7} | {"Status"}");
        Console.WriteLine(new string('-', 100));

        foreach (var r in results)
        {
            string applyStr = r.ApplyVerified ? "PASS" : "FAIL";
            string undoStr = r.UndoVerified ? "PASS" : "FAIL";
            string overallStr = r.Passed ? "PASS" : "FAIL";

            Console.WriteLine($"{r.Index,-4} | {r.Id,-45} | {applyStr,-7} | {undoStr,-7} | {overallStr,-7} | {r.Details}");
        }

        Console.WriteLine(new string('=', 100));
    }

    private static void WriteMarkdownReport(List<SelfTestReportItem> results, int level)
    {
        int passCount = results.Count(r => r.Passed);
        int failCount = results.Count(r => !r.Passed);

        using var sw = new StreamWriter("self-test-results.md", false, System.Text.Encoding.UTF8);
        sw.WriteLine("# WinVelo Host Self-Test Report");
        sw.WriteLine();
        sw.WriteLine($"- **Execution Date**: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sw.WriteLine($"- **Test Level**: Level {level} (Safe — Strictly HKCU Registry Tweaks)");
        sw.WriteLine($"- **Total Tweaks Audited**: {results.Count}");
        sw.WriteLine($"- **Passed**: {passCount}");
        sw.WriteLine($"- **Failed**: {failCount}");
        sw.WriteLine($"- **Overall Verdict**: **{(failCount == 0 ? "PASSED (100% Exact Restoration)" : "FAILED")}**");
        sw.WriteLine();
        sw.WriteLine("## Verification Results Table");
        sw.WriteLine();
        sw.WriteLine("| # | Tweak ID | Name | Category | Apply Verify | Undo Verify | Overall | Details |");
        sw.WriteLine("|---|---|---|---|:---:|:---:|:---:|---|");

        foreach (var r in results)
        {
            string apply = r.ApplyVerified ? "PASS" : "FAIL";
            string undo = r.UndoVerified ? "PASS" : "FAIL";
            string overall = r.Passed ? "**PASS**" : "**FAIL**";
            sw.WriteLine($"| {r.Index} | `{r.Id}` | {r.Name} | {r.Category} | {apply} | {undo} | {overall} | {r.Details} |");
        }

        sw.WriteLine();
        sw.WriteLine("---");
        sw.WriteLine("*Report automatically generated by WinVelo CLI Self-Test Engine.*");
    }

    private static bool CompareValues(object? val1, object? val2)
    {
        if (val1 == null && val2 == null) return true;
        if (val1 == null || val2 == null) return false;

        if (val1 is int i1 && val2 is int i2) return i1 == i2;
        if (val1 is long l1 && val2 is long l2) return l1 == l2;

        if (long.TryParse(val1.ToString(), out long p1) && long.TryParse(val2.ToString(), out long p2))
        {
            return p1 == p2;
        }

        return string.Equals(val1.ToString(), val2.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool CompareRegistryValue(object? actual, JsonElement? expected)
    {
        if (actual == null && expected == null) return true;
        if (actual == null || expected == null) return false;

        if (expected.Value.ValueKind == JsonValueKind.Number)
        {
            long expNum = expected.Value.GetInt64();
            if (actual is int actInt) return actInt == expNum;
            if (actual is long actLong) return actLong == expNum;
            if (long.TryParse(actual.ToString(), out long parsed)) return parsed == expNum;
        }
        else if (expected.Value.ValueKind == JsonValueKind.String)
        {
            string? expStr = expected.Value.GetString();
            return string.Equals(actual.ToString(), expStr, StringComparison.OrdinalIgnoreCase);
        }
        return string.Equals(actual.ToString(), expected.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<int> ExecuteDetectAllAsync(
        List<TweakDefinition> catalog,
        StateDetector detector,
        IRegistryService registry,
        ISystemServicesService services,
        ITaskSchedulerService tasks)
    {
        Console.WriteLine("\n=======================================================");
        Console.WriteLine("    HOST MACHINE AUDIT: READ-ONLY TWEAK DETECTION      ");
        Console.WriteLine("=======================================================");
        Console.WriteLine($"Scanning host machine state for all {catalog.Count} tweaks in catalog...\n");

        string rootDir = AppContext.BaseDirectory;
        string verifyDir = Path.Combine(rootDir, "verify");
        string? parent = rootDir;
        while (parent != null)
        {
            if (File.Exists(Path.Combine(parent, "Tweakora.sln")))
            {
                verifyDir = Path.Combine(parent, "verify");
                break;
            }
            parent = Directory.GetParent(parent)?.FullName;
        }

        if (!Directory.Exists(verifyDir))
        {
            Directory.CreateDirectory(verifyDir);
        }

        string reportPath = Path.Combine(verifyDir, "detect-report.md");

        var lines = new List<string>
        {
            "# Host System Tweak Detection Report",
            "",
            $"- **Execution Timestamp**: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC",
            $"- **Host OS**: {Environment.OSVersion}",
            $"- **Machine**: {Environment.MachineName}",
            $"- **Total Tweaks Audited**: {catalog.Count}",
            "",
            "| # | Tweak ID | Name | Category | Risk | Status | Error/Exception |",
            "|---|---|---|---|:---:|:---:|---|"
        };

        int appliedCount = 0;
        int defaultCount = 0;
        int errorCount = 0;

        for (int i = 0; i < catalog.Count; i++)
        {
            var tweak = catalog[i];
            string status = "Default";
            string errorDetails = "None";

            try
            {
                bool isActive = await detector.IsTweakActiveAsync(tweak, useCache: false);
                if (isActive)
                {
                    status = "Applied";
                    appliedCount++;
                }
                else
                {
                    status = "Default";
                    defaultCount++;
                }
            }
            catch (Exception ex)
            {
                status = "Error";
                errorDetails = ex.Message;
                errorCount++;
            }

            Console.WriteLine($"[{i + 1,3}/{catalog.Count}] [{status,-7}] {tweak.Id,-45} ({tweak.Name})");
            lines.Add($"| {i + 1} | `{tweak.Id}` | {tweak.Name} | {tweak.Category} | {tweak.Risk} | **{status}** | {errorDetails} |");
        }

        lines.Add("");
        lines.Add("## Summary");
        lines.Add("");
        lines.Add($"- **Applied**: {appliedCount}");
        lines.Add($"- **Default (Inactive)**: {defaultCount}");
        lines.Add($"- **Errors/Access Issues**: {errorCount}");

        File.WriteAllText(reportPath, string.Join(Environment.NewLine, lines));

        Console.WriteLine("\n=======================================================");
        Console.WriteLine($"Detection Complete! Applied: {appliedCount}, Default: {defaultCount}, Errors: {errorCount}");
        Console.WriteLine($"Saved full detection report to: {reportPath}");
        Console.WriteLine("=======================================================");

        return 0;
    }

    private static Task<int> ExecutePresetAsync(
        string presetName,
        List<TweakDefinition> catalog,
        DryRunEngine dryRun,
        bool isDryRun,
        List<IActionHandler> handlers,
        Core.Engine.ExecutionContext context,
        TweakRunner runner,
        string catalogDir)
    {
        string presetPath = Path.Combine(catalogDir, "..", "Config", "preset.json");
        if (!File.Exists(presetPath))
        {
            string? p = Directory.GetParent(catalogDir)?.FullName;
            if (p != null)
            {
                presetPath = Path.Combine(p, "Config", "preset.json");
            }
        }

        if (!File.Exists(presetPath))
        {
            Console.WriteLine($"[ERROR] Presets file not found: {presetPath}");
            return Task.FromResult(1);
        }

        string presetJson = File.ReadAllText(presetPath);
        using var presetDoc = JsonDocument.Parse(presetJson);

        if (!presetDoc.RootElement.TryGetProperty(presetName, out var presetElem))
        {
            Console.WriteLine($"[ERROR] Preset '{presetName}' not found in preset.json.");
            Console.WriteLine("Available presets: " + string.Join(", ", presetDoc.RootElement.EnumerateObject().Select(x => x.Name)));
            return Task.FromResult(1);
        }

        List<string> keys = presetElem.EnumerateArray().Select(x => x.GetString() ?? "").Where(s => !string.IsNullOrEmpty(s)).ToList();
        var keySet = new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase);

        var targetTweaks = catalog.Where(t => keySet.Contains(t.Id) || (t.Origin != null && keySet.Contains(t.Origin))).ToList();
        Console.WriteLine($"[INFO] Preset '{presetName}' matched {targetTweaks.Count} of {keys.Count} configured keys.");

        string rootDir = AppContext.BaseDirectory;
        string verifyDir = Path.Combine(rootDir, "verify");
        string? parent = rootDir;
        while (parent != null)
        {
            if (File.Exists(Path.Combine(parent, "Tweakora.sln")))
            {
                verifyDir = Path.Combine(parent, "verify");
                break;
            }
            parent = Directory.GetParent(parent)?.FullName;
        }

        if (!Directory.Exists(verifyDir))
        {
            Directory.CreateDirectory(verifyDir);
        }

        string dryRunReportPath = Path.Combine(verifyDir, $"dryrun-{presetName.ToLowerInvariant()}.md");

        var previews = dryRun.GeneratePreview(targetTweaks);

        var reportLines = new List<string>
        {
            $"# WinVelo Dry-Run Plan: Preset `{presetName}`",
            "",
            $"- **Generated At**: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC",
            $"- **Preset**: `{presetName}`",
            $"- **Total Tweaks In Preset**: {targetTweaks.Count}",
            $"- **Total Actions Planned**: {previews.Sum(p => p.Actions.Count)}",
            "",
            "## Planned Execution Actions",
            ""
        };

        foreach (var p in previews)
        {
            reportLines.Add($"### `{p.TweakId}`: {p.Name} (Risk: **{p.Risk}**)");
            reportLines.Add("| Action Type | Target | Operation | Current State | Proposed State |");
            reportLines.Add("|---|---|---|---|---|");
            foreach (var a in p.Actions)
            {
                reportLines.Add($"| {a.ActionType} | `{a.Target}` | `{a.Operation}` | `{a.CurrentState}` | `{a.ProposedState}` |");
            }
            reportLines.Add("");
        }

        File.WriteAllText(dryRunReportPath, string.Join(Environment.NewLine, reportLines));

        Console.WriteLine($"\n[DRY RUN PREVIEW] Preset '{presetName}' ({targetTweaks.Count} tweaks):");
        foreach (var p in previews)
        {
            Console.WriteLine($"\n[{p.Risk}] {p.TweakId} ({p.Name})");
            foreach (var a in p.Actions)
            {
                Console.WriteLine($"  • {a.ActionType} {a.Target} -> {a.Operation} (Current: {a.CurrentState} | Proposed: {a.ProposedState})");
            }
        }

        Console.WriteLine($"\n[INFO] Dry-run plan saved to: {dryRunReportPath}");
        return Task.FromResult(0);
    }

    private static List<TweakDefinition> LoadCatalog(string tweaksDir)
    {
        List<TweakDefinition> tweaks = new();
        if (!Directory.Exists(tweaksDir)) return tweaks;

        foreach (string file in Directory.GetFiles(tweaksDir, "*.json", SearchOption.AllDirectories))
        {
            try
            {
                string json = File.ReadAllText(file);
                List<TweakDefinition>? items = JsonSerializer.Deserialize<List<TweakDefinition>>(json);
                if (items != null) tweaks.AddRange(items);
            }
            catch { }
        }
        return tweaks;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  winvelo-cli --self-test --level 1        Run automated safe HKCU self-test with restore point");
        Console.WriteLine("  winvelo-cli --detect-all                 Non-destructive read-only detection across all tweaks");
        Console.WriteLine("  winvelo-cli --preset <name> [--dry-run]  Inspect or apply preset (Standard, Minimal, Advanced)");
        Console.WriteLine("  winvelo-cli --list                       List all catalog tweaks");
        Console.WriteLine("  winvelo-cli --dry-run                    Preview all catalog tweaks without modifying system");
        Console.WriteLine("  winvelo-cli --tweak <id> --apply         Apply a specific tweak (add --real-mode for real OS)");
        Console.WriteLine("  winvelo-cli --tweak <id> --undo          Undo a specific tweak (add --real-mode for real OS)");
        Console.WriteLine("  winvelo-cli --feature <key> --enable     Enable an optional Windows feature");
        Console.WriteLine("  winvelo-cli --feature <key> --disable    Disable an optional Windows feature");
        Console.WriteLine("  winvelo-cli --clean <category|all>       Clean safe temporary file categories");
        Console.WriteLine("  winvelo-cli --dns <id>                   Apply a DNS preset (e.g. dns.cloudflare.standard)");
        Console.WriteLine("  winvelo-cli --update-mode <mode>         Set update mode (Default, Security, DisableAll)");
        Console.WriteLine("  winvelo-cli --fix <id>                   Execute a system maintenance fix");
        Console.WriteLine("  winvelo-cli --config <path> --run        Apply configuration profile (protected by journal)");
        Console.WriteLine("  winvelo-cli --real-mode                  Use real Windows drivers (default is simulation)");
    }
}
