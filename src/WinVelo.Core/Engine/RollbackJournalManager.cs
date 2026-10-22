using System.Text.Json;
using WinVelo.Core.Models;
using WinVelo.Core.Safety;

namespace WinVelo.Core.Engine;

public sealed class RollbackJournalManager
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> OperationGates = new(StringComparer.OrdinalIgnoreCase);
    public SemaphoreSlim OperationGate { get; }
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private readonly string _journalDirectory;
    private readonly object _lock = new();
    private readonly Mutex? _fileMutex;
    private readonly List<string> _damagedFiles = new();
    private RollbackJournalSession? _currentSession;

    public RollbackJournalManager(string journalDirectory)
    {
        _journalDirectory = journalDirectory;
        OperationGate = OperationGates.GetOrAdd(Path.TrimEndingDirectorySeparator(Path.GetFullPath(journalDirectory)), _ => new SemaphoreSlim(1, 1));
        Directory.CreateDirectory(_journalDirectory);
        _fileMutex = CreateFileMutex(_journalDirectory);
    }

    /// <summary>
    /// Journal files that could not be read, even from their backup copy. They are never deleted, so a
    /// person (or a later version) can still recover them; the app shows that they exist.
    /// </summary>
    public IReadOnlyList<string> DamagedFiles
    {
        get
        {
            lock (_damagedFiles)
            {
                return _damagedFiles.ToList();
            }
        }
    }

    public RollbackJournalSession StartSession(string? mode = null, string? appVersion = null)
    {
        lock (_lock)
            using (AcquireFileLock())
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
            using (AcquireFileLock())
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
            using (AcquireFileLock())
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
            using (AcquireFileLock())
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
            using (AcquireFileLock())
            {
                if (_currentSession != null && _currentSession.SessionId == sessionId)
                {
                    _currentSession.IsCompleted = true;
                    FlushSessionUnsafe(_currentSession);
                }

                string filePath = SessionPath(sessionId);
                if (File.Exists(filePath))
                {
                    try
                    {
                        RollbackJournalSession? session = TryReadSession(filePath);
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
                using (AcquireFileLock())
                {
                    return _currentSession != null && !_currentSession.IsCompleted;
                }
        }
    }

    public List<RollbackJournalSession> GetUnfinishedSessions()
    {
        lock (_lock)
            using (AcquireFileLock())
            {
                List<RollbackJournalSession> unfinished = new();
                string[] files = Directory.GetFiles(_journalDirectory, "*.json");
                foreach (string file in files)
                {
                    try
                    {
                        RollbackJournalSession? session = TryReadSession(file);
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
                RollbackJournalSession? session = TryReadSession(file);
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
            using (AcquireFileLock())
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
                        RollbackJournalSession? session = TryReadSession(file);
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
        static bool SameRecord(JournalActionRecord a, JournalActionRecord b) => a.TweakId == b.TweakId && a.ActionIndex == b.ActionIndex && a.TargetIdentifier == b.TargetIdentifier && a.AppliedAtUtc == b.AppliedAtUtc;
        var targetSet = new HashSet<JournalActionRecord>(records);
        if (targetSet.Count == 0) return;

        lock (_lock)
            using (AcquireFileLock())
            {
                if (_currentSession != null)
                {
                    bool modified = false;
                    foreach (var r in _currentSession.Records)
                    {
                        if (targetSet.Any(t => SameRecord(t, r)) && !r.IsReverted)
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
                        RollbackJournalSession? session = TryReadSession(file);
                        if (session != null)
                        {
                            bool modified = false;
                            foreach (var r in session.Records)
                            {
                                if (targetSet.Any(t => SameRecord(t, r)) && !r.IsReverted)
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

    /// <summary>
    /// Copies the original state stored in <paramref name="from"/> into <paramref name="target"/> (a record of
    /// another tweak on the same registry value or service) and saves it. Returns false when the target record
    /// cannot be found any more.
    /// </summary>
    public bool InheritBaseline(JournalActionRecord target, JournalActionRecord from)
    {
        static bool Same(JournalActionRecord a, JournalActionRecord b) =>
            a.TweakId == b.TweakId && a.ActionIndex == b.ActionIndex &&
            a.TargetIdentifier == b.TargetIdentifier && a.AppliedAtUtc == b.AppliedAtUtc;

        static void Copy(JournalActionRecord to, JournalActionRecord src)
        {
            to.ExistedBefore = src.ExistedBefore;
            to.PreviousValueKind = src.PreviousValueKind;
            to.PreviousData = src.PreviousData;
            to.KeyCreated = to.KeyCreated || src.KeyCreated;
        }

        lock (_lock)
            using (AcquireFileLock())
            {
                if (_currentSession != null)
                {
                    var inMemory = _currentSession.Records.FirstOrDefault(r => Same(r, target));
                    if (inMemory != null)
                    {
                        Copy(inMemory, from);
                        FlushSessionUnsafe(_currentSession);
                        return true;
                    }
                }

                foreach (string file in Directory.GetFiles(_journalDirectory, "*.json"))
                {
                    RollbackJournalSession? session = TryReadSession(file);
                    var match = session?.Records.FirstOrDefault(r => Same(r, target));
                    if (session != null && match != null)
                    {
                        Copy(match, from);
                        FlushSessionUnsafe(session);
                        return true;
                    }
                }
                return false;
            }
    }

    public List<RollbackJournalSession> GetAllSessions()
    {
        lock (_lock)
            using (AcquireFileLock())
            {
                List<RollbackJournalSession> sessions = new();
                string[] files = Directory.GetFiles(_journalDirectory, "*.json")
                    .OrderByDescending(f => f)
                    .ToArray();

                foreach (string file in files)
                {
                    try
                    {
                        RollbackJournalSession? session = TryReadSession(file);
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

    /// <summary>
    /// Writes a session atomically: the JSON goes to a temporary file that is flushed to disk, then replaces the
    /// session file in one step (the previous copy is kept as session-&lt;id&gt;.bak). A crash or power cut
    /// therefore leaves either the old or the new file, never a half-written one.
    /// </summary>
    private void FlushSessionUnsafe(RollbackJournalSession session)
    {
        Directory.CreateDirectory(_journalDirectory);
        string filePath = SessionPath(session.SessionId);
        string tempPath = Path.ChangeExtension(filePath, ".tmp");
        string backupPath = Path.ChangeExtension(filePath, ".bak");
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(session, WriteOptions);

        for (int attempt = 0; ; attempt++)
        {
            try
            {
                using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                {
                    fs.Write(bytes, 0, bytes.Length);
                    fs.Flush(flushToDisk: true);
                }

                if (File.Exists(filePath))
                {
                    File.Replace(tempPath, filePath, backupPath, ignoreMetadataErrors: true);
                }
                else
                {
                    File.Move(tempPath, filePath);
                }
                return;
            }
            catch (Exception ex) when (attempt < 4 && ex is IOException or UnauthorizedAccessException)
            {
                // Antivirus or a backup tool may hold the file for a moment.
                Thread.Sleep(25 * (attempt + 1));
            }
        }
    }

    private string SessionPath(string sessionId)
    {
        // Session ids are generated by StartSession ("yyyyMMdd_HHmmss_xxxxxxxx"); anything else must not become a path.
        foreach (char c in sessionId)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '_' && c != '-')
            {
                throw new ArgumentException($"Invalid journal session id '{sessionId}'.", nameof(sessionId));
            }
        }
        return Path.Combine(_journalDirectory, $"session-{sessionId}.json");
    }

    /// <summary>
    /// Reads a session file. When it is damaged (for example cut off by a crash in an older version), the backup
    /// copy written by the previous save is used instead. Returns null only when neither can be read.
    /// </summary>
    private RollbackJournalSession? TryReadSession(string file)
    {
        if (TryDeserialize(file, out var session)) return session;

        string backup = Path.ChangeExtension(file, ".bak");
        if (File.Exists(backup) && TryDeserialize(backup, out var fromBackup))
        {
            return fromBackup;
        }

        lock (_damagedFiles)
        {
            if (!_damagedFiles.Contains(file)) _damagedFiles.Add(file);
        }
        return null;
    }

    private static bool TryDeserialize(string file, out RollbackJournalSession? session)
    {
        session = null;
        try
        {
            string json = File.ReadAllText(file);
            if (string.IsNullOrWhiteSpace(json)) return false;
            session = JsonSerializer.Deserialize<RollbackJournalSession>(json);
            return session != null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    private static Mutex? CreateFileMutex(string directory)
    {
        // One lock per journal folder, shared by every WinVelo process of this user session (app and CLI).
        string full = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant();
        byte[] hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(full));
        string name = @"Local\WinVelo.Journal." + Convert.ToHexString(hash, 0, 12);
        try
        {
            return new Mutex(false, name);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or WaitHandleCannotBeOpenedException or PlatformNotSupportedException)
        {
            return null; // fall back to the in-process lock only
        }
    }

    private FileLock AcquireFileLock()
    {
        if (_fileMutex == null) return default;
        try
        {
            if (!_fileMutex.WaitOne(TimeSpan.FromSeconds(30)))
            {
                throw new IOException("Another WinVelo window or winvelo-cli is busy with the rollback journal. Try again in a moment.");
            }
        }
        catch (AbandonedMutexException)
        {
            // The other process ended while holding the lock. Every write is atomic, so the files are consistent.
        }
        return new FileLock(_fileMutex);
    }

    private readonly struct FileLock : IDisposable
    {
        private readonly Mutex? _mutex;
        public FileLock(Mutex mutex) => _mutex = mutex;
        public void Dispose() => _mutex?.ReleaseMutex();
    }
}
