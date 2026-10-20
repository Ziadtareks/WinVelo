using System.Linq;
using WinVelo.Core.Catalog;
using WinVelo.Core.Profiles;
using Xunit;

namespace WinVelo.Tests;

/// <summary>Config files from WinVelo and from WinUtil must both import, and WinUtil names must find WinVelo tweaks.</summary>
public sealed class ConfigImportTests
{
    [Fact]
    public void WinUtilFlatArray_IsSortedIntoAppsTweaksFeaturesAndAppx()
    {
        var config = ConfigManager.ImportConfigJson("""
            ["WPFInstall7zip", "WPFTweaksTelemetry", "WPFToggleDarkMode", "WPFFeatureswsl", "WPFAppxMicrosoft_BingNews"]
            """);

        Assert.NotNull(config);
        Assert.Equal(new[] { "7zip" }, config!.SelectedApps);
        Assert.Equal(new[] { "WPFTweaksTelemetry", "WPFToggleDarkMode" }, config.SelectedTweaks);
        Assert.Equal(new[] { "WPFFeatureswsl" }, config.SelectedFeatures);
        Assert.Equal(new[] { "WPFAppxMicrosoft_BingNews" }, config.SelectedAppx);
    }

    [Fact]
    public void LegacyWinUtilObject_IsFlattened()
    {
        var config = ConfigManager.ImportConfigJson("""
            { "Install": [ { "winget": "7zip.7zip" } ], "WPFInstall": ["WPFInstall7zip"], "WPFTweaks": ["WPFTweaksWPBT"] }
            """);

        Assert.NotNull(config);
        Assert.Equal(new[] { "7zip" }, config!.SelectedApps);
        Assert.Equal(new[] { "WPFTweaksWPBT" }, config.SelectedTweaks);
    }

    [Fact]
    public void WinVeloConfig_RoundTrips_AndIgnoresPropertyCase()
    {
        var original = new WinVeloConfig { SelectedTweaks = { "essential.telemetry" }, SelectedApps = { "vlc" }, SelectedAppx = { "WPFAppxClipchamp_Clipchamp" } };
        var back = ConfigManager.ImportConfigJson(ConfigManager.ExportConfigJson(original));
        Assert.Equal(original.SelectedTweaks, back!.SelectedTweaks);
        Assert.Equal(original.SelectedApps, back.SelectedApps);
        Assert.Equal(original.SelectedAppx, back.SelectedAppx);

        var pascal = ConfigManager.ImportConfigJson("""{ "SelectedTweaks": ["essential.wpbt"] }""");
        Assert.Equal(new[] { "essential.wpbt" }, pascal!.SelectedTweaks);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("42")]
    public void Garbage_ReturnsNull(string text) => Assert.Null(ConfigManager.ImportConfigJson(text));

    [Theory]
    [InlineData("WPFTweaksDeliveryOptimization")]
    [InlineData("WPFTweaksEndTaskOnTaskbar")]
    [InlineData("WPFTweaksRightClickMenu")]
    [InlineData("WPFToggleShowExt")]
    public void WinUtilAliasOnlyNames_ResolveToACatalogTweak(string name)
    {
        var tweaks = CatalogLoader.LoadTweaks(CatalogSource.EmbeddedOnly()).Items;
        Assert.NotNull(TweakResolver.Find(tweaks, name));
    }

    [Fact]
    public void EveryTweakInTheWinUtilPresets_ResolvesToACatalogTweak()
    {
        var tweaks = CatalogLoader.LoadTweaks(CatalogSource.EmbeddedOnly()).Items;
        using var doc = CatalogLoader.LoadDocument(CatalogSource.EmbeddedOnly(), "Config/preset.json");
        foreach (string preset in new[] { "Standard", "Minimal", "Advanced" })
        {
            var missing = doc!.RootElement.GetProperty(preset).EnumerateArray()
                .Select(e => e.GetString()!)
                .Where(k => TweakResolver.Find(tweaks, k) == null)
                .ToList();
            Assert.True(missing.Count == 0, $"{preset}: {string.Join(", ", missing)}");
        }
    }
}
