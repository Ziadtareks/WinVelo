using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using WinVelo.Core.Engine;
using WinVelo.Core.Models;
using WinVelo.Core.Safety;
using Xunit;

namespace WinVelo.Tests.EngineTests;

/// <summary>The rollback journal must survive crashes, damaged files and two WinVelo processes at once.</summary>
public sealed class JournalDurabilityTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "WinVeloJournalDurability_" + Guid.NewGuid().ToString("N"));

    public JournalDurabilityTests()
    {
        TestModeManager.EnableTestMode();
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static JournalActionRecord Record(string tweakId, int index = 0) => new()
    {
        TweakId = tweakId,
        ActionIndex = index,
        ActionType = "Registry",
        TargetIdentifier = $@"HKCU\Software\WinVeloTest\{tweakId}|Value{index}",
        ExistedBefore = false
    };

    [Fact]
    public void Save_IsAtomic_AndKeepsThePreviousCopyAsBackup()
    {
        var journal = new RollbackJournalManager(_dir);
        var session = journal.StartSession();
        journal.RecordActionWriteAhead(Record("a"));
        journal.RecordActionWriteAhead(Record("b"));

        string file = Path.Combine(_dir, $"session-{session.SessionId}.json");
        Assert.True(File.Exists(file));
        Assert.True(File.Exists(Path.ChangeExtension(file, ".bak")));
        Assert.False(File.Exists(Path.ChangeExtension(file, ".tmp")));
        Assert.Equal(2, journal.GetAllSessions().Single().Records.Count);
    }

    [Fact]
    public void HalfWrittenSessionFile_IsRecoveredFromBackup()
    {
        var journal = new RollbackJournalManager(_dir);
        var session = journal.StartSession();
        journal.RecordActionWriteAhead(Record("a"));
        journal.RecordActionWriteAhead(Record("b"));

        // Simulate a crash in the middle of a write by an older version: the file is cut off.
        string file = Path.Combine(_dir, $"session-{session.SessionId}.json");
        string text = File.ReadAllText(file);
        File.WriteAllText(file, text.Substring(0, text.Length / 2));

        var reopened = new RollbackJournalManager(_dir);
        var unfinished = reopened.GetUnfinishedSessions();

        var recovered = Assert.Single(unfinished);
        Assert.Equal(session.SessionId, recovered.SessionId);
        Assert.Single(recovered.Records); // the backup holds the state before the last save
        Assert.Empty(reopened.DamagedFiles);
    }

    [Fact]
    public void UnreadableFile_IsReported_AndNeverDeleted()
    {
        Directory.CreateDirectory(_dir);
        string broken = Path.Combine(_dir, "session-20250101_000000_deadbeef.json");
        File.WriteAllText(broken, "{ \"sessionId\": \"x\", \"records\": [ {");

        var journal = new RollbackJournalManager(_dir);
        Assert.Empty(journal.GetAllSessions());

        Assert.Contains(broken, journal.DamagedFiles);
        Assert.True(File.Exists(broken));
    }

    [Fact]
    public void JournalWrittenByVersion101_IsStillReadable()
    {
        // Exact shape written by WinVelo 1.0.1 (and by Tweakora before the rename).
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "session-20261016_101500_a1b2c3d4.json"), """
            {
              "sessionId": "20261016_101500_a1b2c3d4",
              "startedAtUtc": "2026-10-16T10:15:00Z",
              "isCompleted": false,
              "mode": "real",
              "appVersion": "1.0.1",
              "records": [
                {
                  "tweakId": "ui.explorer.show-file-extensions",
                  "actionIndex": 0,
                  "actionType": "Registry",
                  "targetIdentifier": "HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced|HideFileExt",
                  "existedBefore": true,
                  "previousValueKind": "DWord",
                  "previousData": "1",
                  "keyCreated": false,
                  "isBaseline": true,
                  "isReverted": false,
                  "revertedAtUtc": null,
                  "appliedAtUtc": "2026-10-16T10:15:01Z"
                }
              ]
            }
            """);

        var journal = new RollbackJournalManager(_dir);
        var session = Assert.Single(journal.GetUnfinishedSessions());
        var record = Assert.Single(session.Records);
        Assert.Equal("1", record.PreviousData);
        Assert.True(record.ExistedBefore);
    }

    [Fact]
    public async Task TwoProcessesWritingAtOnce_LeaveEveryFileValid()
    {
        // Two managers on one folder behave like the app and the CLI running at the same time.
        var first = new RollbackJournalManager(_dir);
        var second = new RollbackJournalManager(_dir);
        first.StartSession();
        second.StartSession();

        var writeFirst = Task.Run(() => { for (int i = 0; i < 40; i++) first.RecordActionWriteAhead(Record("first", i)); });
        var writeSecond = Task.Run(() => { for (int i = 0; i < 40; i++) second.RecordActionWriteAhead(Record("second", i)); });
        var readAll = Task.Run(() => { for (int i = 0; i < 40; i++) first.GetAllSessions(); });
        await Task.WhenAll(writeFirst, writeSecond, readAll);

        var reader = new RollbackJournalManager(_dir);
        var sessions = reader.GetAllSessions();
        Assert.Equal(2, sessions.Count);
        Assert.All(sessions, s => Assert.Equal(40, s.Records.Count));
        Assert.Empty(reader.DamagedFiles);
    }

    [Fact]
    public void SessionIdThatLooksLikeAPath_IsRefused()
    {
        var journal = new RollbackJournalManager(_dir);
        Assert.Throws<ArgumentException>(() => journal.CompleteSession(@"..\..\evil"));
    }
}
