using System;
using Tweakora.Core.Interfaces;

namespace Tweakora.Core.Engine.Runners;

public sealed class ServiceRunner
{
    private readonly ISystemServicesService _servicesService;

    public ServiceRunner(ISystemServicesService servicesService)
    {
        _servicesService = servicesService;
    }

    public bool ServiceExists(string serviceName) => _servicesService.ServiceExists(serviceName);
    public string GetStartupType(string serviceName) => _servicesService.GetStartupType(serviceName);
    public string GetState(string serviceName) => _servicesService.GetState(serviceName);

    public bool TrySetStartupType(string serviceName, string startupType, out string? errorMessage)
    {
        try
        {
            _servicesService.SetStartupType(serviceName, startupType);
            errorMessage = null;
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }

    public bool TryStartService(string serviceName, out string? errorMessage)
    {
        try
        {
            _servicesService.StartService(serviceName);
            errorMessage = null;
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }

    public bool TryStopService(string serviceName, out string? errorMessage)
    {
        try
        {
            _servicesService.StopService(serviceName);
            errorMessage = null;
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }
}
