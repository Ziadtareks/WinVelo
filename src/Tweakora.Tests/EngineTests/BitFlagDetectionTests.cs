using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Tweakora.Core.Engine;
using Tweakora.Core.Models;
using Tweakora.Simulation;
using Xunit;

namespace Tweakora.Tests.EngineTests;

public class BitFlagDetectionTests
{
    private const string Key = @"SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters";

    private static TweakDefinition BitTweak(string matchType, int mask, int applyData) => new()
    {
        Id = "test.bits",
        Name = "Bits",
        Description = "Test",
        Category = "network",
        Risk = "Low",
        Apply = new List<ActionItem>
        {
            new() { Type = "Registry", Hive = "HKLM", Key = Key, ValueName = "DisabledComponents", Operation = "Set", ValueKind = "DWord", Data = JsonSerializer.SerializeToElement(applyData) }
        },
        Detect = new DetectStructure
        {
            Any = new List<DetectRule>
            {
                new() { Type = "Registry", Hive = "HKLM", Key = Key, ValueName = "DisabledComponents", MatchType = matchType, ExpectedValue = JsonSerializer.SerializeToElement(mask) }
            }
        }
    };

    [Theory]
    [InlineData("BitsAnySet", 9, 8, 1, true)]     // all tunnels disabled also disables Teredo
    [InlineData("BitsAnySet", 9, 8, 255, true)]
    [InlineData("BitsAnySet", 9, 8, 32, false)]
    [InlineData("BitsAllSet", 32, 32, 255, true)]
    [InlineData("BitsAllSet", 32, 32, 1, false)]
    [InlineData("BitsClear", 5, 506, 0, true)]     // StickyKeys off with the shortcut off
    [InlineData("BitsClear", 5, 506, 510, false)]  // Windows default: shortcut on
    public async Task BitRules_MatchTheEffect_NotTheExactNumber(string matchType, int mask, int applyData, int stored, bool expected)
    {
        var registry = new FakeRegistryService();
        var detector = new StateDetector(registry, new FakeServicesService(), new FakeTaskSchedulerService());
        var tweak = BitTweak(matchType, mask, applyData);

        Assert.False(await detector.IsTweakActiveAsync(tweak, useCache: false)); // missing value never matches

        registry.SetValue("HKLM", Key, "DisabledComponents", stored, "DWord");
        Assert.Equal(expected, await detector.IsTweakActiveAsync(tweak, useCache: false));
    }

    [Fact]
    public async Task DwordMinusOne_MatchesUnsignedReadBack()
    {
        var registry = new FakeRegistryService();
        var detector = new StateDetector(registry, new FakeServicesService(), new FakeTaskSchedulerService());
        var tweak = new TweakDefinition
        {
            Id = "test.dword",
            Name = "Dword",
            Description = "Test",
            Category = "performance",
            Risk = "Low",
            Apply = new List<ActionItem>
            {
                new() { Type = "Registry", Hive = "HKLM", Key = @"SOFTWARE\Test", ValueName = "NetworkThrottlingIndex", Operation = "Set", ValueKind = "DWord", Data = JsonSerializer.SerializeToElement(-1) }
            }
        };

        registry.SetValue("HKLM", @"SOFTWARE\Test", "NetworkThrottlingIndex", 4294967295L, "DWord");
        Assert.True(await detector.IsTweakActiveAsync(tweak, useCache: false));
    }
}
