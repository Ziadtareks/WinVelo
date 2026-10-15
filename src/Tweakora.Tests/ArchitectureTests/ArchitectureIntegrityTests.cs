using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using Tweakora.App.ViewModels;
using Tweakora.Core.Engine;
using Tweakora.Core.Interfaces;
using Tweakora.Core.Operations;
using Tweakora.Modules.Advisor;
using Tweakora.Modules.Apps;
using Tweakora.Modules.Cleaner;
using Tweakora.Modules.Config;
using Tweakora.Modules.Memory;
using Tweakora.Modules.Network;
using Tweakora.Modules.Services;
using Tweakora.Modules.Startup;
using Tweakora.Modules.Tweaks;
using Tweakora.Modules.Updates;
using Tweakora.Core.Safety;
using Tweakora.Simulation;
using Xunit;

namespace Tweakora.Tests.ArchitectureTests;

public sealed class ArchitectureIntegrityTests
{
    [Fact]
    public void Architecture_NoFakesInProductionCode_ViewModelsAndServicesNeverReferenceSimulation()
    {
        // ViewModels, Modules, and Core must never reference Tweakora.Simulation types
        var assembliesToScan = new[]
        {
            typeof(Tweakora.Core.Engine.TweakRunner).Assembly,
            typeof(Tweakora.Platform.WindowsRegistryService).Assembly,
            typeof(CleanerService).Assembly,
            typeof(MainViewModel).Assembly
        };

        List<string> violations = new();

        foreach (var asm in assembliesToScan)
        {
            foreach (var type in asm.GetTypes())
            {
                // The composition roots App (App.xaml.cs) in Tweakora.App is allowed to reference fakes ONLY for --test-mode DI
                if (type.Name == "App" || type.Name.StartsWith("App<")) continue;

                // Check interfaces, base types, fields, properties, constructor arguments, and methods
                foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
                {
                    if (IsForbiddenSimulationType(field.FieldType))
                    {
                        violations.Add($"{type.FullName}.{field.Name} has forbidden type {field.FieldType.FullName}");
                    }
                }

                foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
                {
                    if (IsForbiddenSimulationType(prop.PropertyType))
                    {
                        violations.Add($"{type.FullName}.{prop.Name} has forbidden type {prop.PropertyType.FullName}");
                    }
                }

                foreach (var ctor in type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    foreach (var param in ctor.GetParameters())
                    {
                        if (IsForbiddenSimulationType(param.ParameterType))
                        {
                            violations.Add($"{type.FullName} constructor parameter '{param.Name}' has forbidden type {param.ParameterType.FullName}");
                        }
                    }
                }

                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                {
                    if (IsForbiddenSimulationType(method.ReturnType))
                    {
                        violations.Add($"{type.FullName}.{method.Name}() return type is forbidden {method.ReturnType.FullName}");
                    }
                    foreach (var param in method.GetParameters())
                    {
                        if (IsForbiddenSimulationType(param.ParameterType))
                        {
                            violations.Add($"{type.FullName}.{method.Name}({param.Name}) parameter type is forbidden {param.ParameterType.FullName}");
                        }
                    }
                }
            }
        }

        Assert.True(violations.Count == 0, "Architecture violation: Fakes leaked into production types:\n" + string.Join("\n", violations));
    }

    [Fact]
    public void Architecture_NoForbiddenTaskDelay_ForbiddenInSourceCode()
    {
        string? rootDir = FindSolutionRoot();
        Assert.NotNull(rootDir);

        string srcDir = Path.Combine(rootDir, "src");
        Assert.True(Directory.Exists(srcDir), $"Source directory not found at {srcDir}");

        List<string> violations = new();

        foreach (string file in Directory.GetFiles(srcDir, "*.cs", SearchOption.AllDirectories))
        {
            // Allowed exceptions: Tests and Simulation projects only
            if (file.Contains("Tweakora.Tests", StringComparison.OrdinalIgnoreCase) ||
                file.Contains("Tweakora.UiTests", StringComparison.OrdinalIgnoreCase) ||
                file.Contains("Tweakora.Simulation", StringComparison.OrdinalIgnoreCase) ||
                file.Contains("obj", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.StartsWith("//") || line.StartsWith("/*")) continue;

                if (line.Contains("Task.Delay", StringComparison.Ordinal))
                {
                    violations.Add($"{Path.GetFileName(file)}:{i + 1} -> {line}");
                }
            }
        }

        Assert.True(violations.Count == 0,
            "Architecture violation: Forbidden Task.Delay found in production code:\n" + string.Join("\n", violations));
    }

    [Fact]
    public void Architecture_NoSimulationFlagsInProductionCode()
    {
        var assembliesToScan = new[]
        {
            typeof(Tweakora.Core.Engine.TweakRunner).Assembly,
            typeof(CleanerService).Assembly
        };

        List<string> violations = new();

        foreach (var asm in assembliesToScan)
        {
            foreach (var type in asm.GetTypes())
            {
                foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
                {
                    if (prop.Name.Contains("Simulation", StringComparison.OrdinalIgnoreCase))
                    {
                        violations.Add($"{type.FullName}.{prop.Name}");
                    }
                }

                foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
                {
                    if (field.Name.Contains("Simulation", StringComparison.OrdinalIgnoreCase))
                    {
                        violations.Add($"{type.FullName}.{field.Name}");
                    }
                }
            }
        }

        Assert.True(violations.Count == 0,
            "Architecture violation: isSimulationMode flags found in production services:\n" + string.Join("\n", violations));
    }

    [Fact]
    public void Architecture_EveryICommandInViewModelsIsNotNullAndCallable()
    {
        // Build simulation DI container to instantiate all ViewModels
        var services = new ServiceCollection();
        services.AddSingleton<IRegistryService, FakeRegistryService>();
        services.AddSingleton<ISystemServicesService, FakeServicesService>();
        services.AddSingleton<ITaskSchedulerService, FakeTaskSchedulerService>();
        services.AddSingleton<ISystemRestoreService, FakeRestoreService>();
        services.AddSingleton<IShellService, FakeShellService>();
        services.AddSingleton<IAppSettingsService, FakeAppSettingsService>();

        services.AddSingleton(new RollbackJournalManager(Path.Combine(Path.GetTempPath(), "Tweakora_ArchTest_Journal")));
        services.AddSingleton<IOperationRunner, OperationRunner>();

        services.AddSingleton<ICleanerService, CleanerService>();
        services.AddSingleton<ITweakService, TweakService>();
        services.AddSingleton<IAppInstallService, AppInstallService>();
        services.AddSingleton<IFeatureService, FeatureService>();
        services.AddSingleton<IFixService, FixService>();
        services.AddSingleton<IUpdateModeService, UpdateModeService>();
        services.AddSingleton<IDnsService, DnsService>();
        services.AddSingleton<IStartupService, StartupService>();
        services.AddSingleton<IServicesManagerService, ServicesManagerService>();
        services.AddSingleton<IMemoryOptimizerService, MemoryOptimizerService>();
        services.AddSingleton<INetworkOptimizerService, NetworkOptimizerService>();
        services.AddSingleton<ISmartAdvisorService, SmartAdvisorService>();

        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<InstallViewModel>();
        services.AddSingleton<TweaksViewModel>();
        services.AddSingleton<ConfigViewModel>();
        services.AddSingleton<UpdatesViewModel>();
        services.AddSingleton<ToolboxViewModel>();
        services.AddSingleton<StartupViewModel>();
        services.AddSingleton<ToolsViewModel>();
        services.AddSingleton<HistoryViewModel>();
        services.AddSingleton<FirstRunWizardViewModel>();
        services.AddSingleton<UnifiedExecutionDialogViewModel>();
        services.AddSingleton<MainViewModel>();

        var provider = services.BuildServiceProvider();

        var viewModelTypes = new[]
        {
            typeof(DashboardViewModel),
            typeof(InstallViewModel),
            typeof(TweaksViewModel),
            typeof(ConfigViewModel),
            typeof(UpdatesViewModel),
            typeof(ToolboxViewModel),
            typeof(StartupViewModel),
            typeof(ToolsViewModel),
            typeof(HistoryViewModel),
            typeof(FirstRunWizardViewModel),
            typeof(UnifiedExecutionDialogViewModel),
            typeof(MainViewModel)
        };

        List<string> commandViolations = new();

        foreach (var vmType in viewModelTypes)
        {
            object vm = provider.GetRequiredService(vmType);
            Assert.NotNull(vm);

            var commandProps = vmType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => typeof(ICommand).IsAssignableFrom(p.PropertyType))
                .ToList();

            Assert.NotEmpty(commandProps);

            foreach (var prop in commandProps)
            {
                object? commandVal = prop.GetValue(vm);
                if (commandVal == null)
                {
                    commandViolations.Add($"{vmType.Name}.{prop.Name} is null!");
                }
                else if (commandVal is not ICommand cmd)
                {
                    commandViolations.Add($"{vmType.Name}.{prop.Name} does not implement ICommand!");
                }
            }
        }

        Assert.True(commandViolations.Count == 0,
            "Architecture violation: Null or invalid ICommand found in ViewModels:\n" + string.Join("\n", commandViolations));
    }

    [Fact]
    public void Accessibility_AllInteractiveControlsInViews_HaveAutomationPropertiesName()
    {
        string? solutionRoot = FindSolutionRoot();
        Assert.NotNull(solutionRoot);
        string viewsDir = Path.Combine(solutionRoot, "src", "Tweakora.App", "Views");
        Assert.True(Directory.Exists(viewsDir), $"Views directory not found at {viewsDir}");

        List<string> missingControls = new();
        foreach (var file in Directory.GetFiles(viewsDir, "*.xaml"))
        {
            var xml = new System.Xml.XmlDocument();
            xml.Load(file);
            var nodes = xml.SelectNodes("//*[local-name()='Button' or local-name()='CheckBox' or local-name()='TextBox' or local-name()='RadioButton' or local-name()='ToggleSwitch']");
            if (nodes != null)
            {
                foreach (System.Xml.XmlNode node in nodes)
                {
                    string? name = node.Attributes?["AutomationProperties.Name"]?.Value;
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        missingControls.Add($"{Path.GetFileName(file)}: {node.OuterXml.Substring(0, Math.Min(120, node.OuterXml.Length))}");
                    }
                }
            }
        }

        Assert.True(missingControls.Count == 0,
            $"Accessibility violation: The following interactive controls lack AutomationProperties.Name:\n{string.Join("\n", missingControls)}");
    }

    private static bool IsForbiddenSimulationType(Type? type)
    {
        if (type == null) return false;

        // Check namespace
        if (type.Namespace != null && type.Namespace.Contains("Tweakora.Simulation", StringComparison.OrdinalIgnoreCase))
            return true;

        // Check type name
        if (type.Name.StartsWith("Fake", StringComparison.OrdinalIgnoreCase))
            return true;

        // Check generic arguments
        if (type.IsGenericType)
        {
            foreach (var arg in type.GetGenericArguments())
            {
                if (IsForbiddenSimulationType(arg)) return true;
            }
        }

        return false;
    }

    private static string? FindSolutionRoot()
    {
        string? current = AppDomain.CurrentDomain.BaseDirectory;
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

    [Fact]
    public void Architecture_TestModeJournalPath_IsNeverAppDataPath()
    {
        string realAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string realLocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string realWinVeloAppData = Path.Combine(realAppData, "WinVelo");
        string realWinVeloLocalAppData = Path.Combine(realLocalAppData, "WinVelo");

        // 1. Verify test mode paths are completely isolated from real WinVelo AppData and LocalAppData
        string testJournalDir = WinVeloStoragePaths.GetJournalDirectory(isTestMode: true);
        string testSettingsPath = WinVeloStoragePaths.GetSettingsPath(isTestMode: true);
        string testLogsDir = WinVeloStoragePaths.GetLogsDirectory(isTestMode: true);
        string testRoot = WinVeloStoragePaths.GetStorageRoot(isTestMode: true);

        Assert.DoesNotContain(realWinVeloAppData, testJournalDir, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(realWinVeloAppData, testSettingsPath, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(realWinVeloLocalAppData, testLogsDir, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(realAppData, testJournalDir, StringComparison.OrdinalIgnoreCase);

        Assert.StartsWith(Path.GetTempPath(), testJournalDir, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(Path.GetTempPath(), testSettingsPath, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(Path.GetTempPath(), testLogsDir, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(Path.GetTempPath(), testRoot, StringComparison.OrdinalIgnoreCase);

        Assert.Contains(WinVeloStoragePaths.TestModeFolderName, testJournalDir, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(WinVeloStoragePaths.TestModeFolderName, testSettingsPath, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(WinVeloStoragePaths.TestModeFolderName, testLogsDir, StringComparison.OrdinalIgnoreCase);

        // 2. Verify real mode paths correctly target real WinVelo AppData
        string realJournalDir = WinVeloStoragePaths.GetJournalDirectory(isTestMode: false);
        string realSettingsPath = WinVeloStoragePaths.GetSettingsPath(isTestMode: false);
        string realLogsDir = WinVeloStoragePaths.GetLogsDirectory(isTestMode: false);

        Assert.NotEqual(realJournalDir, testJournalDir, StringComparer.OrdinalIgnoreCase);
        Assert.NotEqual(realSettingsPath, testSettingsPath, StringComparer.OrdinalIgnoreCase);
        Assert.NotEqual(realLogsDir, testLogsDir, StringComparer.OrdinalIgnoreCase);

        Assert.StartsWith(realWinVeloAppData, realJournalDir, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(realWinVeloAppData, realSettingsPath, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(realWinVeloLocalAppData, realLogsDir, StringComparison.OrdinalIgnoreCase);
    }
}
