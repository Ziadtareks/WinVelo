namespace Tweakora.Modules.Cleaner;

public sealed class CleanerCategoryScan
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string PathDescription { get; init; }
    public int FileCount { get; set; }
    public long TotalBytes { get; set; }
    public bool IsSelected { get; set; } = true;
    public string FormattedSize => FormatBytes(TotalBytes);

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1024 * 1024 * 1024)
            return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        if (bytes >= 1024 * 1024)
            return $"{bytes / (1024.0 * 1024):F1} MB";
        if (bytes >= 1024)
            return $"{bytes / 1024.0:F0} KB";
        return $"{bytes} B";
    }
}

public sealed class SmartCleaner
{
    // Strict Whitelist of Paths That Are Safe to Clean
    private static readonly Dictionary<string, (string Name, string PathEnv)> WhitelistedLocations = new()
    {
        ["UserTemp"] = ("User Temporary Files", @"%LOCALAPPDATA%\Temp"),
        ["WindowsTemp"] = ("Windows System Temp", @"%WINDIR%\Temp"),
        ["UpdateCache"] = ("Windows Update Downloads", @"%WINDIR%\SoftwareDistribution\Download"),
        ["CrashDumps"] = ("Application Crash Dumps", @"%LOCALAPPDATA%\CrashDumps")
    };

    public Task<List<CleanerCategoryScan>> ScanCategoriesAsync()
    {
        List<CleanerCategoryScan> results = new();

        foreach (var kvp in WhitelistedLocations)
        {
            string expanded = Environment.ExpandEnvironmentVariables(kvp.Value.PathEnv);
            int count = 0;
            long bytes = 0;

            if (Directory.Exists(expanded))
            {
                try
                {
                    DirectoryInfo di = new(expanded);
                    foreach (FileInfo fi in di.EnumerateFiles("*", SearchOption.AllDirectories))
                    {
                        try
                        {
                            count++;
                            bytes += fi.Length;
                        }
                        catch { /* skipped unreadable */ }
                    }
                }
                catch { /* access denied skipped */ }
            }

            results.Add(new CleanerCategoryScan
            {
                Id = kvp.Key,
                Name = kvp.Value.Name,
                PathDescription = kvp.Value.PathEnv,
                FileCount = count,
                TotalBytes = bytes,
                IsSelected = true
            });
        }

        return Task.FromResult(results);
    }

    public bool IsSimulationMode { get; set; } = false;

    public SmartCleaner(bool isSimulationMode = false)
    {
        IsSimulationMode = isSimulationMode;
    }

    public Task<long> CleanCategoriesAsync(IEnumerable<string> categoryIds)
    {
        long bytesFreed = 0;
        HashSet<string> selected = new(categoryIds, StringComparer.OrdinalIgnoreCase);

        foreach (var kvp in WhitelistedLocations)
        {
            if (!selected.Contains(kvp.Key)) continue;

            string expanded = Environment.ExpandEnvironmentVariables(kvp.Value.PathEnv);
            if (!Directory.Exists(expanded)) continue;

            DirectoryInfo di = new(expanded);
            foreach (FileInfo fi in di.GetFiles())
            {
                try
                {
                    long len = fi.Length;
                    if (!IsSimulationMode)
                    {
                        fi.Delete();
                    }
                    bytesFreed += len;
                }
                catch { /* in-use files safely skipped */ }
            }

            foreach (DirectoryInfo sub in di.GetDirectories())
            {
                try
                {
                    if (!IsSimulationMode)
                    {
                        sub.Delete(true);
                    }
                }
                catch { /* in-use folders safely skipped */ }
            }
        }

        return Task.FromResult(bytesFreed);
    }
}
