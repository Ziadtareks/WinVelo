using System.Text.Json;
using Tweakora.Core.Models;
using Tweakora.Core.Safety;

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

    public RollbackJournalSession StartSession(string? mode = null, string? appVersion = null)
    {
        lock (_lock)
        {
            mode ??= TestModeManager.IsTestMode ? "test" : "real";
            appVersion ??= AppInfo.Version;

            _currentSession = new RollbackJournalSession
            {
                SessionId = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_") + Guid.NewGuid().ToString("N")[..8],
                StartedAtUtc = DateTime.UtcNow,
                IsCompleted = false,
                Mode = mode,
                AppVersion = appVersion
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

    public void CompleteSession(RollbackJournalSession session)
    {
        lock (_lock)
        {
            session.IsCompleted = true;
            FlushSessionUnsafe(session);
            if (_currentSession != null && _currentSession.SessionId == session.SessionId)
            {
                _currentSession.IsCompleted = true;
            }
        }
    }

    public void CompleteSession(string sessionId)
    {
        lock (_lock)
        {
            if (_currentSession != null && _currentSession.SessionId == sessionId)
            {
                _currentSession.IsCompleted = true;
                FlushSessionUnsafe(_currentSession);
            }

            string filePath = Path.Combine(_journalDirectory, $"session-{sessionId}.json");
            if (File.Exists(filePath))
            {
                try
                {
                    string json = File.ReadAllText(filePath);
                    RollbackJournalSession? session = JsonSerializer.Deserialize<RollbackJournalSession>(json);
                    if (session != null)
                    {
                        session.IsCompleted = true;
                        FlushSessionUnsafe(session);
                    }
                }
                catch { }
            }
        }
    }

    public bool HasActiveSession
    {
        get
        {
            lock (_lock)
            {
                return _currentSession != null && !_currentSession.IsCompleted;
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
                        // In real mode, exclude sessions without a marker (legacy sessions)
                        if (!TestModeManager.IsTestMode && session.IsLegacy)
                        {
                            continue;
                        }

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
                .FirstOrDefault(r => r.TweakId == tweakId && r.TargetIdentifier == targetIdentifier && r.IsBaseline && !r.IsReverted);
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
                        .FirstOrDefault(r => r.TweakId == tweakId && r.TargetIdentifier == targetIdentifier && r.IsBaseline && !r.IsReverted);
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

    public void MarkTweakRecordsReverted(string tweakId)
    {
        lock (_lock)
        {
            if (_currentSession != null)
            {
                bool modified = false;
                foreach (var r in _currentSession.Records.Where(r => r.TweakId == tweakId && !r.IsReverted))
                {
                    r.IsReverted = true;
                    r.RevertedAtUtc = DateTime.UtcNow;
                    modified = true;
                }
                if (modified) FlushSessionUnsafe(_currentSession);
            }

            string[] files = Directory.GetFiles(_journalDirectory, "*.json");
            foreach (string file in files)
            {
                try
                {
                    string json = File.ReadAllText(file);
                    RollbackJournalSession? session = JsonSerializer.Deserialize<RollbackJournalSession>(json);
                    if (session != null)
                    {
                        bool modified = false;
                        foreach (var r in session.Records.Where(r => r.TweakId == tweakId && !r.IsReverted))
                        {
                            r.IsReverted = true;
                            r.RevertedAtUtc = DateTime.UtcNow;
                            modified = true;
                        }
                        if (modified)
                        {
                            FlushSessionUnsafe(session);
                        }
                    }
                }
                catch { }
            }
        }
    }

    public void MarkRecordsReverted(IEnumerable<JournalActionRecord> records)
    {
        var targetSet = new HashSet<JournalActionRecord>(records);
        if (targetSet.Count == 0) return;

        lock (_lock)
        {
            if (_currentSession != null)
            {
                bool modified = false;
                foreach (var r in _currentSession.Records)
                {
                    if (targetSet.Contains(r) && !r.IsReverted)
                    {
                        r.IsReverted = true;
                        r.RevertedAtUtc = DateTime.UtcNow;
                        modified = true;
                    }
                }
                if (modified) FlushSessionUnsafe(_currentSession);
            }

            string[] files = Directory.GetFiles(_journalDirectory, "*.json");
            foreach (string file in files)
            {
                try
                {
                    string json = File.ReadAllText(file);
                    RollbackJournalSession? session = JsonSerializer.Deserialize<RollbackJournalSession>(json);
                    if (session != null)
                    {
                        bool modified = false;
                        foreach (var r in session.Records)
                        {
                            if (targetSet.Any(t => t.TweakId == r.TweakId && t.ActionIndex == r.ActionIndex && t.TargetIdentifier == r.TargetIdentifier) && !r.IsReverted)
                            {
                                r.IsReverted = true;
                                r.RevertedAtUtc = DateTime.UtcNow;
                                modified = true;
                            }
                        }
                        if (modified)
                        {
                            FlushSessionUnsafe(session);
                        }
                    }
                }
                catch { }
            }
        }
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
        Directory.CreateDirectory(_journalDirectory);
        string filePath = Path.Combine(_journalDirectory, $"session-{session.SessionId}.json");
        string json = JsonSerializer.Serialize(session, new JsonSerializerOptions { WriteIndented = true });

        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
                using var writer = new StreamWriter(fs);
                writer.Write(json);
                writer.Flush();
                return;
            }
            catch (Exception) when (attempt < 4)
            {
                Thread.Sleep(15);
            }
        }
    }
}
