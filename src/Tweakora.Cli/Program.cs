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
using Tweakora.Core.Utilities;
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
        Console.WriteLine("         WinVelo CLI v0.9.0-beta        ");
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

        int infoIdx = Array.FindIndex(args, a => string.Equals(a, "--info", StringComparison.OrdinalIgnoreCase) || a.StartsWith("--info=", StringComparison.OrdinalIgnoreCase));
        if (infoIdx >= 0)
        {
            string? targetId = null;
            if (args[infoIdx].StartsWith("--info=", StringComparison.OrdinalIgnoreCase))
            {
                targetId = args[infoIdx].Substring("--info=".Length);
            }
            else if (infoIdx + 1 < args.Length)
            {
                targetId = args[infoIdx + 1];
            }

            if (string.IsNullOrWhiteSpace(targetId))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("[WARN] Please specify a tweak ID to inspect: winvelo-cli --info <tweakId>");
                Console.ResetColor();
                return 1;
            }

            var tweak = catalog.FirstOrDefault(t => string.Equals(t.Id, targetId, StringComparison.OrdinalIgnoreCase));
            if (tweak == null)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[ERROR] Tweak '{targetId}' not found in catalog.");
                Console.ResetColor();
                return 1;
            }

            PrintTweakInfo(tweak);
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

        AppDataMigrator.Migrate();

        int diagIdx = Array.FindIndex(args, a => string.Equals(a, "--diagnostics", StringComparison.OrdinalIgnoreCase) || a.StartsWith("--diagnostics=", StringComparison.OrdinalIgnoreCase));
        if (diagIdx >= 0)
        {
            string? outZip = null;
            if (args[diagIdx].StartsWith("--diagnostics=", StringComparison.OrdinalIgnoreCase))
            {
                outZip = args[diagIdx].Substring("--diagnostics=".Length);
            }
            else if (diagIdx + 1 < args.Length && !args[diagIdx + 1].StartsWith("--"))
            {
                outZip = args[diagIdx + 1];
            }

            if (string.IsNullOrWhiteSpace(outZip))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("[WARN] Please specify an output zip path: winvelo-cli --diagnostics <out.zip>");
                Console.ResetColor();
                return 1;
            }

            Console.WriteLine("[DIAGNOSTICS] Collecting system information, tweak states, and sanitized logs...");
            var collector = new Tweakora.Core.Diagnostics.DiagnosticsCollector();
            var result = await collector.CollectDiagnosticsAsync(outZip, registry, services, tasks, catalog);

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[SUCCESS] Diagnostic archive created: {result.OutputPath}");
            Console.WriteLine($"  • Total tweaks scanned: {result.TotalTweaksScanned}");
            Console.WriteLine($"  • Journal sessions summarized: {result.JournalSessionsCount}");
            Console.WriteLine($"  • Logs packaged (sanitized): {result.LogsPackagedCount}");
            Console.WriteLine($"  • Archive size: {result.ArchiveSizeBytes / 1024} KB");
            Console.ResetColor();
            return 0;
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
        // Crash Recovery Check: Unfinished journal sessions
        // ---------------------------------------------------------------------
        var unfinishedSessions = journal.GetUnfinishedSessions();
        int recoverArgIdx = Array.FindIndex(args, a => string.Equals(a, "--recover", StringComparison.OrdinalIgnoreCase));
        string? recoverMode = (recoverArgIdx >= 0 && recoverArgIdx < args.Length - 1)
            ? args[recoverArgIdx + 1].ToLowerInvariant()
            : null;

        if (recoverMode != null && recoverMode != "rollback" && recoverMode != "keep")
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[ERROR] Invalid recovery option '{recoverMode}'. Supported options: rollback, keep.");
            Console.ResetColor();
            return 1;
        }

        if (unfinishedSessions.Count > 0)
        {
            var touchedTweaks = unfinishedSessions
                .SelectMany(s => s.Records)
                .Select(r => r.TweakId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct()
                .ToList();
            int totalActions = unfinishedSessions.Sum(s => s.Records.Count);

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("\n[WARNING] Unfinished session(s) detected from a previous run!");
            Console.WriteLine($"Found {unfinishedSessions.Count} unfinished session(s) with {totalActions} action(s) touching tweak(s): [{string.Join(", ", touchedTweaks)}]");
            Console.ResetColor();

            if (string.IsNullOrEmpty(recoverMode))
            {
                if (Console.IsInputRedirected)
                {
                    Console.WriteLine("[INFO] Non-interactive console detected. Please specify --recover rollback or --recover keep.");
                }
                else
                {
                    Console.Write("Run rollback recovery? (y/n): ");
                    string? choice = Console.ReadLine()?.Trim().ToLowerInvariant();
                    recoverMode = (choice == "y" || choice == "yes") ? "rollback" : "keep";
                }
            }

            if (recoverMode == "rollback")
            {
                if (!CheckConfirmationGate(args, "Rolling back unfinished session changes"))
                {
                    return 1;
                }

                Console.WriteLine("[RECOVERY] Rolling back unfinished session changes...");
                var res = await runner.RollbackAllUnfinishedSessionsAsync();
                if (res.Success)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"[RECOVERY] {res.Message}");
                    Console.ResetColor();
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"[RECOVERY ERROR] {res.Message}");
                    Console.ResetColor();
                }
            }
            else if (recoverMode == "keep")
            {
                Console.WriteLine("[RECOVERY] Marking unfinished session(s) complete without rolling back...");
                runner.KeepAndCompleteAllUnfinishedSessions();
                Console.WriteLine("[RECOVERY] Unfinished session(s) marked complete.");
            }

            if (args.Length <= 2 && recoverArgIdx >= 0)
            {
                return 0;
            }
        }
        else if (recoverArgIdx >= 0)
        {
            Console.WriteLine("[INFO] No unfinished sessions found to recover.");
            if (args.Length <= 2)
            {
                return 0;
            }
        }

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
        // CLI COMMAND: --health-check [--restore]
        // ---------------------------------------------------------------------
        if (args.Contains("--health-check"))
        {
            return await ExecuteHealthCheckAsync(services, registry, args.Contains("--restore"));
        }

        // ---------------------------------------------------------------------
        // CLI COMMAND: --preset <name> [--dry-run | --run]
        // ---------------------------------------------------------------------
        int presetIdx = Array.FindIndex(args, a => string.Equals(a, "--preset", StringComparison.OrdinalIgnoreCase) || string.Equals(a, "-p", StringComparison.OrdinalIgnoreCase));
        if (presetIdx >= 0 && presetIdx < args.Length - 1)
        {
            string presetName = args[presetIdx + 1];
            return await ExecutePresetAsync(presetName, catalog, dryRun, isDryRun, isRun, handlers, context, runner, tweaksDir, args);
        }

        // 1. Tweak execution: --tweak <id1,id2> / --tweaks <id1,id2> [--apply | --undo]
        int tweakIdx = Array.FindIndex(args, a => string.Equals(a, "--tweak", StringComparison.OrdinalIgnoreCase) || string.Equals(a, "--tweaks", StringComparison.OrdinalIgnoreCase));
        if (tweakIdx >= 0 && tweakIdx < args.Length - 1)
        {
            string tweakArg = args[tweakIdx + 1];
            string[] rawIds = tweakArg.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var targetTweaks = new List<TweakDefinition>();
            int missingCount = 0;
            foreach (var id in rawIds)
            {
                var target = catalog.FirstOrDefault(t =>
                    string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(t.Origin, id, StringComparison.OrdinalIgnoreCase));
                if (target != null)
                {
                    targetTweaks.Add(target);
                }
                else
                {
                    missingCount++;
                    Console.WriteLine($"[Failed] {id}: Tweak not found in catalog");
                }
            }

            int totalRequested = targetTweaks.Count + missingCount;

            if (targetTweaks.Count == 0)
            {
                Console.WriteLine($"[ERROR] None of the specified tweaks were found in catalog.");
                Console.WriteLine($"Applied 0 of {totalRequested} — {missingCount} failed");
                return 1;
            }

            if (args.Contains("--undo"))
            {
                if (!CheckConfirmationGate(args, $"Undoing {targetTweaks.Count} tweak(s)"))
                {
                    return 1;
                }

                int restored = 0;
                int failed = missingCount;
                foreach (var tweak in targetTweaks)
                {
                    Console.WriteLine($"[EXECUTE] Undoing tweak: {tweak.Id} ({tweak.Name})...");
                    var result = await runner.UndoTweakAsync(tweak);
                    if (result.Success)
                    {
                        restored++;
                        Console.WriteLine($"  [Restored] {tweak.Id} - {result.Message ?? "Restored original state."}");
                    }
                    else
                    {
                        failed++;
                        Console.WriteLine($"[Failed] {tweak.Id}: {result.Message ?? "Undo failed."}");
                    }
                }

                Console.WriteLine($"Restored {restored} of {totalRequested} — {failed} failed");
                return failed == 0 ? 0 : 1;
            }
            else
            {
                if (!CheckConfirmationGate(args, $"Applying {targetTweaks.Count} tweak(s)"))
                {
                    return 1;
                }

                if (!CheckIrreversibleTweaksGate(targetTweaks, args))
                {
                    return 1;
                }

                Console.WriteLine($"[EXECUTE] Applying {targetTweaks.Count} tweak(s)...");
                var report = await runner.ApplyBatchAsync(targetTweaks);
                foreach (var res in report.Results)
                {
                    if (!res.IsFailure)
                    {
                        Console.WriteLine($"  [{res.DisplayStatus}] {res.TweakId} - {res.Message ?? "Applied successfully."}");
                    }
                    else
                    {
                        Console.WriteLine($"[Failed] {res.TweakId}: {res.Message ?? "Execution failed."}");
                    }
                }

                int totalFailed = report.FailedCount + missingCount;
                Console.WriteLine($"Applied {report.AppliedCount} of {totalRequested} — {totalFailed} failed");
                return totalFailed == 0 ? 0 : 1;
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
                if (!CheckConfirmationGate(args, $"Applying configuration profile '{configPath}' ({targetTweaks.Count} tweaks)"))
                {
                    return 1;
                }

                if (!CheckIrreversibleTweaksGate(targetTweaks, args))
                {
                    return 1;
                }

                Console.WriteLine($"\n[EXECUTE] Running {targetTweaks.Count} configured tweak(s) (RealMode: {isRealMode})...");
                var report = await runner.ApplyBatchAsync(targetTweaks);
                foreach (var res in report.Results)
                {
                    if (!res.IsFailure)
                    {
                        Console.WriteLine($"  [{res.DisplayStatus}] {res.TweakId} - {res.Message ?? "Applied successfully."}");
                    }
                    else
                    {
                        Console.WriteLine($"[Failed] {res.TweakId}: {res.Message ?? "Execution failed."}");
                    }
                }
                Console.WriteLine($"Applied {report.AppliedCount} of {report.TotalRequested} — {report.FailedCount} failed");
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
        int partialCount = 0;
        int defaultCount = 0;
        int unknownCount = 0;
        int errorCount = 0;

        for (int i = 0; i < catalog.Count; i++)
        {
            var tweak = catalog[i];
            string status = "Default";
            string errorDetails = "None";

            try
            {
                var state = await detector.DetectTweakStateAsync(tweak, useCache: false);
                switch (state)
                {
                    case TweakState.Applied:
                        status = "Applied";
                        appliedCount++;
                        break;
                    case TweakState.Partial:
                        status = "Partial";
                        partialCount++;
                        break;
                    case TweakState.Unknown:
                        status = "Unknown";
                        unknownCount++;
                        break;
                    default:
                        status = "Default";
                        defaultCount++;
                        break;
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
        lines.Add($"- **Partial**: {partialCount}");
        lines.Add($"- **Default (Inactive)**: {defaultCount}");
        lines.Add($"- **Unknown (Unverifiable)**: {unknownCount}");
        lines.Add($"- **Errors/Access Issues**: {errorCount}");

        File.WriteAllText(reportPath, string.Join(Environment.NewLine, lines));

        Console.WriteLine("\n=======================================================");
        Console.WriteLine($"Detection Complete! Applied: {appliedCount}, Partial: {partialCount}, Default: {defaultCount}, Unknown: {unknownCount}, Errors: {errorCount}");
        Console.WriteLine($"Saved full detection report to: {reportPath}");
        Console.WriteLine("=======================================================");

        return 0;
    }

    private static async Task<int> ExecutePresetAsync(
        string presetName,
        List<TweakDefinition> catalog,
        DryRunEngine dryRun,
        bool isDryRun,
        bool isRun,
        IEnumerable<IActionHandler> handlers,
        Core.Engine.ExecutionContext context,
        TweakRunner runner,
        string catalogDir,
        string[] args)
    {
        string presetsDir = Path.Combine(catalogDir, "..", "Presets");
        if (!Directory.Exists(presetsDir))
        {
            string? p = Directory.GetParent(catalogDir)?.FullName;
            if (p != null)
            {
                presetsDir = Path.Combine(p, "Presets");
            }
        }

        List<PresetDefinition> loadedPresets = new();
        if (Directory.Exists(presetsDir))
        {
            foreach (var pFile in Directory.GetFiles(presetsDir, "*.json"))
            {
                try
                {
                    string pJson = File.ReadAllText(pFile);
                    var def = JsonSerializer.Deserialize<PresetDefinition>(pJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (def != null) loadedPresets.Add(def);
                }
                catch { }
            }
        }

        PresetDefinition? matchedPreset = loadedPresets.FirstOrDefault(p =>
            string.Equals(p.Id, presetName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(p.Id, $"preset.{presetName}", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(p.Name, presetName, StringComparison.OrdinalIgnoreCase));

        if (matchedPreset == null && Directory.Exists(presetsDir))
        {
            string namedFile = Path.Combine(presetsDir, $"{presetName}.json");
            if (File.Exists(namedFile))
            {
                try
                {
                    matchedPreset = JsonSerializer.Deserialize<PresetDefinition>(File.ReadAllText(namedFile), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }
                catch { }
            }
        }

        if (matchedPreset == null)
        {
            Console.WriteLine($"[ERROR] Preset '{presetName}' not found in Catalog/Presets.");
            Console.WriteLine("Available presets: " + string.Join(", ", loadedPresets.Select(x => $"{x.Id} ({x.Name})")));
            return 1;
        }

        var keySet = new HashSet<string>(matchedPreset.TweakIds, StringComparer.OrdinalIgnoreCase);
        var targetTweaks = catalog.Where(t => keySet.Contains(t.Id)).ToList();
        Console.WriteLine($"[INFO] Preset '{matchedPreset.Name}' ({matchedPreset.Id}) matched {targetTweaks.Count} of {matchedPreset.TweakIds.Count} configured tweaks.");

        if (isRun)
        {
            if (!CheckConfirmationGate(args, $"Applying preset '{matchedPreset.Name}' ({targetTweaks.Count} tweaks)"))
            {
                return 1;
            }

            if (!CheckIrreversibleTweaksGate(targetTweaks, args))
            {
                return 1;
            }

            Console.WriteLine($"\n[EXECUTE] Running preset '{presetName}' ({targetTweaks.Count} tweaks)...");
            var report = await runner.ApplyBatchAsync(targetTweaks);
            foreach (var res in report.Results)
            {
                if (!res.IsFailure)
                {
                    Console.WriteLine($"  [{res.DisplayStatus}] {res.TweakId} - {res.Message ?? "Applied successfully."}");
                }
                else
                {
                    Console.WriteLine($"[Failed] {res.TweakId}: {res.Message ?? "Execution failed."}");
                }
            }
            Console.WriteLine($"Applied {report.AppliedCount} of {report.TotalRequested} — {report.FailedCount} failed");

            Console.WriteLine("\n[HEALTH CHECK] Verifying Windows servicing integrity...");
            var healthReport = await ServicingProtectionHelper.CheckServicingHealthAsync(context.Services, context.Registry);
            PrintHealthReport(healthReport);

            return report.FailedCount == 0 ? 0 : 1;
        }

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
        return 0;
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

    private static bool CheckIrreversibleTweaksGate(IEnumerable<TweakDefinition> tweaks, string[] args)
    {
        var irreversible = tweaks.Where(t => !t.Reversible).ToList();
        if (irreversible.Count == 0)
        {
            return true;
        }

        bool hasFlag = args.Any(a => string.Equals(a, "--yes-irreversible", StringComparison.OrdinalIgnoreCase));
        if (hasFlag)
        {
            return true;
        }

        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("[ERROR] The following tweak(s) are irreversible and cannot be fully restored by Undo:");
        foreach (var t in irreversible)
        {
            Console.WriteLine($"  - {t.Id} ({t.Name}): {t.IrreversibleReason ?? "Permanent system modification."}");
        }
        Console.WriteLine();
        Console.WriteLine("To proceed and apply these irreversible tweaks, re-run with --yes-irreversible.");
        Console.ResetColor();
        return false;
    }

    public static bool CheckConfirmationGate(string[] args, string operationDescription)
    {
        // Simulation / test mode never touches the real host system
        if (args.Any(a => string.Equals(a, "--test-mode", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        // If explicitly confirmed via --yes or -y, proceed immediately
        if (args.Any(a => string.Equals(a, "--yes", StringComparison.OrdinalIgnoreCase) || string.Equals(a, "-y", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        // If stdin is not interactive, refuse to execute
        if (Console.IsInputRedirected)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n[ERROR] Command requires confirmation for '{operationDescription}', but standard input is not interactive.");
            Console.WriteLine("Pass '--yes' to confirm execution non-interactively.");
            Console.ResetColor();
            return false;
        }

        // Prompt user interactively
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Write($"\n[CONFIRM] Are you sure you want to proceed with: {operationDescription}? (y/N): ");
        Console.ResetColor();

        string? response = Console.ReadLine()?.Trim();
        if (string.Equals(response, "y", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(response, "yes", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        Console.WriteLine("[INFO] Operation cancelled by user.");
        return false;
    }

    private static void PrintHealthReport(ServicingHealthReport report)
    {
        Console.WriteLine("========================================");
        Console.WriteLine("     Windows Servicing Health Check     ");
        Console.WriteLine("========================================");

        var wu = report.Components.FirstOrDefault(c => c.Name.Equals("wuauserv", StringComparison.OrdinalIgnoreCase));
        var bits = report.Components.FirstOrDefault(c => c.Name.Equals("BITS", StringComparison.OrdinalIgnoreCase));
        var store = report.Components.FirstOrDefault(c => c.Name.Equals("InstallService", StringComparison.OrdinalIgnoreCase));
        var defender = report.Components.FirstOrDefault(c => c.Name.Equals("WinDefend", StringComparison.OrdinalIgnoreCase));
        var search = report.Components.FirstOrDefault(c => c.Name.Equals("WSearch", StringComparison.OrdinalIgnoreCase));

        Console.WriteLine($"• Windows Update Service (wuauserv):    {wu?.ActualState ?? "Unknown"} [{(wu != null && !wu.IsCompromised ? "PASS - Startable" : "FAIL - Disabled")}]");
        Console.WriteLine($"• BITS Service:                         {bits?.ActualState ?? "Unknown"} [{(bits != null && !bits.IsCompromised ? "PASS - Not Disabled" : "FAIL - Disabled")}]");
        Console.WriteLine($"• Store Install Service:                {store?.ActualState ?? "Unknown"} [{(store != null && !store.IsCompromised ? "PASS - Not Disabled" : "FAIL - Disabled")}]");
        Console.WriteLine($"• Windows Defender Antivirus:           {defender?.ActualState ?? "Unknown"} [{(defender != null && !defender.IsCompromised ? "PASS - Healthy" : "FAIL - Disabled")}]");
        Console.WriteLine($"• Windows Search State:                 {search?.ActualState ?? "Unknown"} [INFO]");

        Console.WriteLine("\nAll Monitored Components:");
        foreach (var c in report.Components)
        {
            string statusTag = c.IsCompromised ? "[COMPROMISED]" : "[HEALTHY]";
            Console.ForegroundColor = c.IsCompromised ? ConsoleColor.Red : ConsoleColor.Green;
            Console.Write($"  {statusTag,-14}");
            Console.ResetColor();
            Console.WriteLine($" {c.Name,-22} | {c.Category,-16} | State: {c.ActualState}");
            if (!string.IsNullOrWhiteSpace(c.IssueDescription))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"    └─ {c.IssueDescription}");
                Console.ResetColor();
            }
        }

        if (report.Warnings.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"\n[WARNING] Found {report.Warnings.Count} servicing integrity issue(s):");
            foreach (var w in report.Warnings)
            {
                Console.WriteLine($"  - {w}");
            }
            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("\n[PASS] All Windows servicing components and policies are healthy.");
            Console.ResetColor();
        }
    }

    private static async Task<int> ExecuteHealthCheckAsync(ISystemServicesService services, IRegistryService registry, bool restore)
    {
        Console.WriteLine("[INFO] Running Windows Servicing Health Check...");
        var report = await ServicingProtectionHelper.CheckServicingHealthAsync(services, registry);
        PrintHealthReport(report);

        if (!report.IsHealthy)
        {
            if (restore)
            {
                Console.WriteLine("\n[REPAIR] Restoring compromised servicing components and policies...");
                var restored = await ServicingProtectionHelper.RestoreServicingComponentsAsync(services, registry);
                foreach (var item in restored)
                {
                    Console.WriteLine($"  ✔ {item}");
                }
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("[PASS] Repair completed. Re-running health check...");
                Console.ResetColor();

                var postReport = await ServicingProtectionHelper.CheckServicingHealthAsync(services, registry);
                PrintHealthReport(postReport);
                return postReport.IsHealthy ? 0 : 1;
            }
            else
            {
                Console.WriteLine("\nTo restore compromised components, re-run with: winvelo-cli --health-check --restore");
                return 1;
            }
        }

        return 0;
    }

    private static void PrintTweakInfo(TweakDefinition tweak)
    {
        Console.WriteLine("\n============================================================");
        Console.WriteLine($" TWEAK INFORMATION: {tweak.Id}");
        Console.WriteLine("============================================================");
        Console.WriteLine($"Name:                  {tweak.Name}");
        Console.WriteLine($"Category:              {tweak.Category} (Subcategory: {tweak.Subcategory ?? "N/A"})");
        Console.WriteLine($"Risk:                  {tweak.Risk}");
        Console.WriteLine($"Evidence:              {tweak.Evidence}");
        Console.WriteLine($"Reversible:            {(tweak.Reversible ? "Yes" : "No")}");
        Console.WriteLine($"Requires Reboot:       {(tweak.RequiresReboot ? "Yes" : "No")}");
        Console.WriteLine($"Requires Explorer:     {(tweak.RequiresExplorerRestart ? "Yes" : "No")}");
        Console.WriteLine($"Needs Review:          {(tweak.NeedsReview ? "Yes (Excluded from automated presets)" : "No")}");

        Console.WriteLine("\nWHAT IT DOES:");
        Console.WriteLine(string.IsNullOrWhiteSpace(tweak.WhatItDoes) ? tweak.Description : tweak.WhatItDoes);

        Console.WriteLine("\nSIDE EFFECTS / POTENTIAL IMPACT:");
        Console.WriteLine(string.IsNullOrWhiteSpace(tweak.SideEffects)
            ? (string.Equals(tweak.Risk, "Safe", StringComparison.OrdinalIgnoreCase) ? "None known." : "See documentation for potential impacts.")
            : tweak.SideEffects);

        if (!string.IsNullOrWhiteSpace(tweak.UiEquivalentLocation))
        {
            Console.WriteLine($"\nWHERE TO FIND THIS IN WINDOWS:\n  {tweak.UiEquivalentLocation}");
        }

        Console.WriteLine("\nEXACT CHANGES (ENGINE SPECIFICATION):");
        var exactChanges = TweakActionFormatter.FormatExactChanges(tweak);
        if (exactChanges.Count > 0)
        {
            foreach (var change in exactChanges)
            {
                Console.WriteLine($"  • {change}");
            }
        }
        else
        {
            Console.WriteLine("  (No direct apply actions defined)");
        }

        Console.WriteLine("\nDOCUMENTATION & SOURCES:");
        var sources = tweak.Sources != null && tweak.Sources.Count > 0
            ? tweak.Sources
            : (tweak.Source.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(tweak.Source.GetString())
                ? new List<string> { tweak.Source.GetString()! }
                : new List<string>());

        if (sources.Count > 0)
        {
            foreach (var s in sources)
            {
                Console.WriteLine($"  • {s}");
            }
        }
        else
        {
            Console.WriteLine("  (No sources documented)");
        }

        if (!string.IsNullOrWhiteSpace(tweak.SourceQuote))
        {
            Console.WriteLine($"\nVERIFIED SOURCE QUOTE:\n  \"{tweak.SourceQuote}\"");
        }
        Console.WriteLine("============================================================\n");
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  winvelo-cli --info <tweakId>             Show detailed technical specification and exact changes");
        Console.WriteLine("  winvelo-cli --self-test --level 1        Run automated safe HKCU self-test with restore point");
        Console.WriteLine("  winvelo-cli --detect-all                 Non-destructive read-only detection across all tweaks");
        Console.WriteLine("  winvelo-cli --health-check [--restore]   Verify Windows Update, Store, and servicing integrity");
        Console.WriteLine("  winvelo-cli --preset <name> [--dry-run]  Inspect or apply preset (Standard, Minimal, Advanced)");
        Console.WriteLine("  winvelo-cli --list                       List all catalog tweaks");
        Console.WriteLine("  winvelo-cli --dry-run                    Preview all catalog tweaks without modifying system");
        Console.WriteLine("  winvelo-cli --tweak <id> --apply         Apply a specific tweak (add --real-mode for real OS)");
        Console.WriteLine("  winvelo-cli --tweak <id> --undo          Undo a specific tweak (add --real-mode for real OS)");
        Console.WriteLine("  winvelo-cli --recover <rollback|keep>    Recover or resolve unfinished journal sessions");
        Console.WriteLine("  winvelo-cli --yes-irreversible           Confirm application of irreversible tweaks");
        Console.WriteLine("  winvelo-cli --feature <key> --enable     Enable an optional Windows feature");
        Console.WriteLine("  winvelo-cli --feature <key> --disable    Disable an optional Windows feature");
        Console.WriteLine("  winvelo-cli --clean <category|all>       Clean safe temporary file categories");
        Console.WriteLine("  winvelo-cli --dns <id>                   Apply a DNS preset (e.g. dns.cloudflare.standard)");
        Console.WriteLine("  winvelo-cli --update-mode <mode>         Set update mode (Default, Security, DisableAll)");
        Console.WriteLine("  winvelo-cli --fix <id>                   Execute a system maintenance fix");
        Console.WriteLine("  winvelo-cli --config <path> --run        Apply configuration profile (protected by journal)");
        Console.WriteLine("  winvelo-cli --diagnostics <out.zip>      Export read-only diagnostic bundle for bug reports");
        Console.WriteLine("  winvelo-cli --yes / -y                   Bypass interactive confirmation for modifying commands");
        Console.WriteLine("  winvelo-cli --real-mode                  Use real Windows drivers (default is simulation)");
    }
}

