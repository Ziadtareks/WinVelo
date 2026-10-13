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

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.WriteLine("========================================");
        Console.WriteLine("          Tweakora CLI v0.3.0           ");
        Console.WriteLine("========================================");

        if (args.Length == 0 || args.Contains("--help") || args.Contains("-h"))
        {
            PrintUsage();
            return 0;
        }

        bool isRealMode = args.Contains("--real-mode");
        bool isDryRun = args.Contains("--dry-run");
        bool isRun = args.Contains("--run") || args.Contains("--apply") || args.Contains("--enable") || args.Contains("--disable") || args.Contains("--clean") || args.Contains("--dns") || args.Contains("--update-mode") || args.Contains("--fix") || args.Contains("--undo");

        if (isRealMode)
        {
            TestModeManager.DisableTestMode();
            Console.WriteLine("[MODE] REAL-MODE ACTIVE — Modifying host OS configuration.");
        }
        else
        {
            TestModeManager.EnableTestMode();
            Console.WriteLine("[MODE] TEST/SIMULATION ACTIVE — Using safe in-memory fakes.");
        }

        string rootDir = AppDomain.CurrentDomain.BaseDirectory;
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
            "Tweakora", "Journal");
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
        Console.WriteLine("  tweakora-cli --list                       List all catalog tweaks");
        Console.WriteLine("  tweakora-cli --dry-run                    Preview all catalog tweaks without modifying system");
        Console.WriteLine("  tweakora-cli --tweak <id> --apply         Apply a specific tweak (add --real-mode for real OS)");
        Console.WriteLine("  tweakora-cli --tweak <id> --undo          Undo a specific tweak (add --real-mode for real OS)");
        Console.WriteLine("  tweakora-cli --feature <key> --enable     Enable an optional Windows feature");
        Console.WriteLine("  tweakora-cli --feature <key> --disable    Disable an optional Windows feature");
        Console.WriteLine("  tweakora-cli --clean <category|all>       Clean safe temporary file categories");
        Console.WriteLine("  tweakora-cli --dns <id>                   Apply a DNS preset (e.g. dns.cloudflare.standard)");
        Console.WriteLine("  tweakora-cli --update-mode <mode>         Set update mode (Default, Security, DisableAll)");
        Console.WriteLine("  tweakora-cli --fix <id>                   Execute a system maintenance fix");
        Console.WriteLine("  tweakora-cli --config <path> --run        Apply configuration profile (protected by journal)");
        Console.WriteLine("  tweakora-cli --real-mode                  Use real Windows drivers (default is simulation)");
    }
}
