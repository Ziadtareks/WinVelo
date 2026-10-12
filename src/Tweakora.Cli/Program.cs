using System.Text.Json;
using Tweakora.Core.Engine;
using Tweakora.Core.Engine.Handlers;
using Tweakora.Core.Fakes;
using Tweakora.Core.Models;

namespace Tweakora.Cli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.WriteLine("========================================");
        Console.WriteLine("          Tweakora CLI v0.1.0           ");
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
            // Upward search
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
        bool isSilent = args.Contains("--silent");

        if (args.Contains("--list"))
        {
            Console.WriteLine("\nAvailable Tweaks in Catalog:");
            foreach (var t in catalog)
            {
                Console.WriteLine($"  - [{t.Risk,-6}] {t.Id}: {t.Name.En} (Category: {t.Category})");
            }
            return 0;
        }

        // Setup safe test execution context for CLI dry-run and command demonstrations
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
            new FileActionHandler()
        };

        if (isDryRun)
        {
            DryRunEngine dryRun = new(handlers, context);
            List<TweakPreview> previews = dryRun.GeneratePreview(catalog);
            Console.WriteLine("\n[DRY RUN PREVIEW] No changes will be made:");
            foreach (var p in previews)
            {
                Console.WriteLine($"\nTweak: {p.TweakId} ({p.Risk})");
                foreach (var a in p.Actions)
                {
                    Console.WriteLine($"  - [{a.ActionType}] {a.Target} -> Op: {a.Operation} (Current: {a.CurrentState} | Proposed: {a.ProposedState})");
                }
            }
            return 0;
        }

        Console.WriteLine("\n[NOTE] Real modifications require GUI mode with System Restore verification.");
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
        Console.WriteLine("  tweakora-cli --list                   List all catalog tweaks");
        Console.WriteLine("  tweakora-cli --dry-run                Preview all tweak changes without modifying system");
        Console.WriteLine("  tweakora-cli --apply <tweak-id>       Apply a specific tweak (GUI recommended)");
        Console.WriteLine("  tweakora-cli --undo <tweak-id>        Undo a specific tweak");
        Console.WriteLine("  tweakora-cli --silent                 Run silently with minimal output");
    }
}
