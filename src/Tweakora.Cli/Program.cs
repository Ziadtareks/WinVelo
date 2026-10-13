using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Tweakora.Core.Engine;
using Tweakora.Core.Engine.Handlers;
using Tweakora.Core.Fakes;
using Tweakora.Core.Models;
using Tweakora.Core.Profiles;
using Tweakora.Core.Safety;

namespace Tweakora.Cli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.WriteLine("========================================");
        Console.WriteLine("          Tweakora CLI v0.2.0           ");
        Console.WriteLine("========================================");

        if (args.Length == 0 || args.Contains("--help") || args.Contains("-h"))
        {
            PrintUsage();
            return 0;
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

        bool isDryRun = args.Contains("--dry-run");
        bool isRun = args.Contains("--run");
        bool isTestMode = args.Contains("--test-mode") || !isRun;

        if (args.Contains("--list"))
        {
            Console.WriteLine("\nAvailable Tweaks in Catalog:");
            foreach (var t in catalog)
            {
                Console.WriteLine($"  - [{t.Risk,-6}] {t.Id}: {t.Name} (Category: {t.Category})");
            }
            return 0;
        }

        // Setup execution context with fakes for safe CLI simulation
        FakeRegistryService fakeReg = new();
        FakeServicesService fakeSvc = new();
        FakeTaskSchedulerService fakeTask = new();
        FakeRestoreService fakeRestore = new();
        FakeShellService fakeShell = new();
        RollbackJournalManager journal = new(Path.Combine(Path.GetTempPath(), "Tweakora_CLI_Journal"));

        Core.Engine.ExecutionContext context = new()
        {
            Registry = fakeReg,
            Services = fakeSvc,
            Tasks = fakeTask,
            Shell = fakeShell,
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

        StateDetector detector = new(fakeReg, fakeSvc, fakeTask);
        DryRunEngine dryRun = new(handlers, context);
        TweakRunner runner = new(handlers, context, fakeRestore, detector);

        // Check for --config <path>
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
                Console.WriteLine($"\n[EXECUTE] Running {targetTweaks.Count} configured tweak(s) (Simulation: {isTestMode})...");
                var report = await runner.ApplyBatchAsync(targetTweaks);
                foreach (var res in report.Results)
                {
                    Console.WriteLine($"  [{res.Status}] {res.TweakId} - {res.Message}");
                }
                Console.WriteLine($"\n[DONE] Finished executing configuration. Applied: {report.AppliedCount}, Skipped: {report.SkippedCount}, Failed: {report.FailedCount}");
                return 0;
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

        Console.WriteLine("\n[NOTE] Use --config <path> with --dry-run or --run to automate configuration.");
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
        Console.WriteLine("  tweakora-cli --config <path> --dry-run    Preview configuration profile without applying");
        Console.WriteLine("  tweakora-cli --config <path> --run        Apply configuration profile (protected by journal)");
        Console.WriteLine("  tweakora-cli --silent                     Run silently with minimal output");
    }
}
