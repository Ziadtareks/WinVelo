using System.Text.Json;
using Tweakora.Core.Models;
using Tweakora.Core.Profiles;
using Xunit;

namespace Tweakora.Tests.ProfileTests;

public class ProfileManagerTests
{
    [Fact]
    public void GetBuiltInProfiles_ReturnsExpectedFourProfiles()
    {
        var profiles = ProfileManager.GetBuiltInProfiles();
        Assert.NotNull(profiles);
        Assert.Equal(4, profiles.Count);

        var ids = profiles.Select(p => p.Id).ToList();
        Assert.Contains("preset.recommended", ids);
        Assert.Contains("preset.privacy", ids);
        Assert.Contains("preset.gaming", ids);
        Assert.Contains("preset.minimal", ids);

        foreach (var profile in profiles)
        {
            Assert.False(string.IsNullOrWhiteSpace(profile.Name));
            Assert.False(string.IsNullOrWhiteSpace(profile.Description));
            Assert.NotEmpty(profile.TweakIds);
        }
    }

    [Fact]
    public void ExportAndImportJson_PreservesProfileStructure()
    {
        var profile = new TweakoraProfile
        {
            Id = "preset.custom-test",
            Name = "Custom Test Profile",
            Description = "A test profile for roundtrip serialization.",
            TweakIds = new List<string> { "privacy.telemetry.diagnostic-data", "gaming.mode.enable" }
        };

        string json = ProfileManager.ExportProfileToJson(profile);
        Assert.False(string.IsNullOrWhiteSpace(json));

        var imported = ProfileManager.ImportProfileFromJson(json);
        Assert.NotNull(imported);
        Assert.Equal(profile.Id, imported.Id);
        Assert.Equal(profile.Name, imported.Name);
        Assert.Equal(profile.Description, imported.Description);
        Assert.Equal(profile.TweakIds.Count, imported.TweakIds.Count);
        Assert.Equal(profile.TweakIds, imported.TweakIds);
    }

    [Fact]
    public void ExportToReg_ProducesValidWindowsRegistryContent()
    {
        var profile = new TweakoraProfile
        {
            Id = "preset.reg-test",
            Name = "Registry Test Profile",
            Description = "Testing registry file generation.",
            TweakIds = new List<string> { "test.tweak1", "test.tweak2" }
        };

        var tweaks = new List<TweakDefinition>
        {
            new()
            {
                Id = "test.tweak1",
                Name = "Test Tweak 1",
                Description = "Desc 1",
                Apply = new List<ActionItem>
                {
                    new()
                    {
                        Type = "Registry",
                        Hive = "HKCU",
                        Key = @"Software\TestCompany\App",
                        Operation = "Set",
                        ValueName = "FeatureEnabled",
                        ValueKind = "DWord",
                        Data = JsonDocument.Parse("1").RootElement
                    }
                }
            },
            new()
            {
                Id = "test.tweak2",
                Name = "Test Tweak 2",
                Description = "Desc 2",
                Apply = new List<ActionItem>
                {
                    new()
                    {
                        Type = "Registry",
                        Hive = "HKLM",
                        Key = @"Software\Policies\Microsoft\Windows\DataCollection",
                        Operation = "DeleteValue",
                        ValueName = "OldSetting"
                    }
                }
            }
        };

        string regContent = ProfileManager.ExportProfileToReg(profile, tweaks);
        Assert.Contains("Windows Registry Editor Version 5.00", regContent);
        Assert.Contains(@"[HKCU\Software\TestCompany\App]", regContent);
        Assert.Contains("\"FeatureEnabled\"=dword:00000001", regContent);
        Assert.Contains(@"[HKLM\Software\Policies\Microsoft\Windows\DataCollection]", regContent);
        Assert.Contains("\"OldSetting\"=-", regContent);
    }

    [Fact]
    public void ExportToPowerShell_ProducesUnattendedScript()
    {
        var profile = new TweakoraProfile
        {
            Id = "preset.ps-test",
            Name = "PowerShell Test Profile",
            Description = "Testing powershell script generation.",
            TweakIds = new List<string> { "test.tweak1" }
        };

        var tweaks = new List<TweakDefinition>
        {
            new()
            {
                Id = "test.tweak1",
                Name = "Test Tweak 1",
                Description = "Desc 1",
                Category = "Privacy",
                Apply = new List<ActionItem>
                {
                    new()
                    {
                        Type = "Registry",
                        Hive = "HKCU",
                        Key = @"Software\TestCompany\App",
                        Operation = "Set",
                        ValueName = "OptOut",
                        ValueKind = "DWord",
                        Data = JsonDocument.Parse("1").RootElement
                    }
                }
            }
        };

        string psContent = ProfileManager.ExportProfileToPowerShell(profile, tweaks);
        Assert.Contains("Write-Host", psContent);
        Assert.Contains(@"Set-ItemProperty -Path 'HKCU:\Software\TestCompany\App'", psContent);
        Assert.Contains("-Name 'OptOut' -Value 1", psContent);
    }

    [Fact]
    public void Export_HandlesMissingTweakGracefully()
    {
        var profile = new TweakoraProfile
        {
            Id = "preset.missing-test",
            Name = "Missing Tweaks Test",
            Description = "Testing missing tweak reference.",
            TweakIds = new List<string> { "non.existent.tweak" }
        };

        var emptyTweaks = new List<TweakDefinition>();

        string reg = ProfileManager.ExportProfileToReg(profile, emptyTweaks);
        Assert.Contains("Windows Registry Editor Version 5.00", reg);

        string ps = ProfileManager.ExportProfileToPowerShell(profile, emptyTweaks);
        Assert.Contains("Write-Host", ps);
    }
}
