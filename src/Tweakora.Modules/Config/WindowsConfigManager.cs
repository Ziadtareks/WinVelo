using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Tweakora.Core.Interfaces;

namespace Tweakora.Modules.Config;

public sealed partial class FeatureItemViewModel : ObservableObject
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string Category { get; init; }
    public List<string> FeatureNames { get; init; } = new();
    public List<string> InvokeScripts { get; init; } = new();

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isInstalled;

    [ObservableProperty]
    private bool _isBusy;
}

public sealed class LegacyPanelItem
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    public required string Command { get; init; }
    public string Icon { get; init; } = "Apps24";
}

public sealed class WindowsConfigManager
{
    private readonly IShellService _shell;
    public bool IsSimulationMode { get; set; } = true;

    public event Action<string>? OnLog;

    public WindowsConfigManager(IShellService shell, bool isSimulationMode = true)
    {
        _shell = shell;
        IsSimulationMode = isSimulationMode;
    }

    public List<FeatureItemViewModel> LoadFeatures()
    {
        string rootDir = AppDomain.CurrentDomain.BaseDirectory;
        string configPath = Path.Combine(rootDir, "Catalog", "Config", "feature.json");

        if (!File.Exists(configPath))
        {
            string? parent = Directory.GetParent(rootDir)?.FullName;
            while (parent != null)
            {
                string candidate = Path.Combine(parent, "Catalog", "Config", "feature.json");
                if (File.Exists(candidate))
                {
                    configPath = candidate;
                    break;
                }
                parent = Directory.GetParent(parent)?.FullName;
            }
        }

        var list = new List<FeatureItemViewModel>();
        if (File.Exists(configPath))
        {
            string json = File.ReadAllText(configPath);
            using var doc = JsonDocument.Parse(json);

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                var elem = prop.Value;
                string cat = elem.TryGetProperty("category", out var cP) ? cP.GetString() ?? "" : "";
                string type = elem.TryGetProperty("Type", out var tP) ? tP.GetString() ?? "" : "";

                // Only include selectable checkboxes under Features category
                if (cat == "Features" && type != "Button")
                {
                    string name = elem.TryGetProperty("Content", out var nP) ? nP.GetString() ?? prop.Name : prop.Name;
                    string desc = elem.TryGetProperty("Description", out var dP) ? dP.GetString() ?? name : name;

                    var feats = new List<string>();
                    if (elem.TryGetProperty("feature", out var fP) && fP.ValueKind == JsonValueKind.Array)
                    {
                        feats.AddRange(fP.EnumerateArray().Select(x => x.GetString() ?? "").Where(s => !string.IsNullOrEmpty(s)));
                    }

                    var scripts = new List<string>();
                    if (elem.TryGetProperty("InvokeScript", out var sP) && sP.ValueKind == JsonValueKind.Array)
                    {
                        scripts.AddRange(sP.EnumerateArray().Select(x => x.GetString() ?? "").Where(s => !string.IsNullOrEmpty(s)));
                    }

                    list.Add(new FeatureItemViewModel
                    {
                        Key = prop.Name,
                        Name = name,
                        Description = desc,
                        Category = cat,
                        FeatureNames = feats,
                        InvokeScripts = scripts
                    });
                }
            }
        }

        return list;
    }

    public List<LegacyPanelItem> GetLegacyPanels()
    {
        return new List<LegacyPanelItem>
        {
            new() { Key = "WPFPanelControl", Name = "Control Panel", Command = "control.exe", Icon = "Settings24" },
            new() { Key = "WPFPanelComputer", Name = "Computer Management", Command = "compmgmt.msc", Icon = "Desktop24" },
            new() { Key = "WPFPanelNetwork", Name = "Network Connections", Command = "ncpa.cpl", Icon = "Globe24" },
            new() { Key = "WPFPanelPower", Name = "Power Options", Command = "powercfg.cpl", Icon = "BatteryCharge24" },
            new() { Key = "WPFPanelPrinter", Name = "Printers & Devices", Command = "control printers", Icon = "Print24" },
            new() { Key = "WPFPanelPrograms", Name = "Programs and Features", Command = "appwiz.cpl", Icon = "Apps24" },
            new() { Key = "WPFPanelRegion", Name = "Region Settings", Command = "intl.cpl", Icon = "LocalLanguage24" },
            new() { Key = "WPFPanelSecurity", Name = "Security & Maintenance", Command = "wscui.cpl", Icon = "Shield24" },
            new() { Key = "WPFPanelSound", Name = "Sound Settings", Command = "mmsys.cpl", Icon = "Speaker224" },
            new() { Key = "WPFPanelSystem", Name = "System Properties", Command = "sysdm.cpl", Icon = "Info24" },
            new() { Key = "WPFPanelTimedate", Name = "Time and Date", Command = "timedate.cpl", Icon = "Clock24" },
            new() { Key = "WPFPanelFirewall", Name = "Windows Firewall", Command = "firewall.cpl", Icon = "LockClosed24" },
            new() { Key = "WPFPanelRestore", Name = "System Restore", Command = "rstrui.exe", Icon = "History24" },
            new() { Key = "WPFPanelMouse", Name = "Mouse Properties", Command = "main.cpl", Icon = "Cursor24" }
        };
    }

    public async Task InstallFeaturesAsync(IEnumerable<FeatureItemViewModel> features)
    {
        var items = features.ToList();
        if (items.Count == 0) return;

        OnLog?.Invoke($"[CONFIG] Installing {items.Count} Windows feature(s) (Simulation: {IsSimulationMode})...");
        foreach (var item in items)
        {
            item.IsBusy = true;
            OnLog?.Invoke($"[FEATURE] Enabling {item.Name}...");

            if (IsSimulationMode)
            {
                await Task.Delay(50);
                item.IsInstalled = true;
            }
            else
            {
                foreach (string fn in item.FeatureNames)
                {
                    await _shell.ExecutePowerShellAsync($"Enable-WindowsOptionalFeature -Online -FeatureName '{fn}' -All -NoRestart", 180);
                }
                foreach (string sc in item.InvokeScripts)
                {
                    await _shell.ExecutePowerShellAsync(sc, 180);
                }
            }
            item.IsBusy = false;
        }
        OnLog?.Invoke($"[CONFIG] Finished processing Windows features.");
    }

    public async Task UninstallFeaturesAsync(IEnumerable<FeatureItemViewModel> features)
    {
        var items = features.ToList();
        if (items.Count == 0) return;

        OnLog?.Invoke($"[CONFIG] Disabling {items.Count} Windows feature(s) (Simulation: {IsSimulationMode})...");
        foreach (var item in items)
        {
            item.IsBusy = true;
            OnLog?.Invoke($"[FEATURE] Disabling {item.Name}...");

            if (IsSimulationMode)
            {
                await Task.Delay(50);
                item.IsInstalled = false;
                item.IsSelected = false;
            }
            else
            {
                foreach (string fn in item.FeatureNames)
                {
                    await _shell.ExecutePowerShellAsync($"Disable-WindowsOptionalFeature -Online -FeatureName '{fn}' -NoRestart", 180);
                }
            }
            item.IsBusy = false;
        }
        OnLog?.Invoke($"[CONFIG] Finished disabling Windows features.");
    }

    public async Task<bool> RunFixAsync(string fixKey)
    {
        switch (fixKey.ToLowerInvariant())
        {
            case "autologon":
                OpenAutologonPage();
                return true;
            case "update_reset":
                await ResetWindowsUpdateAsync();
                return true;
            case "network_reset":
                await ResetNetworkAsync();
                return true;
            case "dism_sfc":
                await RunSystemCorruptionScanAsync();
                return true;
            case "winget_reinstall":
                await ReinstallWinGetAsync();
                return true;
            case "sync_time":
                await SetNtpServerAsync();
                return true;
            default:
                OnLog?.Invoke($"[WARN] Unknown fix key: {fixKey}");
                return false;
        }
    }

    public async Task ConfigureOpenSshAsync(bool enable)
    {
        if (enable)
        {
            await EnableOpenSshServerAsync();
        }
    }

    public void LaunchPanel(LegacyPanelItem panel)
    {
        OnLog?.Invoke($"[PANEL] Launching {panel.Name} ({panel.Command})...");
        _shell.LaunchApplet(panel.Command);
    }

    public void OpenAutologonPage()
    {
        OnLog?.Invoke("[FIX] Opening Sysinternals AutoLogon page...");
        _shell.OpenUrl("https://learn.microsoft.com/en-us/sysinternals/downloads/autologon");
    }

    public async Task<string> RunSystemCorruptionScanAsync()
    {
        OnLog?.Invoke("[SCAN] Initiating SFC and DISM System Corruption Scan...");
        if (IsSimulationMode)
        {
            await Task.Delay(100);
            string mockOutput = "Windows Resource Protection did not find any integrity violations.\n[DISM] No component store corruption detected. The operation completed successfully.";
            OnLog?.Invoke(mockOutput);
            return mockOutput;
        }

        string sfcOut = await _shell.ExecutePowerShellAsync("sfc /scannow", 600);
        string dismOut = await _shell.ExecutePowerShellAsync("DISM /Online /Cleanup-Image /RestoreHealth", 600);
        return $"{sfcOut}\n{dismOut}";
    }

    public async Task ResetWindowsUpdateAsync()
    {
        OnLog?.Invoke("[FIX] Resetting Windows Update components...");
        if (IsSimulationMode)
        {
            await Task.Delay(100);
            OnLog?.Invoke("[DONE] Windows Update services and cache reset simulated.");
            return;
        }

        string script = "Stop-Service wuauserv, bits, cryptsvc -Force; Remove-Item 'C:\\Windows\\SoftwareDistribution\\*' -Recurse -Force; Start-Service wuauserv, bits, cryptsvc";
        await _shell.ExecutePowerShellAsync(script, 120);
        OnLog?.Invoke("[DONE] Windows Update services reset completed.");
    }

    public async Task ResetNetworkAsync()
    {
        OnLog?.Invoke("[FIX] Resetting Network stack...");
        if (IsSimulationMode)
        {
            await Task.Delay(100);
            OnLog?.Invoke("[DONE] Network stack reset simulated (netsh winsock reset, ipconfig /flushdns).");
            return;
        }

        string script = "netsh winsock reset; netsh int ip reset; ipconfig /flushdns";
        await _shell.ExecutePowerShellAsync(script, 60);
        OnLog?.Invoke("[DONE] Network stack reset completed.");
    }

    public async Task ReinstallWinGetAsync()
    {
        OnLog?.Invoke("[FIX] Re-registering WinGet package...");
        if (IsSimulationMode)
        {
            await Task.Delay(100);
            OnLog?.Invoke("[DONE] WinGet re-registration simulated.");
            return;
        }

        string script = "Add-AppxPackage -RegisterByFamilyName -MainPackage Microsoft.DesktopAppInstaller_8wekyb3d8bbwe";
        await _shell.ExecutePowerShellAsync(script, 60);
        OnLog?.Invoke("[DONE] WinGet package re-registered.");
    }

    public async Task SetNtpServerAsync()
    {
        OnLog?.Invoke("[FIX] Setting NTP Server to pool.ntp.org...");
        if (IsSimulationMode)
        {
            await Task.Delay(50);
            OnLog?.Invoke("[DONE] NTP Server set to pool.ntp.org simulated.");
            return;
        }

        string script = "w32tm /config /syncfromflags:manual /manualpeerlist:pool.ntp.org; w32tm /config /update; Restart-Service w32time";
        await _shell.ExecutePowerShellAsync(script, 60);
        OnLog?.Invoke("[DONE] NTP Server configured to pool.ntp.org.");
    }

    public async Task EnableOpenSshServerAsync()
    {
        OnLog?.Invoke("[SECURITY WARN] Enabling OpenSSH Server on port 22...");
        if (IsSimulationMode)
        {
            await Task.Delay(50);
            OnLog?.Invoke("[DONE] OpenSSH Server installation and service startup simulated.");
            return;
        }

        string script = "Add-WindowsCapability -Online -Name OpenSSH.Server~~~~0.0.1.0; Start-Service sshd; Set-Service -Name sshd -StartupType 'Automatic'";
        await _shell.ExecutePowerShellAsync(script, 180);
        OnLog?.Invoke("[DONE] OpenSSH Server enabled and running.");
    }
}
