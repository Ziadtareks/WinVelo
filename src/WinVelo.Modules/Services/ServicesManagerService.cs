using System;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using System.Threading.Tasks;
using WinVelo.Core.Safety;
using WinVelo.Core.Interfaces;

namespace WinVelo.Modules.Services;

public sealed class WindowsServiceInfo : ObservableObject
{
    public required string ServiceName { get; init; }
    public required string DisplayName { get; init; }
    private string _startupType = "Manual";
    public required string StartupType { get => _startupType; set => SetProperty(ref _startupType, value); }
    private string _status = "Unknown";
    public required string Status { get => _status; set => SetProperty(ref _status, value); }
    public IReadOnlyList<string> StartupTypes { get; } = new[] { "Automatic", "Manual", "Disabled" };
    public bool IsMicrosoft { get; init; }
}

public interface IServicesManagerService
{
    List<WindowsServiceInfo> GetServices();
    Task<bool> SetStartupTypeAsync(string serviceName, string startupType);
    Task<bool> ToggleServiceStateAsync(string serviceName, bool start);
}

public sealed class ServicesManagerService : IServicesManagerService
{
    private readonly ISystemServicesService _services;

    public ServicesManagerService(ISystemServicesService services, WinVelo.Core.Engine.RollbackJournalManager? journal = null)
    {
        _services = WinVelo.Core.Engine.ModuleJournal.Services(services, journal, "tools.services");
    }

    public List<WindowsServiceInfo> GetServices()
    {
        List<WindowsServiceInfo> list = new();

        string[] commonServices =
        [
            "wuauserv", "BITS", "Spooler", "DiagTrack", "WSearch",
            "SysMain", "EventLog", "LanmanServer", "LanmanWorkstation",
            "sshd", "w32time", "AudioSrv", "AudioEndpointBuilder"
        ];

        foreach (string s in commonServices)
        {
            try
            {
                if (_services.ServiceExists(s))
                {
                    list.Add(new WindowsServiceInfo
                    {
                        ServiceName = s,
                        DisplayName = GetFriendlyName(s),
                        StartupType = _services.GetStartupType(s),
                        Status = _services.GetState(s),
                        IsMicrosoft = true
                    });
                }
            }
            catch { }
        }

        return list;
    }

    public Task<bool> SetStartupTypeAsync(string serviceName, string startupType)
    {
        if (startupType is not ("Automatic" or "Manual" or "Disabled")) return Task.FromResult(false);
        // Windows Update, BITS, the Store and Windows Security services are never weakened, not even by hand.
        if (ServicingGuard.CheckService(serviceName, "SetStartup", startupType) != null)
        {
            return Task.FromResult(false);
        }

        try
        {
            _services.SetStartupType(serviceName, startupType);
            return Task.FromResult(true);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    public Task<bool> ToggleServiceStateAsync(string serviceName, bool start)
    {
        if (!start && ServicingGuard.CheckService(serviceName, "Stop", null) != null)
        {
            return Task.FromResult(false);
        }

        try
        {
            if (start)
                _services.StartService(serviceName);
            else
                _services.StopService(serviceName);
            return Task.FromResult(true);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    private static string GetFriendlyName(string serviceName) => serviceName switch
    {
        "wuauserv" => "Windows Update Service",
        "BITS" => "Background Intelligent Transfer Service",
        "Spooler" => "Print Spooler",
        "DiagTrack" => "Connected User Experiences and Telemetry",
        "WSearch" => "Windows Search Indexer",
        "SysMain" => "SysMain / SuperFetch",
        "sshd" => "OpenSSH SSH Server",
        "w32time" => "Windows Time Service",
        "AudioSrv" => "Windows Audio",
        "AudioEndpointBuilder" => "Windows Audio Endpoint Builder",
        _ => serviceName
    };
}
