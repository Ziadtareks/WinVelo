using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using WinVelo.Core.Engine;
using WinVelo.Core.Interfaces;
using WinVelo.Core.Models;
using WinVelo.Core.Safety;

namespace WinVelo.Core.Diagnostics;

public sealed class DiagnosticsResult
{
    public required string OutputPath { get; init; }
    public required int TotalTweaksScanned { get; init; }
    public required int JournalSessionsCount { get; init; }
    public required int LogsPackagedCount { get; init; }
    public required long ArchiveSizeBytes { get; init; }
}

public sealed class SystemDiagnosticInfo
{
    public string AppVersion { get; init; } = AppInfo.Version;
    public string OSDescription { get; init; } = RuntimeInformation.OSDescription;
    public string OSVersion { get; init; } = Environment.OSVersion.VersionString;
    public string OSArchitecture { get; init; } = RuntimeInformation.OSArchitecture.ToString();
    public string ProcessArchitecture { get; init; } = RuntimeInformation.ProcessArchitecture.ToString();
    public string FrameworkDescription { get; init; } = RuntimeInformation.FrameworkDescription;
    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;
}

public sealed class JournalSessionSummaryItem
{
    public required string SessionId { get; init; }
    public required DateTime StartedAtUtc { get; init; }
    public required bool IsCompleted { get; init; }
    public required int TotalRecords { get; init; }
    public required int ActiveRecords { get; init; }
    public required int RevertedRecords { get; init; }
    public required List<string> TweaksTouched { get; init; }
}

public sealed class TweakStateSummaryItem
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Category { get; init; }
    public required string Risk { get; init; }
    public required string State { get; init; }
}

public sealed class DiagnosticsCollector
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public async Task<DiagnosticsResult> CollectDiagnosticsAsync(
        string outputPath,
        IRegistryService registry,
        ISystemServicesService services,
        ITaskSchedulerService tasks,
        IReadOnlyList<TweakDefinition> catalog,
        string? journalDir = null,
        string? logsDir = null)
    {
        journalDir ??= WinVeloStoragePaths.GetJournalDirectory(TestModeManager.IsTestMode);
        logsDir ??= WinVeloStoragePaths.GetLogsDirectory(TestModeManager.IsTestMode);

        string? outDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
        {
            Directory.CreateDirectory(outDir);
        }

        // Delete existing zip file if it already exists
        if (File.Exists(outputPath))
        {
            File.Delete(outputPath);
        }

        // 1. Gather System Information
        var sysInfo = new SystemDiagnosticInfo
        {
            AppVersion = GetAppVersion()
        };
        string sysInfoJson = JsonSerializer.Serialize(sysInfo, JsonOptions);

        // 2. Gather Journal Summary (Sanitized)
        var journalSummaries = CollectJournalSummaries(journalDir);
        string journalJson = JsonSerializer.Serialize(journalSummaries, JsonOptions);

        // 3. Gather Tweak States (Read-only detection)
        var tweakStates = await CollectTweakStatesAsync(catalog, registry, services, tasks);
        string tweaksJson = JsonSerializer.Serialize(tweakStates, JsonOptions);

        // 4. Gather & Sanitize Recent Logs
        var sanitizedLogs = CollectSanitizedLogs(logsDir);

        // 5. Create Zip Archive
        using (var zipStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
        {
            // Add system_info.json
            WriteArchiveEntry(archive, "system_info.json", sysInfoJson);

            // Add journal_summary.json
            WriteArchiveEntry(archive, "journal_summary.json", journalJson);

            // Add tweak_states.json
            WriteArchiveEntry(archive, "tweak_states.json", tweaksJson);

            // Add sanitized logs
            foreach (var log in sanitizedLogs)
            {
                WriteArchiveEntry(archive, $"logs/{log.FileName}", log.Content);
            }
        }

        var fileInfo = new FileInfo(outputPath);

        return new DiagnosticsResult
        {
            OutputPath = outputPath,
            TotalTweaksScanned = tweakStates.Count,
            JournalSessionsCount = journalSummaries.Count,
            LogsPackagedCount = sanitizedLogs.Count,
            ArchiveSizeBytes = fileInfo.Length
        };
    }

    private static string GetAppVersion() => AppInfo.Version;

    private static List<JournalSessionSummaryItem> CollectJournalSummaries(string journalDir)
    {
        var list = new List<JournalSessionSummaryItem>();
        if (!Directory.Exists(journalDir))
        {
            return list;
        }

        string[] sessionFiles = Directory.GetFiles(journalDir, "session-*.json")
            .OrderByDescending(f => f)
            .ToArray();

        foreach (string file in sessionFiles)
        {
            try
            {
                string json = File.ReadAllText(file);
                var session = JsonSerializer.Deserialize<RollbackJournalSession>(json);
                if (session == null) continue;

                var tweaksTouched = session.Records
                    .Select(r => r.TweakId)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                list.Add(new JournalSessionSummaryItem
                {
                    SessionId = session.SessionId,
                    StartedAtUtc = session.StartedAtUtc,
                    IsCompleted = session.IsCompleted,
                    TotalRecords = session.Records.Count,
                    ActiveRecords = session.Records.Count(r => !r.IsReverted),
                    RevertedRecords = session.Records.Count(r => r.IsReverted),
                    TweaksTouched = tweaksTouched
                });
            }
            catch
            {
                // Skip unparseable sessions
            }
        }

        return list;
    }

    private static async Task<List<TweakStateSummaryItem>> CollectTweakStatesAsync(
        IReadOnlyList<TweakDefinition> catalog,
        IRegistryService registry,
        ISystemServicesService services,
        ITaskSchedulerService tasks)
    {
        var detector = new StateDetector(registry, services, tasks);
        var results = new List<TweakStateSummaryItem>(catalog.Count);

        foreach (var tweak in catalog)
        {
            TweakState state;
            try
            {
                state = await detector.DetectTweakStateAsync(tweak, useCache: false);
            }
            catch
            {
                state = TweakState.Unknown;
            }

            results.Add(new TweakStateSummaryItem
            {
                Id = tweak.Id,
                Name = tweak.Name,
                Category = tweak.Category,
                Risk = tweak.Risk,
                State = state.ToString()
            });
        }

        return results;
    }

    private static List<(string FileName, string Content)> CollectSanitizedLogs(string logsDir)
    {
        var logs = new List<(string FileName, string Content)>();
        if (!Directory.Exists(logsDir))
        {
            return logs;
        }

        string[] logFiles = Directory.GetFiles(logsDir, "winvelo-*.log")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .Take(3)
            .ToArray();

        foreach (string logPath in logFiles)
        {
            try
            {
                string raw = File.ReadAllText(logPath);
                string sanitized = SanitizeContent(raw);
                logs.Add((Path.GetFileName(logPath), sanitized));
            }
            catch
            {
                // Skip inaccessible logs
            }
        }

        return logs;
    }

    /// <summary>
    /// Removes personal data from log text before it goes into a diagnostics bundle: user profile paths (of any
    /// user), the user, machine and domain names, Windows SIDs, IP and MAC addresses, e-mail addresses and
    /// serial numbers. Names are replaced only as whole words, so "Al" does not turn "Already" into "&lt;USER&gt;ready".
    /// </summary>
    public static string SanitizeContent(string input)
    {
        return SanitizeContent(input, Environment.UserName, Environment.MachineName,
            Environment.GetEnvironmentVariable("USERDOMAIN"), Environment.GetEnvironmentVariable("USERDNSDOMAIN"));
    }

    public static string SanitizeContent(string input, string? userName, string? machineName, string? domain = null, string? dnsDomain = null)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        string s = input;

        // Profile folders of any user: C:\Users\Name, \\server\Users\Name, C:/Users/Name, Documents and Settings.
        s = Regex.Replace(s, @"(?i)(\\|/)(Users|Documents and Settings)(\\|/)(?!<USER>)[^\\/:*?""<>|\r\n]+", "$1$2$3<USER>");

        // Windows security identifiers (user and machine accounts).
        s = Regex.Replace(s, @"\bS-1-5-21(-\d+){3,4}\b", "<SID>");
        s = Regex.Replace(s, @"\bS-1-12-1(-\d+){4,}\b", "<SID>");

        // E-mail addresses (Microsoft account names often appear in paths and messages).
        s = Regex.Replace(s, @"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b", "<EMAIL>");

        // MAC addresses, then IPv4 and IPv6 addresses.
        s = Regex.Replace(s, @"(?i)\b([0-9a-f]{2}[:-]){5}[0-9a-f]{2}\b", "<MAC>");
        s = Regex.Replace(s, @"(?<![\d.])(25[0-5]|2[0-4]\d|1?\d?\d)(\.(25[0-5]|2[0-4]\d|1?\d?\d)){3}(?![\d.])", "<IP>");
        s = Regex.Replace(s, @"(?i)(?<![0-9a-f:])([0-9a-f]{1,4}:){7}[0-9a-f]{1,4}(?![0-9a-f:])", "<IP>");
        s = Regex.Replace(s, @"(?i)(?<![0-9a-z:])([0-9a-f]{1,4}:){1,6}:[0-9a-f]{1,4}(:[0-9a-f]{1,4}){0,5}(%\w+)?(?![0-9a-z:])", "<IP>");

        // "SerialNumber: ABC123", "Serial=..." and similar.
        s = Regex.Replace(s, @"(?i)\b(serial\s*(number|no\.?)?\s*[:=]\s*)[^\s,;""']+", "$1<SERIAL>");

        // Names last, as whole words only.
        s = ReplaceWord(s, userName, "<USER>");
        if (!string.Equals(machineName, userName, StringComparison.OrdinalIgnoreCase)) s = ReplaceWord(s, machineName, "<MACHINE>");
        if (!string.Equals(domain, machineName, StringComparison.OrdinalIgnoreCase)) s = ReplaceWord(s, domain, "<DOMAIN>");
        s = ReplaceWord(s, dnsDomain, "<DOMAIN>");

        return s;
    }

    private static string ReplaceWord(string text, string? word, string replacement)
    {
        if (string.IsNullOrWhiteSpace(word) || word.Length < 2) return text;
        return Regex.Replace(text, @"(?<![\p{L}\p{N}_])" + Regex.Escape(word) + @"(?![\p{L}\p{N}_])", replacement, RegexOptions.IgnoreCase);
    }

    private static void WriteArchiveEntry(ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, System.Text.Encoding.UTF8);
        writer.Write(content);
    }
}
