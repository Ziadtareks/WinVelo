using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using WinVelo.Core.Interfaces;

namespace WinVelo.Modules.Startup;

public interface IStartupService
{
    Task<List<StartupAppItem>> GetStartupEntriesAsync();
    Task<bool> SetEntryStateAsync(StartupAppItem entry, bool enable);
    Task<int> OptimizeStartupAsync();
}

public sealed class StartupService : IStartupService
{
    private readonly IRegistryService _registry;

    private const string ApprovedRunKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ApprovedRun32Key = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32";
    private const string ApprovedFolderKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder";

    public StartupService(IRegistryService registry)
    {
        _registry = registry;
    }

    public Task<List<StartupAppItem>> GetStartupEntriesAsync()
    {
        List<StartupAppItem> entries = new();
        HashSet<string> seenIds = new(StringComparer.OrdinalIgnoreCase);

        // 1. Current User Run (HKCU)
        ScanRunKey(
            entries,
            seenIds,
            hive: "HKCU",
            runKey: @"Software\Microsoft\Windows\CurrentVersion\Run",
            approvedHive: "HKCU",
            approvedKey: ApprovedRunKey,
            source: "User Registry",
            locationType: StartupLocationType.UserRun);

        // 2. Local Machine Run 64-bit (HKLM)
        ScanRunKey(
            entries,
            seenIds,
            hive: "HKLM",
            runKey: @"Software\Microsoft\Windows\CurrentVersion\Run",
            approvedHive: "HKLM",
            approvedKey: ApprovedRunKey,
            source: "System Registry",
            locationType: StartupLocationType.SystemRun);

        // 3. Local Machine Run 32-bit (HKLM WOW6432Node)
        ScanRunKey(
            entries,
            seenIds,
            hive: "HKLM",
            runKey: @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run",
            approvedHive: "HKLM",
            approvedKey: ApprovedRun32Key,
            source: "System Registry (x86)",
            locationType: StartupLocationType.SystemRun32);

        // 4. User Startup Folder
        try
        {
            string userStartup = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            ScanStartupFolder(
                entries,
                seenIds,
                folderPath: userStartup,
                approvedHive: "HKCU",
                approvedKey: ApprovedFolderKey,
                source: "User Startup Folder",
                locationType: StartupLocationType.UserStartupFolder);
        }
        catch { }

        // 5. Common Startup Folder
        try
        {
            string commonStartup = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup);
            ScanStartupFolder(
                entries,
                seenIds,
                folderPath: commonStartup,
                approvedHive: "HKLM",
                approvedKey: ApprovedFolderKey,
                source: "System Startup Folder",
                locationType: StartupLocationType.CommonStartupFolder);
        }
        catch { }

        return Task.FromResult(entries);
    }

    public Task<bool> SetEntryStateAsync(StartupAppItem entry, bool enable)
    {
        try
        {
            object? current = _registry.GetValue(entry.StartupApprovedHive, entry.StartupApprovedKey, entry.StartupApprovedValueName);
            byte[]? existingBytes = current as byte[];

            byte[] newBytes = enable
                ? StartupApprovedHelper.CreateEnabledBytes(existingBytes)
                : StartupApprovedHelper.CreateDisabledBytes(existingBytes);

            _registry.SetValue(entry.StartupApprovedHive, entry.StartupApprovedKey, entry.StartupApprovedValueName, newBytes, "Binary");
            entry.IsEnabled = enable;

            return Task.FromResult(true);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    public async Task<int> OptimizeStartupAsync()
    {
        var entries = await GetStartupEntriesAsync();
        int count = 0;
        foreach (var entry in entries)
        {
            if (entry.IsEnabled && entry.Impact == "High" && !entry.IsEssentialSystemItem)
            {
                bool ok = await SetEntryStateAsync(entry, false);
                if (ok) count++;
            }
        }
        return count;
    }

    private void ScanRunKey(
        List<StartupAppItem> entries,
        HashSet<string> seenIds,
        string hive,
        string runKey,
        string approvedHive,
        string approvedKey,
        string source,
        StartupLocationType locationType)
    {
        try
        {
            if (!_registry.KeyExists(hive, runKey)) return;

            var valNames = _registry.GetValueNames(hive, runKey);
            foreach (string valName in valNames)
            {
                if (string.IsNullOrWhiteSpace(valName)) continue;
                string id = $"{hive}_{runKey}_{valName}";
                if (seenIds.Contains(id)) continue;
                seenIds.Add(id);

                object? rawCommand = _registry.GetValue(hive, runKey, valName);
                string command = rawCommand?.ToString() ?? string.Empty;
                string cleanExe = StartupPathHelper.ExtractCleanExePath(command);

                bool isEnabled = ReadApprovedState(approvedHive, approvedKey, valName);
                var (publisher, description, fileSize) = ExtractFileMetadata(cleanExe, valName);
                var (impact, delay) = StartupImpactEvaluator.CalculateImpact(valName, cleanExe, fileSize, command);
                bool isEssential = StartupImpactEvaluator.IsEssential(valName, cleanExe);
                var (brandColor, brandSymbol, brandSvg) = StartupImpactEvaluator.GetBrandIcon(valName, cleanExe);

                entries.Add(new StartupAppItem
                {
                    Id = id,
                    Name = valName,
                    Command = command,
                    CleanExePath = cleanExe,
                    Publisher = publisher,
                    Description = description,
                    Source = source,
                    LocationType = locationType,
                    StartupApprovedHive = approvedHive,
                    StartupApprovedKey = approvedKey,
                    StartupApprovedValueName = valName,
                    IsEnabled = isEnabled,
                    Impact = impact,
                    BootDelaySeconds = delay,
                    FileSizeBytes = fileSize,
                    FileSizeFormatted = FormatFileSize(fileSize),
                    IsEssentialSystemItem = isEssential,
                    IconColor = brandColor,
                    IconSymbol = brandSymbol,
                    SvgIconPath = brandSvg,
                    IconLetter = StartupImpactEvaluator.GetIconLetter(valName)
                });
            }
        }
        catch { }
    }

    private void ScanStartupFolder(
        List<StartupAppItem> entries,
        HashSet<string> seenIds,
        string folderPath,
        string approvedHive,
        string approvedKey,
        string source,
        StartupLocationType locationType)
    {
        HashSet<string> seenFileNames = new(StringComparer.OrdinalIgnoreCase);

        // Scan actual files on disk if directory exists
        if (Directory.Exists(folderPath))
        {
            try
            {
                var files = Directory.GetFiles(folderPath);
                foreach (string file in files)
                {
                    string fileName = Path.GetFileName(file);
                    seenFileNames.Add(fileName);
                    string name = Path.GetFileNameWithoutExtension(file);

                    string id = $"{approvedHive}_{folderPath}_{fileName}";
                    if (seenIds.Contains(id)) continue;
                    seenIds.Add(id);

                    string cleanExe = StartupPathHelper.ExtractCleanExePath(file);
                    bool isEnabled = ReadApprovedState(approvedHive, approvedKey, fileName);
                    var (publisher, description, fileSize) = ExtractFileMetadata(cleanExe, name);
                    var (impact, delay) = StartupImpactEvaluator.CalculateImpact(name, cleanExe, fileSize, file);
                    bool isEssential = StartupImpactEvaluator.IsEssential(name, cleanExe);
                    var (brandColor, brandSymbol, brandSvg) = StartupImpactEvaluator.GetBrandIcon(name, cleanExe);

                    entries.Add(new StartupAppItem
                    {
                        Id = id,
                        Name = name,
                        Command = file,
                        CleanExePath = cleanExe,
                        Publisher = publisher,
                        Description = description,
                        Source = source,
                        LocationType = locationType,
                        StartupApprovedHive = approvedHive,
                        StartupApprovedKey = approvedKey,
                        StartupApprovedValueName = fileName,
                        IsEnabled = isEnabled,
                        Impact = impact,
                        BootDelaySeconds = delay,
                        FileSizeBytes = fileSize,
                        FileSizeFormatted = FormatFileSize(fileSize),
                        IsEssentialSystemItem = isEssential,
                        IconColor = brandColor,
                        IconSymbol = brandSymbol,
                        SvgIconPath = brandSvg,
                        IconLetter = StartupImpactEvaluator.GetIconLetter(name)
                    });
                }
            }
            catch { }
        }

        // Also scan values in StartupApproved\StartupFolder for items tracked by Windows or seeded in simulation
        try
        {
            if (_registry.KeyExists(approvedHive, approvedKey))
            {
                var valNames = _registry.GetValueNames(approvedHive, approvedKey);
                foreach (string valName in valNames)
                {
                    if (seenFileNames.Contains(valName)) continue;
                    string name = Path.GetFileNameWithoutExtension(valName);
                    string fakePath = Path.Combine(folderPath, valName);

                    string id = $"{approvedHive}_{folderPath}_{valName}";
                    if (seenIds.Contains(id)) continue;
                    seenIds.Add(id);

                    string cleanExe = StartupPathHelper.ExtractCleanExePath(fakePath);
                    bool isEnabled = ReadApprovedState(approvedHive, approvedKey, valName);
                    var (publisher, description, fileSize) = ExtractFileMetadata(cleanExe, name);
                    var (impact, delay) = StartupImpactEvaluator.CalculateImpact(name, cleanExe, fileSize, fakePath);
                    bool isEssential = StartupImpactEvaluator.IsEssential(name, cleanExe);
                    var (brandColor, brandSymbol, brandSvg) = StartupImpactEvaluator.GetBrandIcon(name, cleanExe);

                    entries.Add(new StartupAppItem
                    {
                        Id = id,
                        Name = name,
                        Command = fakePath,
                        CleanExePath = cleanExe,
                        Publisher = publisher,
                        Description = description,
                        Source = source,
                        LocationType = locationType,
                        StartupApprovedHive = approvedHive,
                        StartupApprovedKey = approvedKey,
                        StartupApprovedValueName = valName,
                        IsEnabled = isEnabled,
                        Impact = impact,
                        BootDelaySeconds = delay,
                        FileSizeBytes = fileSize,
                        FileSizeFormatted = FormatFileSize(fileSize),
                        IsEssentialSystemItem = isEssential,
                        IconColor = brandColor,
                        IconSymbol = brandSymbol,
                        SvgIconPath = brandSvg,
                        IconLetter = StartupImpactEvaluator.GetIconLetter(name)
                    });
                }
            }
        }
        catch { }
    }

    private bool ReadApprovedState(string approvedHive, string approvedKey, string valName)
    {
        try
        {
            if (!_registry.ValueExists(approvedHive, approvedKey, valName))
            {
                return true; // Not present => Enabled by default in Windows Task Manager
            }

            object? data = _registry.GetValue(approvedHive, approvedKey, valName);
            if (data == null) return true;

            if (data is byte[] bytes)
            {
                return StartupApprovedHelper.IsEnabled(bytes);
            }
            else if (data is string str && !string.IsNullOrEmpty(str))
            {
                try
                {
                    byte[] decoded = Convert.FromBase64String(str);
                    return StartupApprovedHelper.IsEnabled(decoded);
                }
                catch { }
            }
        }
        catch { }
        return true;
    }

    private static (string Publisher, string Description, long FileSize) ExtractFileMetadata(string exePath, string fallbackName)
    {
        string publisher = "Unknown Publisher";
        string description = fallbackName;
        long fileSize = 0;

        if (string.IsNullOrWhiteSpace(exePath)) return (publisher, description, fileSize);

        try
        {
            if (File.Exists(exePath))
            {
                var fi = new FileInfo(exePath);
                fileSize = fi.Length;

                var vi = FileVersionInfo.GetVersionInfo(exePath);
                if (!string.IsNullOrWhiteSpace(vi.CompanyName)) publisher = vi.CompanyName;
                if (!string.IsNullOrWhiteSpace(vi.FileDescription)) description = vi.FileDescription;
            }
            else
            {
                // Fallback publishers for common apps if file is absent on host
                string lower = fallbackName.ToLowerInvariant();
                if (lower.Contains("discord")) { publisher = "Discord Inc."; description = "Discord Voice & Chat"; fileSize = 85 * 1024 * 1024; }
                else if (lower.Contains("steam")) { publisher = "Valve Corporation"; description = "Steam Client Bootstrapper"; fileSize = 45 * 1024 * 1024; }
                else if (lower.Contains("spotify")) { publisher = "Spotify AB"; description = "Spotify Music Player"; fileSize = 65 * 1024 * 1024; }
                else if (lower.Contains("teams")) { publisher = "Microsoft Corporation"; description = "Microsoft Teams"; fileSize = 90 * 1024 * 1024; }
                else if (lower.Contains("onedrive")) { publisher = "Microsoft Corporation"; description = "Microsoft OneDrive Sync"; fileSize = 35 * 1024 * 1024; }
                else if (lower.Contains("telegram")) { publisher = "Telegram FZ-LLC"; description = "Telegram Desktop"; fileSize = 40 * 1024 * 1024; }
                else if (lower.Contains("adobe")) { publisher = "Adobe Inc."; description = "Adobe Creative Cloud Launcher"; fileSize = 120 * 1024 * 1024; }
                else if (lower.Contains("securityhealth")) { publisher = "Microsoft Corporation"; description = "Windows Security Health Tray"; fileSize = 1024 * 1024; }
                else if (lower.Contains("rtkaudu") || lower.Contains("realtek")) { publisher = "Realtek Semiconductor"; description = "Realtek Audio Service"; fileSize = 2 * 1024 * 1024; }
                else if (lower.Contains("7-zip")) { publisher = "Igor Pavlov"; description = "7-Zip Shell Helper"; fileSize = 500 * 1024; }
            }
        }
        catch { }

        return (publisher, description, fileSize);
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes <= 0) return string.Empty;
        if (bytes >= 1024 * 1024 * 1024)
            return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
        if (bytes >= 1024 * 1024)
            return $"{bytes / (1024.0 * 1024):F1} MB";
        if (bytes >= 1024)
            return $"{bytes / 1024.0:F0} KB";
        return $"{bytes} B";
    }
}
