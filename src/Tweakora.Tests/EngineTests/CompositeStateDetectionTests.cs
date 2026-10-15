using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Tweakora.App.ViewModels;
using Tweakora.Core.Engine;
using Tweakora.Core.Interfaces;
using Tweakora.Core.Models;
using Tweakora.Simulation;
using Xunit;

namespace Tweakora.Tests.EngineTests;

public sealed class CompositeStateDetectionTests
{
    private readonly FakeRegistryService _registry = new();
    private readonly FakeServicesService _services = new();
    private readonly FakeTaskSchedulerService _tasks = new();
    private readonly StateDetector _detector;

    public CompositeStateDetectionTests()
    {
        _detector = new StateDetector(_registry, _services, _tasks);
    }

    [Fact]
    public async Task MultiRegistryTweak_AllMatch_ReportsApplied()
    {
        var tweak = new TweakDefinition
        {
            Id = "test.multi.reg",
            Name = "Multi Registry Tweak",
            Category = "system",
            Risk = "Safe",
            Apply = new List<ActionItem>
            {
                new() { Type = "Registry", Hive = "HKCU", Key = @"Software\Test\Key1", ValueName = "Val1", Operation = "Set", Data = JsonSerializer.SerializeToElement(1) },
                new() { Type = "Registry", Hive = "HKCU", Key = @"Software\Test\Key2", ValueName = "Val2", Operation = "Set", Data = JsonSerializer.SerializeToElement("Enabled") }
            }
        };

        // Neither set -> NotApplied
        var state0 = await _detector.DetectTweakStateAsync(tweak, useCache: false);
        Assert.Equal(TweakState.NotApplied, state0);

        // One set -> Partial
        _registry.SetValue("HKCU", @"Software\Test\Key1", "Val1", 1, "DWord");
        var state1 = await _detector.DetectTweakStateAsync(tweak, useCache: false);
        Assert.Equal(TweakState.Partial, state1);

        // Both set -> Applied
        _registry.SetValue("HKCU", @"Software\Test\Key2", "Val2", "Enabled", "String");
        var state2 = await _detector.DetectTweakStateAsync(tweak, useCache: false);
        Assert.Equal(TweakState.Applied, state2);
    }

    [Fact]
    public async Task MultiServiceTweak_PartialConfiguration_ReportsPartialNotApplied()
    {
        // Replicating essential.services behavior where one service is disabled out of many
        _services.RegisterService("ServiceA", "Running", "Automatic");
        _services.RegisterService("ServiceB", "Running", "Automatic");

        var tweak = new TweakDefinition
        {
            Id = "test.multi.service",
            Name = "Multi Service Tweak",
            Category = "performance",
            Risk = "Safe",
            Apply = new List<ActionItem>
            {
                new() { Type = "Service", ServiceName = "ServiceA", Operation = "SetStartup", StartupType = "Disabled" },
                new() { Type = "Service", ServiceName = "ServiceB", Operation = "SetStartup", StartupType = "Disabled" }
            }
        };

        // Neither disabled -> NotApplied
        var state0 = await _detector.DetectTweakStateAsync(tweak, useCache: false);
        Assert.Equal(TweakState.NotApplied, state0);

        // Only ServiceA disabled -> Partial (preventing the historical BUG-013 false positive!)
        _services.SetStartupType("ServiceA", "Disabled");
        var state1 = await _detector.DetectTweakStateAsync(tweak, useCache: false);
        Assert.Equal(TweakState.Partial, state1);

        // Both disabled -> Applied
        _services.SetStartupType("ServiceB", "Disabled");
        var state2 = await _detector.DetectTweakStateAsync(tweak, useCache: false);
        Assert.Equal(TweakState.Applied, state2);
    }

    [Fact]
    public async Task ServiceStartupType_MatchesDeclaredState()
    {
        _services.RegisterService("TestSvc", "Stopped", "Automatic");

        var tweak = new TweakDefinition
        {
            Id = "test.service.startup",
            Name = "Service Startup Tweak",
            Category = "system",
            Risk = "Safe",
            Apply = new List<ActionItem>
            {
                new() { Type = "Service", ServiceName = "TestSvc", Operation = "SetStartup", StartupType = "Manual" }
            }
        };

        // Automatic != Manual -> NotApplied
        var state0 = await _detector.DetectTweakStateAsync(tweak, useCache: false);
        Assert.Equal(TweakState.NotApplied, state0);

        // Manual == Manual -> Applied
        _services.SetStartupType("TestSvc", "Manual");
        var state1 = await _detector.DetectTweakStateAsync(tweak, useCache: false);
        Assert.Equal(TweakState.Applied, state1);
    }

    [Fact]
    public async Task PurePowerShellTweak_NoInspectableState_ReportsUnknown()
    {
        var tweak = new TweakDefinition
        {
            Id = "test.pure.powershell",
            Name = "Pure PowerShell Tweak",
            Category = "advanced",
            Risk = "Caution",
            Apply = new List<ActionItem>
            {
                new() { Type = "PowerShell", Script = "Write-Output 'Doing some uninspectable task'" }
            }
        };

        var state = await _detector.DetectTweakStateAsync(tweak, useCache: false);
        Assert.Equal(TweakState.Unknown, state);
    }

    [Fact]
    public void TweakCardModel_TriStateBindingAndBadges_ReflectAccurateState()
    {
        var def = new TweakDefinition
        {
            Id = "ui.test.card",
            Name = "UI Test Card",
            Category = "system",
            Risk = "Safe"
        };

        var card = new TweakCardModel(def);

        // Default / NotApplied
        card.State = TweakState.NotApplied;
        Assert.False(card.IsActive);
        Assert.False(card.ToggleCheckedState);
        Assert.Equal("Not applied", card.StatusBadgeText);

        // Partial
        card.State = TweakState.Partial;
        Assert.False(card.IsActive);
        Assert.Null(card.ToggleCheckedState); // Indeterminate null
        Assert.Equal("Partial", card.StatusBadgeText);

        // Applied
        card.State = TweakState.Applied;
        Assert.True(card.IsActive);
        Assert.True(card.ToggleCheckedState);
        Assert.Equal("Applied", card.StatusBadgeText);

        // Unknown
        card.State = TweakState.Unknown;
        Assert.False(card.IsActive);
        Assert.False(card.ToggleCheckedState);
        Assert.Equal("Can't verify", card.StatusBadgeText);
    }

    [Fact]
    public async Task DetectAny_AllApplyActionsAreAlternatives_AnyMatchReportsApplied()
    {
        // Replicating pref.bingsearch pattern: 2 alternative registry keys for the same setting
        var tweak = new TweakDefinition
        {
            Id = "test.alt.tweak",
            Name = "Alternative Setting Tweak",
            Category = "preferences",
            Risk = "Safe",
            Detect = new DetectStructure
            {
                Any = new List<DetectRule>
                {
                    new() { Type = "Registry", Hive = "HKCU", Key = @"Software\Policies\Test", ValueName = "SettingAlt1", ExpectedValue = JsonSerializer.SerializeToElement(1), MatchType = "Equals" },
                    new() { Type = "Registry", Hive = "HKCU", Key = @"Software\App\Test", ValueName = "SettingAlt2", ExpectedValue = JsonSerializer.SerializeToElement(0), MatchType = "Equals" }
                }
            },
            Apply = new List<ActionItem>
            {
                new() { Type = "Registry", Hive = "HKCU", Key = @"Software\Policies\Test", ValueName = "SettingAlt1", Operation = "Set", Data = JsonSerializer.SerializeToElement(1) },
                new() { Type = "Registry", Hive = "HKCU", Key = @"Software\App\Test", ValueName = "SettingAlt2", Operation = "Set", Data = JsonSerializer.SerializeToElement(0) }
            }
        };

        // Neither set -> NotApplied
        var state0 = await _detector.DetectTweakStateAsync(tweak, useCache: false);
        Assert.Equal(TweakState.NotApplied, state0);

        // Setting only Alt1 -> Applied (because all actions are mutually alternative keys for the same setting)
        _registry.SetValue("HKCU", @"Software\Policies\Test", "SettingAlt1", 1, "DWord");
        var state1 = await _detector.DetectTweakStateAsync(tweak, useCache: false);
        Assert.Equal(TweakState.Applied, state1);

        // Reverting Alt1 and setting Alt2 -> Applied
        _registry.SetValue("HKCU", @"Software\Policies\Test", "SettingAlt1", 0, "DWord");
        _registry.SetValue("HKCU", @"Software\App\Test", "SettingAlt2", 0, "DWord");
        var state2 = await _detector.DetectTweakStateAsync(tweak, useCache: false);
        Assert.Equal(TweakState.Applied, state2);
    }

    [Fact]
    public async Task CompositeTweak_WithDetectAny_OnePolicyKeySet_ReportsPartialNotApplied()
    {
        // Replicating essential.telemetry pattern: 3 actions, but detect.any has alternative rules for only 1 action
        var tweak = new TweakDefinition
        {
            Id = "test.composite.telemetry",
            Name = "Composite Telemetry Tweak",
            Category = "privacy",
            Risk = "Safe",
            Detect = new DetectStructure
            {
                Any = new List<DetectRule>
                {
                    // GPO key alternative for AllowTelemetry
                    new() { Type = "Registry", Hive = "HKLM", Key = @"SOFTWARE\Policies\DataCollection", ValueName = "AllowTelemetry", ExpectedValue = JsonSerializer.SerializeToElement(0), MatchType = "Equals" },
                    // Standard policy key for AllowTelemetry
                    new() { Type = "Registry", Hive = "HKLM", Key = @"SOFTWARE\Microsoft\Policies\DataCollection", ValueName = "AllowTelemetry", ExpectedValue = JsonSerializer.SerializeToElement(0), MatchType = "Equals" }
                }
            },
            Apply = new List<ActionItem>
            {
                new() { Type = "Registry", Hive = "HKCU", Key = @"Software\Advertising", ValueName = "Enabled", Operation = "Set", Data = JsonSerializer.SerializeToElement(0) },
                new() { Type = "Registry", Hive = "HKCU", Key = @"Software\Privacy", ValueName = "Tailored", Operation = "Set", Data = JsonSerializer.SerializeToElement(0) },
                new() { Type = "Registry", Hive = "HKLM", Key = @"SOFTWARE\Microsoft\Policies\DataCollection", ValueName = "AllowTelemetry", Operation = "Set", Data = JsonSerializer.SerializeToElement(0) }
            }
        };

        // Neither set -> NotApplied
        var state0 = await _detector.DetectTweakStateAsync(tweak, useCache: false);
        Assert.Equal(TweakState.NotApplied, state0);

        // Setting ONLY the GPO key for AllowTelemetry -> must report Partial (honest engine!), NEVER Applied
        _registry.SetValue("HKLM", @"SOFTWARE\Policies\DataCollection", "AllowTelemetry", 0, "DWord");
        var state1 = await _detector.DetectTweakStateAsync(tweak, useCache: false);
        Assert.Equal(TweakState.Partial, state1);
        Assert.False(await _detector.IsTweakActiveAsync(tweak, useCache: false));

        // Setting all 3 values (AllowTelemetry via GPO, plus Advertising and Tailored) -> Applied
        _registry.SetValue("HKCU", @"Software\Advertising", "Enabled", 0, "DWord");
        _registry.SetValue("HKCU", @"Software\Privacy", "Tailored", 0, "DWord");
        var state2 = await _detector.DetectTweakStateAsync(tweak, useCache: false);
        Assert.Equal(TweakState.Applied, state2);
        Assert.True(await _detector.IsTweakActiveAsync(tweak, useCache: false));
    }

    [Fact]
    public async Task DetectTweakStateAsync_ThrowsException_ReturnsUnknown()
    {
        // When registry service throws unexpected exception, detection fails safe to Unknown
        var throwingRegistry = new ThrowingRegistryService();
        var detector = new StateDetector(throwingRegistry, _services, _tasks);

        var tweak = new TweakDefinition
        {
            Id = "test.throwing",
            Name = "Throwing Tweak",
            Category = "system",
            Risk = "Safe",
            Apply = new List<ActionItem>
            {
                new() { Type = "Registry", Hive = "HKCU", Key = @"Software\Crash", ValueName = "Val", Operation = "Set", Data = JsonSerializer.SerializeToElement(1) }
            }
        };

        var state = await detector.DetectTweakStateAsync(tweak, useCache: false);
        Assert.Equal(TweakState.Unknown, state);
    }

    private sealed class ThrowingRegistryService : IRegistryService
    {
        public bool KeyExists(string hive, string key) => throw new InvalidOperationException("Registry error");
        public bool ValueExists(string hive, string key, string valueName) => throw new InvalidOperationException("Registry error");
        public object? GetValue(string hive, string key, string valueName) => throw new InvalidOperationException("Registry error");
        public string? GetValueKind(string hive, string key, string valueName) => throw new InvalidOperationException("Registry error");
        public void SetValue(string hive, string key, string valueName, object data, string valueKind) => throw new InvalidOperationException("Registry error");
        public void DeleteValue(string hive, string key, string valueName) => throw new InvalidOperationException("Registry error");
        public void DeleteKey(string hive, string key) => throw new InvalidOperationException("Registry error");
        public void CreateKey(string hive, string key) => throw new InvalidOperationException("Registry error");
        public string ExportRegBackup(string hive, string key, string backupFolder) => throw new InvalidOperationException("Registry error");
        public IEnumerable<string> GetValueNames(string hive, string key) => throw new InvalidOperationException("Registry error");
        public IEnumerable<string> GetSubKeyNames(string hive, string key) => throw new InvalidOperationException("Registry error");
    }
}
