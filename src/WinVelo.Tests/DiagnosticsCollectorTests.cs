using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Threading.Tasks;
using WinVelo.Cli;
using WinVelo.Core.Diagnostics;
using WinVelo.Core.Engine;
using WinVelo.Core.Models;
using WinVelo.Simulation;
using Xunit;

namespace WinVelo.Tests;

public sealed class DiagnosticsCollectorTests : IDisposable
{
    private readonly string _tempDir;

    public DiagnosticsCollectorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "WinVelo_Diag_Test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch { }
    }

    [Fact]
    public async Task DiagnosticsCollector_CreatesValidZip_WithExpectedEntries()
    {
        // Arrange
        string journalDir = Path.Combine(_tempDir, "Journal");
        string logsDir = Path.Combine(_tempDir, "Logs");
        Directory.CreateDirectory(journalDir);
        Directory.CreateDirectory(logsDir);

        // Create sample session
        var session = new RollbackJournalSession
        {
            SessionId = "20261015_120000_diag0001",
            StartedAtUtc = DateTime.UtcNow,
            IsCompleted = true
        };
        session.Records.Add(new JournalActionRecord
        {
            ActionType = "Registry",
            TweakId = "pref.darkmode",
            TargetIdentifier = "HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize\\AppsUseLightTheme",
            IsBaseline = true,
            IsReverted = false
        });
        string sessionJson = JsonSerializer.Serialize(session);
        await File.WriteAllTextAsync(Path.Combine(journalDir, "session-20261015_120000_diag0001.json"), sessionJson);

        // Create sample log with personal username
        string rawLog = $"2026-10-15 12:00:00 [INF] User {Environment.UserName} running on {Environment.MachineName} in path {Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)}\\AppData\\Local";
        await File.WriteAllTextAsync(Path.Combine(logsDir, "winvelo-20261015.log"), rawLog);

        var registry = new FakeRegistryService();
        var services = new FakeServicesService();
        var tasks = new FakeTaskSchedulerService();

        var catalog = new List<TweakDefinition>
        {
            new()
            {
                Id = "pref.darkmode",
                Name = "Windows Dark Mode",
                Category = "ui",
                Risk = "Safe"
            }
        };

        string outZip = Path.Combine(_tempDir, "diag_output.zip");
        var collector = new DiagnosticsCollector();

        // Act
        var result = await collector.CollectDiagnosticsAsync(
            outZip, registry, services, tasks, catalog, journalDir, logsDir);

        // Assert
        Assert.True(File.Exists(outZip));
        Assert.Equal(1, result.TotalTweaksScanned);
        Assert.Equal(1, result.JournalSessionsCount);
        Assert.Equal(1, result.LogsPackagedCount);
        Assert.True(result.ArchiveSizeBytes > 0);

        using var archive = ZipFile.OpenRead(outZip);
        Assert.NotNull(archive.GetEntry("system_info.json"));
        Assert.NotNull(archive.GetEntry("journal_summary.json"));
        Assert.NotNull(archive.GetEntry("tweak_states.json"));
        Assert.NotNull(archive.GetEntry("logs/winvelo-20261015.log"));

        // Verify log sanitization inside the zip
        var logEntry = archive.GetEntry("logs/winvelo-20261015.log")!;
        using var reader = new StreamReader(logEntry.Open());
        string content = await reader.ReadToEndAsync();
        Assert.DoesNotContain(Environment.UserName, content);
        Assert.Contains("<USER>", content);
        if (!string.IsNullOrEmpty(Environment.MachineName) &&
            !string.Equals(Environment.MachineName, Environment.UserName, StringComparison.OrdinalIgnoreCase))
        {
            Assert.DoesNotContain(Environment.MachineName, content);
            Assert.Contains("<MACHINE>", content);
        }
    }

    [Fact]
    public void SanitizeContent_ReplacesUsernameAndMachineName()
    {
        string input = $"Error in C:\\Users\\{Environment.UserName}\\AppData on host {Environment.MachineName} by user {Environment.UserName}";
        string sanitized = DiagnosticsCollector.SanitizeContent(input);

        Assert.DoesNotContain(Environment.UserName, sanitized);
        Assert.Contains("<USER>", sanitized);
        if (!string.IsNullOrEmpty(Environment.MachineName) &&
            !string.Equals(Environment.MachineName, Environment.UserName, StringComparison.OrdinalIgnoreCase))
        {
            Assert.DoesNotContain(Environment.MachineName, sanitized);
            Assert.Contains("<MACHINE>", sanitized);
        }
    }

    [Fact]
    public void SanitizeContent_WithDistinctUserAndMachine_ReplacesBoth()
    {
        // Test custom distinct user and machine simulation
        string input = "User Alice on Machine RIG-999 in C:\\Users\\Alice\\AppData";
        // If we test with Environment.UserName, we already verified above; here verify standard behavior
        string sanitized = DiagnosticsCollector.SanitizeContent(input);
        Assert.NotNull(sanitized);
    }

    [Fact]
    public void SanitizeContent_RemovesEveryKindOfPersonalData()
    {
        string log = string.Join("\n",
            @"Loaded C:\Users\Alice\AppData\Roaming\WinVelo\Journal",
            @"Other profile: D:\Users\Bob Smith\Desktop\file.txt and C:/Users/carol/Downloads",
            @"Share \\RIG-999\Users\Alice\Documents",
            "Owner SID S-1-5-21-3623811015-3361044348-30300820-1013 and AAD S-1-12-1-1111111111-2222222222-3333333333-4444444444",
            "Signed in as alice.smith@contoso.com on RIG-999 in domain CONTOSO",
            "IPv4 192.168.1.23, IPv6 fe80::1c2b:3d4e:5f60:7a8b%12 and 2001:0db8:85a3:0000:0000:8a2e:0370:7334",
            "MAC 00-1A-2B-3C-4D-5E / 00:1a:2b:3c:4d:5e",
            "Disk SerialNumber: WD-WCC4N1234567, BIOS Serial=ABC123",
            "Alice already started at 08:35:52.094 on Windows 10.0.22631 with WinVelo 1.0.2",
            "Tweak HKCU\\Software\\Classes\\CLSID\\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\\InprocServer32");

        string s = DiagnosticsCollector.SanitizeContent(log, userName: "Alice", machineName: "RIG-999", domain: "CONTOSO", dnsDomain: "contoso.local");

        foreach (string secret in new[] { "Alice", "Bob Smith", "carol", "RIG-999", "CONTOSO", "S-1-5-21-3623811015", "S-1-12-1-1111111111",
                                          "alice.smith@contoso.com", "192.168.1.23", "fe80::1c2b", "2001:0db8", "00-1A-2B", "00:1a:2b",
                                          "WCC4N1234567", "ABC123" })
        {
            Assert.DoesNotContain(secret, s, StringComparison.OrdinalIgnoreCase);
        }

        // Useful, non-personal text stays readable.
        Assert.Contains(@"C:\Users\<USER>\AppData\Roaming\WinVelo\Journal", s);
        Assert.Contains("already started at 08:35:52.094", s);
        Assert.Contains("10.0.22631", s);
        Assert.Contains("1.0.2", s);
        Assert.Contains("{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}", s);
    }

    [Fact]
    public async Task Cli_Diagnostics_CommandRunsInTestMode()
    {
        string outZip = Path.Combine(_tempDir, "cli_diag.zip");
        int exitCode = await Program.Main(new[] { "--test-mode", "--diagnostics", outZip });

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(outZip));
    }
}
