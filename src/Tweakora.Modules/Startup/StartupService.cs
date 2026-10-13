using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Tweakora.Core.Interfaces;

namespace Tweakora.Modules.Startup;

public sealed class StartupProgramItem
{
    public required string Name { get; init; }
    public required string Command { get; init; }
    public required string Location { get; init; }
    public bool IsEnabled { get; set; } = true;
    public string Impact { get; init; } = "Low";
}

public interface IStartupService
{
    Task<List<StartupProgramItem>> GetStartupEntriesAsync();
    Task<bool> SetEntryStateAsync(StartupProgramItem entry, bool enable);
}

public sealed class StartupService : IStartupService
{
    private readonly IRegistryService _registry;

    public StartupService(IRegistryService registry)
    {
        _registry = registry;
    }

    public Task<List<StartupProgramItem>> GetStartupEntriesAsync()
    {
        List<StartupProgramItem> entries = new();

        // 1. Current User Run key
        ReadRunKey(entries, "HKCU", @"Software\Microsoft\Windows\CurrentVersion\Run", "User Run Key");

        // 2. Local Machine Run key
        ReadRunKey(entries, "HKLM", @"Software\Microsoft\Windows\CurrentVersion\Run", "System Run Key");

        // 3. User Startup Folder
        try
        {
            string startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            if (Directory.Exists(startupFolder))
            {
                foreach (string file in Directory.GetFiles(startupFolder))
                {
                    entries.Add(new StartupProgramItem
                    {
                        Name = Path.GetFileNameWithoutExtension(file),
                        Command = file,
                        Location = "Startup Folder",
                        IsEnabled = true,
                        Impact = EstimateImpact(file)
                    });
                }
            }
        }
        catch { }

        return Task.FromResult(entries);
    }

    public Task<bool> SetEntryStateAsync(StartupProgramItem entry, bool enable)
    {
        try
        {
            if (entry.Location == "Startup Folder")
            {
                // For files in startup folder, toggle extension (.lnk <-> .disabled)
                if (File.Exists(entry.Command))
                {
                    if (!enable && !entry.Command.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
                    {
                        string newPath = entry.Command + ".disabled";
                        File.Move(entry.Command, newPath);
                        entry.IsEnabled = false;
                        return Task.FromResult(true);
                    }
                    else if (enable && entry.Command.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
                    {
                        string newPath = entry.Command[..^9];
                        File.Move(entry.Command, newPath);
                        entry.IsEnabled = true;
                        return Task.FromResult(true);
                    }
                }
                return Task.FromResult(false);
            }

            string hive = entry.Location == "User Run Key" ? "HKCU" : "HKLM";
            string sourceKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

            if (!enable)
            {
                _registry.DeleteValue(hive, sourceKey, entry.Name);
                entry.IsEnabled = false;
            }
            else
            {
                _registry.SetValue(hive, sourceKey, entry.Name, entry.Command, "String");
                entry.IsEnabled = true;
            }

            return Task.FromResult(true);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    private void ReadRunKey(List<StartupProgramItem> entries, string hive, string key, string locationName)
    {
        try
        {
            if (!_registry.KeyExists(hive, key)) return;

            var valNames = _registry.GetValueNames(hive, key);
            foreach (string valName in valNames)
            {
                object? valData = _registry.GetValue(hive, key, valName);
                entries.Add(new StartupProgramItem
                {
                    Name = valName,
                    Command = valData?.ToString() ?? "",
                    Location = locationName,
                    IsEnabled = true,
                    Impact = EstimateImpact(valName)
                });
            }
        }
        catch { }
    }

    private static string EstimateImpact(string name)
    {
        string lower = name.ToLowerInvariant();
        if (lower.Contains("steam") || lower.Contains("epic") || lower.Contains("discord") || lower.Contains("browser"))
            return "High";
        if (lower.Contains("audio") || lower.Contains("update") || lower.Contains("sync"))
            return "Medium";
        return "Low";
    }
}
