using Tweakora.Core.Interfaces;

namespace Tweakora.Modules.Startup;

public sealed class StartupEntry
{
    public required string Name { get; init; }
    public required string Command { get; init; }
    public required string Location { get; init; }
    public bool IsEnabled { get; set; } = true;
    public string Impact { get; init; } = "Low";
}

public sealed class StartupManager
{
    private readonly IRegistryService _registry;

    public StartupManager(IRegistryService registry)
    {
        _registry = registry;
    }

    public List<StartupEntry> GetStartupEntries()
    {
        List<StartupEntry> entries = new();

        // 1. Current User Run key
        ReadRunKey(entries, "HKCU", @"Software\Microsoft\Windows\CurrentVersion\Run", "User Run Key");

        // 2. Local Machine Run key
        ReadRunKey(entries, "HKLM", @"Software\Microsoft\Windows\CurrentVersion\Run", "System Run Key");

        // 3. User Startup Folder
        string startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        if (Directory.Exists(startupFolder))
        {
            foreach (string file in Directory.GetFiles(startupFolder))
            {
                entries.Add(new StartupEntry
                {
                    Name = Path.GetFileNameWithoutExtension(file),
                    Command = file,
                    Location = "Startup Folder",
                    IsEnabled = true,
                    Impact = EstimateImpact(file)
                });
            }
        }

        return entries;
    }

    public void SetEntryState(StartupEntry entry, bool enable)
    {
        // Safety: If disabled, store in disabled backup subkey
        string sourceKey = entry.Location == "User Run Key"
            ? @"Software\Microsoft\Windows\CurrentVersion\Run"
            : @"Software\Microsoft\Windows\CurrentVersion\Run";
        string hive = entry.Location == "User Run Key" ? "HKCU" : "HKLM";

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
    }

    private void ReadRunKey(List<StartupEntry> entries, string hive, string key, string locationName)
    {
        if (!_registry.KeyExists(hive, key)) return;

        // In simulated or real registry, enumerate known entries or read key
        if (_registry is Tweakora.Core.Fakes.FakeRegistryService fake)
        {
            string fullKey = $"{hive}\\{key}";
            if (fake.Keys.TryGetValue(fullKey, out var vals))
            {
                foreach (var kvp in vals)
                {
                    entries.Add(new StartupEntry
                    {
                        Name = kvp.Key,
                        Command = kvp.Value.Data?.ToString() ?? "",
                        Location = locationName,
                        IsEnabled = true,
                        Impact = EstimateImpact(kvp.Key)
                    });
                }
            }
        }
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
