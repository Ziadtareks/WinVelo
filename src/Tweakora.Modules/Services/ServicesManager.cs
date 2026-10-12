using Tweakora.Core.Interfaces;

namespace Tweakora.Modules.Services;

public sealed class ServiceItem
{
    public required string ServiceName { get; init; }
    public required string DisplayName { get; init; }
    public required string StartupType { get; set; }
    public required string Status { get; set; }
    public bool IsMicrosoft { get; init; }
}

public sealed class ServicesManager
{
    private readonly ISystemServicesService _servicesService;

    public ServicesManager(ISystemServicesService servicesService)
    {
        _servicesService = servicesService;
    }

    public List<ServiceItem> GetServices()
    {
        List<ServiceItem> list = new();

        // If using fake service layer in tests, populate from fake
        if (_servicesService is Tweakora.Core.Fakes.FakeServicesService fake)
        {
            foreach (var kvp in fake.Services)
            {
                list.Add(new ServiceItem
                {
                    ServiceName = kvp.Key,
                    DisplayName = kvp.Key,
                    StartupType = kvp.Value.Startup,
                    Status = kvp.Value.State,
                    IsMicrosoft = kvp.Key.StartsWith("wuauserv") || kvp.Key.StartsWith("BITS")
                });
            }
        }
        else
        {
            // Default common system services list for viewing
            string[] commonServices =
            [
                "wuauserv", "BITS", "Spooler", "DiagTrack", "WSearch",
                "SysMain", "EventLog", "LanmanServer", "LanmanWorkstation"
            ];

            foreach (string s in commonServices)
            {
                if (_servicesService.ServiceExists(s))
                {
                    list.Add(new ServiceItem
                    {
                        ServiceName = s,
                        DisplayName = GetFriendlyName(s),
                        StartupType = _servicesService.GetStartupType(s),
                        Status = _servicesService.GetState(s),
                        IsMicrosoft = true
                    });
                }
            }
        }

        return list;
    }

    public void ChangeStartupType(string serviceName, string newType)
    {
        _servicesService.SetStartupType(serviceName, newType);
    }

    public void ToggleServiceState(string serviceName, bool start)
    {
        if (start)
            _servicesService.StartService(serviceName);
        else
            _servicesService.StopService(serviceName);
    }

    private static string GetFriendlyName(string serviceName) => serviceName switch
    {
        "wuauserv" => "Windows Update Service",
        "BITS" => "Background Intelligent Transfer Service",
        "Spooler" => "Print Spooler",
        "DiagTrack" => "Connected User Experiences and Telemetry",
        "WSearch" => "Windows Search Indexer",
        "SysMain" => "SysMain / SuperFetch",
        _ => serviceName
    };
}
