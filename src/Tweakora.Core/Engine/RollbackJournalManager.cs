using System.Text.Json;
using Tweakora.Core.Models;

namespace Tweakora.Core.Engine;

public sealed class RollbackJournalManager
{
    private readonly string _journalDirectory;
    private readonly object _lock = new();
    private RollbackJournalSession? _currentSession;

    public RollbackJournalManager(string journalDirectory)
    {
        _journalDirectory = journalDirectory;
        Directory.CreateDirectory(_journalDirectory);
    }

    public RollbackJournalSession StartSession()
    {
        lock (_lock)
        {
            _currentSession = new RollbackJournalSession
            {
                SessionId = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_") + Guid.NewGuid().ToString("N")[..8],
                StartedAtUtc = DateTime.UtcNow,
                IsCompleted = false
            };

            FlushSessionUnsafe(_currentSession);
            return _currentSession;
        }
    }

    public void RecordActionWriteAhead(JournalActionRecord record)
    {
        lock (_lock)
        {
            if (_currentSession == null)
            {
                StartSession();
            }

            // Check if baseline already exists for this target in any previous session or current session
            bool hasExistingBaseline = FindExistingBaselineRecord(record.TweakId, record.TargetIdentifier) != null;
            if (hasExistingBaseline)
            {
                record.IsBaseline = false;
            }

            _currentSession!.Records.Add(record);
            FlushSessionUnsafe(_currentSession);
        }
    }

    public void CompleteSession()
    {
        lock (_lock)
        {
            if (_currentSession != null)
            {
                _currentSession.IsCompleted = true;
                FlushSessionUnsafe(_currentSession);
            }
        }
    }

    public List<RollbackJournalSession> GetUnfinishedSessions()
    {
        lock (_lock)
        {
            List<RollbackJournalSession> unfinished = new();
            string[] files = Directory.GetFiles(_journalDirectory, "*.json");
            foreach (string file in files)
            {
                try
                {
                    string json = File.ReadAllText(file);
                    RollbackJournalSession? session = JsonSerializer.Deserialize<RollbackJournalSession>(json);
                    if (session != null && !session.IsCompleted && session.Records.Count > 0)
                    {
                        unfinished.Add(session);
                    }
                }
                catch
                {
                    // Ignore corrupted files in listing
                }
            }
            return unfinished;
        }
    }

    public JournalActionRecord? FindExistingBaselineRecord(string tweakId, string targetIdentifier)
    {
        // First check current session
        if (_currentSession != null)
        {
            JournalActionRecord? found = _currentSession.Records
                .FirstOrDefault(r => r.TweakId == tweakId && r.TargetIdentifier == targetIdentifier && r.IsBaseline);
            if (found != null) return found;
        }

        // Search past session files
        string[] files = Directory.GetFiles(_journalDirectory, "*.json")
            .OrderBy(f => f) // oldest first
            .ToArray();

        foreach (string file in files)
        {
            try
            {
                string json = File.ReadAllText(file);
                RollbackJournalSession? session = JsonSerializer.Deserialize<RollbackJournalSession>(json);
                if (session != null)
                {
                    JournalActionRecord? record = session.Records
                        .FirstOrDefault(r => r.TweakId == tweakId && r.TargetIdentifier == targetIdentifier && r.IsBaseline);
                    if (record != null)
                    {
                        return record;
                    }
                }
            }
            catch
            {
                // Continue searching
            }
        }

        return null;
    }

    public List<RollbackJournalSession> GetAllSessions()
    {
        lock (_lock)
        {
            List<RollbackJournalSession> sessions = new();
            string[] files = Directory.GetFiles(_journalDirectory, "*.json")
                .OrderByDescending(f => f)
                .ToArray();

            foreach (string file in files)
            {
                try
                {
                    string json = File.ReadAllText(file);
                    RollbackJournalSession? session = JsonSerializer.Deserialize<RollbackJournalSession>(json);
                    if (session != null)
                    {
                        sessions.Add(session);
                    }
                }
                catch
                {
                    // Ignore
                }
            }
            return sessions;
        }
    }

    private void FlushSessionUnsafe(RollbackJournalSession session)
    {
        string filePath = Path.Combine(_journalDirectory, $"session-{session.SessionId}.json");
        string tempPath = filePath + ".tmp";

        string json = JsonSerializer.Serialize(session, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, filePath, overwrite: true);
    }
}
