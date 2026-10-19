using System;
using System.Collections.Generic;
using WinVelo.Core.Interfaces;

namespace WinVelo.Core.Engine.Runners;

public sealed class RegistryRunner
{
    private readonly IRegistryService _registryService;

    public RegistryRunner(IRegistryService registryService)
    {
        _registryService = registryService;
    }

    public bool KeyExists(string hive, string key) => _registryService.KeyExists(hive, key);
    public bool ValueExists(string hive, string key, string valueName) => _registryService.ValueExists(hive, key, valueName);
    public object? GetValue(string hive, string key, string valueName) => _registryService.GetValue(hive, key, valueName);
    public string? GetValueKind(string hive, string key, string valueName) => _registryService.GetValueKind(hive, key, valueName);

    public bool TrySetValue(string hive, string key, string valueName, object data, string valueKind, out string? errorMessage)
    {
        try
        {
            _registryService.SetValue(hive, key, valueName, data, valueKind);
            errorMessage = null;
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }

    public bool TryDeleteValue(string hive, string key, string valueName, out string? errorMessage)
    {
        try
        {
            _registryService.DeleteValue(hive, key, valueName);
            errorMessage = null;
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }

    public bool TryDeleteKey(string hive, string key, out string? errorMessage)
    {
        try
        {
            _registryService.DeleteKey(hive, key);
            errorMessage = null;
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }

    public bool TryCreateKey(string hive, string key, out string? errorMessage)
    {
        try
        {
            _registryService.CreateKey(hive, key);
            errorMessage = null;
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }

    public string ExportRegBackup(string hive, string key, string backupFolder) =>
        _registryService.ExportRegBackup(hive, key, backupFolder);
}
