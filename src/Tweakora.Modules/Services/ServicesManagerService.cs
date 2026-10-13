using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Tweakora.Core.Interfaces;

namespace Tweakora.Modules.Services;

public sealed class WindowsServiceInfo
{
    public required string ServiceName { get; init; }
    public required string DisplayName { get; init; }
    public required string StartupType { get; set; }
    public required string Status { get; set; }
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

    public ServicesManagerService(ISystemServicesService services)
    {
        _services = services;
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
