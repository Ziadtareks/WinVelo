using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Tweakora.Modules.Startup;
using Tweakora.Simulation;
using Xunit;

namespace Tweakora.Tests.ModuleTests;

public sealed class StartupManagerTests
{
    [Fact]
    public void StartupApprovedHelper_IsEnabled_CorrectlyParsesBinaryFormat()
    {
        // Null or empty => default enabled
        Assert.True(StartupApprovedHelper.IsEnabled(null));
        Assert.True(StartupApprovedHelper.IsEnabled(Array.Empty<byte>()));

        // Bit 0 == 0 => Enabled
        Assert.True(StartupApprovedHelper.IsEnabled(new byte[] { 0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }));
        Assert.True(StartupApprovedHelper.IsEnabled(new byte[] { 0x00, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }));
        Assert.True(StartupApprovedHelper.IsEnabled(new byte[] { 0x06, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }));

        // Bit 0 == 1 => Disabled
        Assert.False(StartupApprovedHelper.IsEnabled(new byte[] { 0x03, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }));
        Assert.False(StartupApprovedHelper.IsEnabled(new byte[] { 0x01, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }));
        Assert.False(StartupApprovedHelper.IsEnabled(new byte[] { 0x07, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }));
    }

    [Fact]
    public void StartupApprovedHelper_CreateEnabledBytes_Returns12BytesWithBit0Clear()
    {
        byte[] enabled = StartupApprovedHelper.CreateEnabledBytes();

        Assert.Equal(12, enabled.Length);
        Assert.Equal(0x02, enabled[0]);
        Assert.Equal(0, enabled[0] & 1); // Low bit is 0
    }

    [Fact]
    public void StartupApprovedHelper_CreateDisabledBytes_EncodesCurrentFileTime()
    {
        long beforeTime = DateTime.UtcNow.ToFileTimeUtc();
        byte[] disabled = StartupApprovedHelper.CreateDisabledBytes();
        long afterTime = DateTime.UtcNow.ToFileTimeUtc();

        Assert.Equal(12, disabled.Length);
        Assert.Equal(0x03, disabled[0]);
        Assert.Equal(1, disabled[0] & 1); // Low bit is 1

        long recordedTime = BitConverter.ToInt64(disabled, 4);
        Assert.InRange(recordedTime, beforeTime - 10000, afterTime + 10000);
    }

    [Theory]
    [InlineData("\"C:\\Program Files\\Discord\\Update.exe\" --processStart Discord.exe", "C:\\Program Files\\Discord\\Update.exe")]
    [InlineData("\"C:\\Program Files (x86)\\Steam\\steam.exe\" -silent", "C:\\Program Files (x86)\\Steam\\steam.exe")]
    [InlineData("C:\\Windows\\System32\\SecurityHealthSystray.exe", "C:\\Windows\\System32\\SecurityHealthSystray.exe")]
    [InlineData("C:\\Program Files\\App\\app.exe /minimized", "C:\\Program Files\\App\\app.exe")]
    public void StartupPathHelper_ExtractCleanExePath_ExtractsCorrectPath(string raw, string expected)
    {
        string clean = StartupPathHelper.ExtractCleanExePath(raw);
        Assert.Equal(expected, clean);
    }

    [Fact]
    public void StartupImpactEvaluator_CalculateImpact_RatesHeavyLaunchersAsHigh()
    {
        var (impactDiscord, delayDiscord) = StartupImpactEvaluator.CalculateImpact(
            "Discord", "C:\\Discord\\app.exe", 0, "Discord.exe");
        Assert.Equal("High", impactDiscord);
        Assert.Equal(1.2, delayDiscord);

        var (impactSteam, delaySteam) = StartupImpactEvaluator.CalculateImpact(
            "Steam", "C:\\Steam\\steam.exe", 0, "steam.exe -silent");
        Assert.Equal("High", impactSteam);
        Assert.Equal(1.2, delaySteam);

        var (impactSpotify, delaySpotify) = StartupImpactEvaluator.CalculateImpact(
            "Spotify", "C:\\Spotify\\spotify.exe", 0, "spotify.exe");
        Assert.Equal("High", impactSpotify);
        Assert.Equal(1.2, delaySpotify);
    }

    [Fact]
    public void StartupImpactEvaluator_CalculateImpact_RatesLargeFilesAsHigh()
    {
        // 20MB generic app => High
        var (impact, delay) = StartupImpactEvaluator.CalculateImpact(
            "MyCustomApp", "C:\\app.exe", 20 * 1024 * 1024, "app.exe");
        Assert.Equal("High", impact);
        Assert.Equal(1.2, delay);
    }

    [Fact]
    public void StartupImpactEvaluator_CalculateImpact_RatesSmallHelpersAsLowOrMedium()
    {
        // 500KB tool => Low
        var (impactLow, _) = StartupImpactEvaluator.CalculateImpact(
            "7-Zip Shell Helper", "C:\\7z.exe", 500 * 1024, "7z.exe");
        Assert.Equal("Low", impactLow);

        // Updater with args => Medium
        var (impactMed, _) = StartupImpactEvaluator.CalculateImpact(
            "Generic Updater", "C:\\updater.exe", 1024 * 1024, "updater.exe --check --background --sync");
        Assert.Equal("Medium", impactMed);
    }

    [Theory]
    [InlineData("SecurityHealth", "C:\\Windows\\System32\\SecurityHealthSystray.exe", true)]
    [InlineData("RtkAudUService", "C:\\Program Files\\Realtek\\Audio\\HDA\\RtkAudUService64.exe", true)]
    [InlineData("NVDisplay.Container", "C:\\Program Files\\NVIDIA Corporation\\Display.NvContainer\\nvcontainer.exe", true)]
    [InlineData("Discord", "C:\\Users\\User\\AppData\\Local\\Discord\\Discord.exe", false)]
    [InlineData("Spotify", "C:\\Users\\User\\AppData\\Roaming\\Spotify\\Spotify.exe", false)]
    public void StartupImpactEvaluator_IsEssential_ProtectsCoreSystemComponents(string name, string exe, bool expected)
    {
        bool essential = StartupImpactEvaluator.IsEssential(name, exe);
        Assert.Equal(expected, essential);
    }

    [Fact]
    public async Task StartupService_ScanAllSources_DiscoversAppsAndPreservesRunEntries()
    {
        var fakeReg = new FakeRegistryService();
        var service = new StartupService(fakeReg);

        var entries = await service.GetStartupEntriesAsync();

        Assert.NotEmpty(entries);
        Assert.Contains(entries, e => e.Name == "Discord");
        Assert.Contains(entries, e => e.Name == "Steam");
        Assert.Contains(entries, e => e.Name == "Spotify");
        Assert.Contains(entries, e => e.Name == "SecurityHealth");
        Assert.Contains(entries, e => e.Name == "Adobe Creative Cloud");

        // Verify Essential flag on system items
        var secHealth = entries.First(e => e.Name == "SecurityHealth");
        Assert.True(secHealth.IsEssentialSystemItem);

        // Toggle Discord state to Disabled
        var discord = entries.First(e => e.Name == "Discord");
        Assert.True(discord.IsEnabled);

        bool disabledOk = await service.SetEntryStateAsync(discord, false);
        Assert.True(disabledOk);
        Assert.False(discord.IsEnabled);

        // Command must still exist in Run key (never deleted)
        Assert.True(fakeReg.ValueExists("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Run", "Discord"));

        // StartupApproved key must have 12-byte disabled buffer
        var approvedBytes = fakeReg.GetValue("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run", "Discord") as byte[];
        Assert.NotNull(approvedBytes);
        Assert.Equal(12, approvedBytes.Length);
        Assert.Equal(1, approvedBytes[0] & 1); // Disabled bit is set

        // Re-enable Discord
        bool enabledOk = await service.SetEntryStateAsync(discord, true);
        Assert.True(enabledOk);
        Assert.True(discord.IsEnabled);

        var reApprovedBytes = fakeReg.GetValue("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run", "Discord") as byte[];
        Assert.NotNull(reApprovedBytes);
        Assert.Equal(0, reApprovedBytes[0] & 1); // Enabled bit is cleared
    }

    [Fact]
    public async Task StartupService_OptimizeStartupAsync_DisablesHeavyAppsExceptEssential()
    {
        var fakeReg = new FakeRegistryService();
        var service = new StartupService(fakeReg);

        var entriesBefore = await service.GetStartupEntriesAsync();
        var secHealthBefore = entriesBefore.First(e => e.Name == "SecurityHealth");
        Assert.True(secHealthBefore.IsEnabled);
        Assert.True(secHealthBefore.IsEssentialSystemItem);

        int disabledCount = await service.OptimizeStartupAsync();
        Assert.True(disabledCount > 0);

        var entriesAfter = await service.GetStartupEntriesAsync();

        // Essential item must remain enabled
        var secHealthAfter = entriesAfter.First(e => e.Name == "SecurityHealth");
        Assert.True(secHealthAfter.IsEnabled);

        // Non-essential heavy items like Discord or Steam must now be disabled
        var discordAfter = entriesAfter.First(e => e.Name == "Discord");
        Assert.False(discordAfter.IsEnabled);

        var steamAfter = entriesAfter.First(e => e.Name == "Steam");
        Assert.False(steamAfter.IsEnabled);
    }

    [Fact]
    public async Task StartupViewModel_FilterAndToggle_UpdatesMetricsAndFilteredCollection()
    {
        var fakeReg = new FakeRegistryService();
        var service = new StartupService(fakeReg);
        var vm = new Tweakora.App.ViewModels.StartupViewModel(service);

        // Allow initial async load
        await vm.RefreshCommandAsync();

        Assert.True(vm.TotalCount >= 8);
        Assert.True(vm.EnabledCount > 0);
        Assert.True(vm.HighImpactCount > 0);
        Assert.True(vm.BootDelaySeconds > 0);

        // Filter by Disabled
        vm.SetFilter("Disabled");
        Assert.All(vm.FilteredApps, a => Assert.False(a.IsEnabled));
        Assert.Contains(vm.FilteredApps, a => a.Name == "Telegram");

        // Filter by HighImpact
        vm.SetFilter("HighImpact");
        Assert.All(vm.FilteredApps, a => Assert.Equal("High", a.Impact));

        // Search filter
        vm.SetFilter("All");
        vm.SearchText = "Discord";
        Assert.Single(vm.FilteredApps);
        Assert.Equal("Discord", vm.FilteredApps[0].Name);

        // Toggle Discord
        var discord = vm.FilteredApps[0];
        int enabledBefore = vm.EnabledCount;
        await vm.ToggleAppAsync(discord);
        Assert.False(discord.IsEnabled);
        Assert.Equal(enabledBefore - 1, vm.EnabledCount);

        // Clear search
        vm.SearchText = string.Empty;

        // Run 1-Click Startup Optimization
        await vm.OptimizeStartupAsync();
        Assert.Equal(0, vm.HighImpactCount);
        Assert.Contains("boot time saved", vm.BootTimeSavedText);
    }
}
