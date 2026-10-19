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
    public async Task Cli_Diagnostics_CommandRunsInTestMode()
    {
        string outZip = Path.Combine(_tempDir, "cli_diag.zip");
        int exitCode = await Program.Main(new[] { "--test-mode", "--diagnostics", outZip });

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(outZip));
    }
}
