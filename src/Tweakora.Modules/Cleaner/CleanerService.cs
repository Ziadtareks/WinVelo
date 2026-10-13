using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Tweakora.Core.Interfaces;

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

    public static string FormatBytes(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024)
            return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        if (bytes >= 1024L * 1024)
            return $"{bytes / (1024.0 * 1024):F1} MB";
        if (bytes >= 1024L)
            return $"{bytes / 1024.0:F0} KB";
        return $"{bytes} B";
    }
}

public sealed class CleanerCleanResult
{
    public int FilesDeleted { get; set; }
    public long BytesFreed { get; set; }
    public int FilesSkippedInUse { get; set; }
    public string FormattedSummary =>
        $"Deleted {FilesDeleted} files ({CleanerCategoryScan.FormatBytes(BytesFreed)}), skipped {FilesSkippedInUse} (in use)";
}

public interface ICleanerService
{
    Task<List<CleanerCategoryScan>> ScanCategoriesAsync(CancellationToken cancellationToken = default);
    Task<CleanerCleanResult> CleanCategoriesAsync(IEnumerable<string> categoryIds, CancellationToken cancellationToken = default);
}

public sealed class CleanerService : ICleanerService
{
    private readonly ISystemServicesService _services;

    // Strict Whitelist of Safe Locations
    private static readonly Dictionary<string, (string Name, string PathEnv)> WhitelistedLocations = new(StringComparer.OrdinalIgnoreCase)
    {
        ["UserTemp"] = ("User Temporary Files", @"%LOCALAPPDATA%\Temp"),
        ["WindowsTemp"] = ("Windows System Temp", @"%WINDIR%\Temp"),
        ["UpdateCache"] = ("Windows Update Downloads", @"%WINDIR%\SoftwareDistribution\Download"),
        ["CrashDumps"] = ("Application Crash Dumps", @"%LOCALAPPDATA%\CrashDumps")
    };

    public CleanerService(ISystemServicesService services)
    {
        _services = services;
    }

    public Task<List<CleanerCategoryScan>> ScanCategoriesAsync(CancellationToken cancellationToken = default)
    {
        List<CleanerCategoryScan> results = new();

        foreach (var kvp in WhitelistedLocations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string expanded = Environment.ExpandEnvironmentVariables(kvp.Value.PathEnv);
            int count = 0;
            long bytes = 0;

            if (Directory.Exists(expanded))
            {
                try
                {
                    DirectoryInfo di = new(expanded);
                    // Do not traverse reparse points (symlinks/junctions)
                    if (!di.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    {
                        var files = SafeEnumerateFiles(di);
                        foreach (var fi in files)
                        {
                            count++;
                            bytes += fi.Length;
                        }
                    }
                }
                catch { /* Ignore unreadable directories */ }
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

    public async Task<CleanerCleanResult> CleanCategoriesAsync(IEnumerable<string> categoryIds, CancellationToken cancellationToken = default)
    {
        var result = new CleanerCleanResult();
        HashSet<string> selected = new(categoryIds, StringComparer.OrdinalIgnoreCase);

        // Check if Windows Update cache is being cleaned - stop wuauserv and bits first
        bool isCleaningUpdateCache = selected.Contains("UpdateCache");
        bool stoppedWuauserv = false;
        bool stoppedBits = false;

        if (isCleaningUpdateCache)
        {
            try
            {
                if (_services.ServiceExists("wuauserv"))
                {
                    _services.StopService("wuauserv");
                    stoppedWuauserv = true;
                }
                if (_services.ServiceExists("bits"))
                {
                    _services.StopService("bits");
                    stoppedBits = true;
                }
            }
            catch { /* Best effort service stop */ }
        }

        try
        {
            foreach (var kvp in WhitelistedLocations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!selected.Contains(kvp.Key)) continue;

                string expanded = Environment.ExpandEnvironmentVariables(kvp.Value.PathEnv);
                if (!Directory.Exists(expanded)) continue;

                DirectoryInfo di = new(expanded);
                if (di.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;

                CleanDirectoryRecursive(di, result, cancellationToken);
            }
        }
        finally
        {
            // ALWAYS restart stopped services
            if (stoppedWuauserv)
            {
                try { _services.StartService("wuauserv"); } catch { }
            }
            if (stoppedBits)
            {
                try { _services.StartService("bits"); } catch { }
            }
        }

        return await Task.FromResult(result);
    }

    private static void CleanDirectoryRecursive(DirectoryInfo dir, CleanerCleanResult result, CancellationToken ct)
    {
        // Delete all files in current directory
        FileInfo[] files;
        try
        {
            files = dir.GetFiles();
        }
        catch
        {
            return;
        }

        foreach (FileInfo file in files)
        {
            ct.ThrowIfCancellationRequested();

            // Never follow reparse points
            if (file.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;

            try
            {
                long len = file.Length;
                // Clear ReadOnly attribute if present
                if (file.IsReadOnly)
                {
                    file.IsReadOnly = false;
                }
                file.Delete();
                result.FilesDeleted++;
                result.BytesFreed += len;
            }
            catch (IOException)
            {
                // File locked or in-use by running process
                result.FilesSkippedInUse++;
            }
            catch (UnauthorizedAccessException)
            {
                // Permission denied
                result.FilesSkippedInUse++;
            }
            catch
            {
                result.FilesSkippedInUse++;
            }
        }

        // Recurse into subdirectories
        DirectoryInfo[] subDirs;
        try
        {
            subDirs = dir.GetDirectories();
        }
        catch
        {
            return;
        }

        foreach (DirectoryInfo sub in subDirs)
        {
            ct.ThrowIfCancellationRequested();
            if (sub.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;

            CleanDirectoryRecursive(sub, result, ct);

            // Attempt to remove empty directory
            try
            {
                sub.Delete();
            }
            catch
            {
                // Subdirectory still has locked files or in use
            }
        }
    }

    private static List<FileInfo> SafeEnumerateFiles(DirectoryInfo dir)
    {
        List<FileInfo> list = new();
        try
        {
            foreach (FileInfo fi in dir.EnumerateFiles("*", SearchOption.AllDirectories))
            {
                if (!fi.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    list.Add(fi);
                }
            }
        }
        catch
        {
            // Fallback to top-level if enumeration errors
            try
            {
                foreach (FileInfo fi in dir.GetFiles())
                {
                    if (!fi.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    {
                        list.Add(fi);
                    }
                }
            }
            catch { }
        }
        return list;
    }
}
