using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WinVelo.Core.Interfaces;
using WinVelo.Core.Operations;

namespace WinVelo.Modules.Updates;

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
        ITaskSchedulerService tasks, WinVelo.Core.Engine.RollbackJournalManager? journal = null)
    {
        _registry = WinVelo.Core.Engine.ModuleJournal.Registry(registry, journal, "tools.updates");
        _services = WinVelo.Core.Engine.ModuleJournal.Services(services, journal, "tools.updates");
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

    /// <summary>
    /// Turns off AUTOMATIC updates with the documented NoAutoUpdate policy only. Windows Update, BITS and the
    /// Update Orchestrator are never disabled or stopped (see ServicingGuard): the user can still check for and
    /// install updates from Settings, and the Store keeps working. Earlier versions disabled those services;
    /// they are set back to their default startup type here so that choosing this mode also repairs that.
    /// </summary>
    private void ApplyDisableAllMode()
    {
        _registry.SetValue("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "NoAutoUpdate", 1, "DWord");
        _registry.SetValue("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "AUOptions", 1, "DWord");

        RestoreServiceIfDisabled("BITS", "Manual");
        RestoreServiceIfDisabled("wuauserv", "Manual");
        RestoreServiceIfDisabled("UsoSvc", "Automatic");
    }

    private void RestoreServiceIfDisabled(string serviceName, string defaultStartup)
    {
        if (_services.ServiceExists(serviceName) &&
            string.Equals(_services.GetStartupType(serviceName), "Disabled", StringComparison.OrdinalIgnoreCase))
        {
            _services.SetStartupType(serviceName, defaultStartup);
        }
    }
}
