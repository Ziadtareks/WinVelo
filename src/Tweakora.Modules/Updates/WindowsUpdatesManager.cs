using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Tweakora.Core.Interfaces;

namespace Tweakora.Modules.Updates;

public enum UpdateProfileMode
{
    Default,
    Security,
    DisableAll
}

public sealed class UpdateModeInfo
{
    public required UpdateProfileMode Mode { get; init; }
    public required string Title { get; init; }
    public required string Badge { get; init; }
    public required string Description { get; init; }
    public required string Details { get; init; }
    public bool IsRecommended { get; init; }
    public bool IsDangerous { get; init; }
}

public sealed class WindowsUpdatesManager
{
    private readonly IRegistryService _registry;
    private readonly ISystemServicesService _services;
    private readonly ITaskSchedulerService _tasks;
    public bool IsSimulationMode { get; set; } = true;

    public event Action<string>? OnLog;

    public WindowsUpdatesManager(
        IRegistryService registry,
        ISystemServicesService services,
        ITaskSchedulerService tasks,
        bool isSimulationMode = true)
    {
        _registry = registry;
        _services = services;
        _tasks = tasks;
        IsSimulationMode = isSimulationMode;
    }

    public UpdateProfileMode DetectCurrentMode()
    {
        // Check if disabled: NoAutoUpdate == 1 in HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU
        object? noAutoVal = _registry.GetValue("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "NoAutoUpdate");
        if (noAutoVal is int intNoAuto && intNoAuto == 1)
        {
            return UpdateProfileMode.DisableAll;
        }

        // Check if security/recommended: DeferFeatureUpdates == 1
        object? deferFeat = _registry.GetValue("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "DeferFeatureUpdates");
        if (deferFeat is int intDefer && intDefer == 1)
        {
            return UpdateProfileMode.Security;
        }

        return UpdateProfileMode.Default;
    }

    public async Task ApplyModeAsync(UpdateProfileMode mode)
    {
        OnLog?.Invoke($"[UPDATES] Setting Windows Update Profile to: {mode} (Simulation: {IsSimulationMode})...");

        switch (mode)
        {
            case UpdateProfileMode.Default:
                await ApplyDefaultModeAsync();
                break;
            case UpdateProfileMode.Security:
                await ApplySecurityModeAsync();
                break;
            case UpdateProfileMode.DisableAll:
                await ApplyDisableAllModeAsync();
                break;
        }

        OnLog?.Invoke($"[UPDATES] Mode {mode} applied successfully.");
    }

    private async Task ApplyDefaultModeAsync()
    {
        if (IsSimulationMode)
        {
            await Task.Delay(50);
            return;
        }

        // Remove policy overrides
        _registry.DeleteValue("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "NoAutoUpdate");
        _registry.DeleteValue("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "AUOptions");
        _registry.DeleteValue("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "DeferFeatureUpdates");
        _registry.DeleteValue("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "DeferFeatureUpdatesPeriodInDays");
        _registry.DeleteValue("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "DeferQualityUpdates");
        _registry.DeleteValue("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "DeferQualityUpdatesPeriodInDays");
        _registry.DeleteValue("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "ExcludeWUDriversInQualityUpdate");

        // Restore services
        _services.SetStartupType("BITS", "Manual");
        _services.SetStartupType("wuauserv", "Manual");
        _services.SetStartupType("UsoSvc", "Automatic");
        _services.StartService("UsoSvc");
    }

    private async Task ApplySecurityModeAsync()
    {
        if (IsSimulationMode)
        {
            await Task.Delay(50);
            return;
        }

        // Restore core services if disabled
        _services.SetStartupType("BITS", "Manual");
        _services.SetStartupType("wuauserv", "Manual");
        _services.SetStartupType("UsoSvc", "Automatic");
        _services.StartService("UsoSvc");

        // Exclude drivers
        _registry.SetValue("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "ExcludeWUDriversInQualityUpdate", 1, "DWord");

        // Defer feature updates 365 days, quality updates 4 days
        _registry.SetValue("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "DeferFeatureUpdates", 1, "DWord");
        _registry.SetValue("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "DeferFeatureUpdatesPeriodInDays", 365, "DWord");
        _registry.SetValue("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "DeferQualityUpdates", 1, "DWord");
        _registry.SetValue("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "DeferQualityUpdatesPeriodInDays", 4, "DWord");

        // Notify before download
        _registry.SetValue("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "AUOptions", 3, "DWord");
        _registry.DeleteValue("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "NoAutoUpdate");
    }

    private async Task ApplyDisableAllModeAsync()
    {
        if (IsSimulationMode)
        {
            await Task.Delay(50);
            return;
        }

        // Disable policies
        _registry.SetValue("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "NoAutoUpdate", 1, "DWord");
        _registry.SetValue("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "AUOptions", 1, "DWord");

        // Stop and disable services
        _services.StopService("BITS");
        _services.SetStartupType("BITS", "Disabled");
        _services.StopService("wuauserv");
        _services.SetStartupType("wuauserv", "Disabled");
        _services.StopService("UsoSvc");
        _services.SetStartupType("UsoSvc", "Disabled");
    }
}
