using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Tweakora.App.ViewModels;
using Tweakora.Core.Engine;
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
        Assert.Equal("DEFAULT", card.StatusBadgeText);

        // Partial
        card.State = TweakState.Partial;
        Assert.False(card.IsActive);
        Assert.Null(card.ToggleCheckedState); // Indeterminate null
        Assert.Equal("PARTIAL ⚠", card.StatusBadgeText);

        // Applied
        card.State = TweakState.Applied;
        Assert.True(card.IsActive);
        Assert.True(card.ToggleCheckedState);
        Assert.Equal("APPLIED ✓", card.StatusBadgeText);

        // Unknown
        card.State = TweakState.Unknown;
        Assert.False(card.IsActive);
        Assert.False(card.ToggleCheckedState);
        Assert.Equal("CAN'T VERIFY", card.StatusBadgeText);
    }
}
