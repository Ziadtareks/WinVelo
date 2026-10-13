using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Tweakora.Core.Interfaces;
using Tweakora.Core.Operations;

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

public interface IUpdateModeService
{
    UpdateProfileMode DetectCurrentMode();
    Task<OperationExecutionReport> ApplyModeAsync(
        UpdateProfileMode mode,
        IOperationRunner runner,
        CancellationToken cancellationToken = default);
}

public sealed class UpdateModeService : IUpdateModeService
{
    private readonly IRegistryService _registry;
    private readonly ISystemServicesService _services;
    private readonly ITaskSchedulerService _tasks;

    public UpdateModeService(
        IRegistryService registry,
        ISystemServicesService services,
        ITaskSchedulerService tasks)
    {
        _registry = registry;
        _services = services;
        _tasks = tasks;
    }

    public UpdateProfileMode DetectCurrentMode()
    {
        object? noAutoVal = _registry.GetValue("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "NoAutoUpdate");
        if (noAutoVal is int intNoAuto && intNoAuto == 1)
        {
            return UpdateProfileMode.DisableAll;
        }

        object? deferFeat = _registry.GetValue("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "DeferFeatureUpdates");
        if (deferFeat is int intDefer && intDefer == 1)
        {
            return UpdateProfileMode.Security;
        }

        return UpdateProfileMode.Default;
    }

    public async Task<OperationExecutionReport> ApplyModeAsync(
        UpdateProfileMode mode,
        IOperationRunner runner,
        CancellationToken cancellationToken = default)
    {
        return await runner.RunAsync(
            $"Set Windows Update Profile to {mode}",
            new[] { mode },
            (targetMode, ct) =>
            {
                switch (targetMode)
                {
                    case UpdateProfileMode.Default:
                        ApplyDefaultMode();
                        break;
                    case UpdateProfileMode.Security:
                        ApplySecurityMode();
                        break;
                    case UpdateProfileMode.DisableAll:
                        ApplyDisableAllMode();
                        break;
                }

                return Task.FromResult(new OperationItemResult
                {
                    Id = targetMode.ToString(),
                    Name = $"Windows Update Profile: {targetMode}",
                    Status = OperationItemStatus.Succeeded,
                    Reason = $"Profile {targetMode} applied successfully."
                });
            },
            cancellationToken);
    }

    private void ApplyDefaultMode()
    {
        _registry.DeleteValue("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "NoAutoUpdate");
        _registry.DeleteValue("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "AUOptions");
        _registry.DeleteValue("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "DeferFeatureUpdates");
        _registry.DeleteValue("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "DeferFeatureUpdatesPeriodInDays");
        _registry.DeleteValue("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "DeferQualityUpdates");
        _registry.DeleteValue("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "DeferQualityUpdatesPeriodInDays");
        _registry.DeleteValue("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "ExcludeWUDriversInQualityUpdate");

        _services.SetStartupType("BITS", "Manual");
        _services.SetStartupType("wuauserv", "Manual");
        _services.SetStartupType("UsoSvc", "Automatic");
        _services.StartService("UsoSvc");
    }

    private void ApplySecurityMode()
    {
        _services.SetStartupType("BITS", "Manual");
        _services.SetStartupType("wuauserv", "Manual");
        _services.SetStartupType("UsoSvc", "Automatic");
        _services.StartService("UsoSvc");

        _registry.SetValue("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "ExcludeWUDriversInQualityUpdate", 1, "DWord");
        _registry.SetValue("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "DeferFeatureUpdates", 1, "DWord");
        _registry.SetValue("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "DeferFeatureUpdatesPeriodInDays", 365, "DWord");
        _registry.SetValue("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "DeferQualityUpdates", 1, "DWord");
        _registry.SetValue("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "DeferQualityUpdatesPeriodInDays", 4, "DWord");

        _registry.SetValue("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "AUOptions", 3, "DWord");
        _registry.DeleteValue("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "NoAutoUpdate");
    }

    private void ApplyDisableAllMode()
    {
        _registry.SetValue("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "NoAutoUpdate", 1, "DWord");
        _registry.SetValue("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "AUOptions", 1, "DWord");

        _services.StopService("BITS");
        _services.SetStartupType("BITS", "Disabled");
        _services.StopService("wuauserv");
        _services.SetStartupType("wuauserv", "Disabled");
        _services.StopService("UsoSvc");
        _services.SetStartupType("UsoSvc", "Disabled");
    }
}
