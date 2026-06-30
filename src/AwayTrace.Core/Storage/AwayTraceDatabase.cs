using AwayTrace.Core.Models;
using AwayTrace.Core.Services;

namespace AwayTrace.Core.Storage;

public sealed class AwayTraceDatabase : ISettingsStore, ISessionRepository
{
    private readonly string _dbPath;
    private readonly object _gate = new();

    public AwayTraceDatabase(string? dbPath = null)
    {
        _dbPath = dbPath ?? AppPaths.DatabasePath;
    }

    public string DatabasePath => _dbPath;

    public void Initialize()
    {
        var directory = Path.GetDirectoryName(_dbPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        lock (_gate)
        {
            using var db = Open();
            db.ExecuteBatch("""
                PRAGMA journal_mode = WAL;
                PRAGMA foreign_keys = ON;

                CREATE TABLE IF NOT EXISTS settings (
                    key TEXT PRIMARY KEY,
                    value TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS monitored_folders (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    path TEXT NOT NULL UNIQUE,
                    created_at TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS sessions (
                    id TEXT PRIMARY KEY,
                    started_at TEXT NOT NULL,
                    ended_at TEXT NULL,
                    state TEXT NOT NULL,
                    low_confidence INTEGER NOT NULL DEFAULT 0,
                    folder_snapshot_json TEXT NOT NULL,
                    abnormal_reason TEXT NULL
                );

                CREATE TABLE IF NOT EXISTS file_events (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    session_id TEXT NOT NULL,
                    timestamp TEXT NOT NULL,
                    event_type TEXT NOT NULL,
                    path TEXT NOT NULL,
                    old_path TEXT NULL,
                    FOREIGN KEY(session_id) REFERENCES sessions(id) ON DELETE CASCADE
                );

                CREATE TABLE IF NOT EXISTS protected_apps (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    display_name TEXT NOT NULL,
                    process_name TEXT NOT NULL UNIQUE,
                    executable_path TEXT NULL,
                    is_enabled INTEGER NOT NULL DEFAULT 1,
                    created_at TEXT NOT NULL
                );

                CREATE INDEX IF NOT EXISTS idx_file_events_session_time
                    ON file_events(session_id, timestamp);

                CREATE INDEX IF NOT EXISTS idx_sessions_started_at
                    ON sessions(started_at);
                """);
        }
    }

    public Task<string?> GetSettingAsync(string key)
    {
        lock (_gate)
        {
            using var db = Open();
            var row = db.Query(
                "SELECT value FROM settings WHERE key = ? LIMIT 1;",
                new SqliteParameter(key)).FirstOrDefault();
            return Task.FromResult(row is null ? null : ReadString(row, "value"));
        }
    }

    public Task SetSettingAsync(string key, string value)
    {
        lock (_gate)
        {
            using var db = Open();
            db.Execute(
                """
                INSERT INTO settings(key, value)
                VALUES(?, ?)
                ON CONFLICT(key) DO UPDATE SET value = excluded.value;
                """,
                new SqliteParameter(key),
                new SqliteParameter(value));
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ProtectedApp>> GetProtectedAppsAsync()
    {
        lock (_gate)
        {
            using var db = Open();
            var apps = db.Query(
                    """
                    SELECT id, display_name, process_name, executable_path, is_enabled, created_at
                    FROM protected_apps
                    ORDER BY display_name;
                    """)
                .Select(ReadProtectedApp)
                .ToArray();
            return Task.FromResult<IReadOnlyList<ProtectedApp>>(apps);
        }
    }

    public Task AddProtectedAppAsync(string displayName, string processName, string? executablePath)
    {
        var normalizedProcessName = NormalizeProcessName(processName);
        lock (_gate)
        {
            using var db = Open();
            db.Execute(
                """
                INSERT INTO protected_apps(display_name, process_name, executable_path, is_enabled, created_at)
                VALUES(?, ?, ?, 1, ?)
                ON CONFLICT(process_name) DO UPDATE SET
                    display_name = excluded.display_name,
                    executable_path = COALESCE(excluded.executable_path, protected_apps.executable_path),
                    is_enabled = 1;
                """,
                new SqliteParameter(displayName),
                new SqliteParameter(normalizedProcessName),
                new SqliteParameter(executablePath),
                new SqliteParameter(DateTimeOffset.Now));
        }

        return Task.CompletedTask;
    }

    public Task UpdateProtectedAppEnabledAsync(long id, bool isEnabled)
    {
        lock (_gate)
        {
            using var db = Open();
            db.Execute(
                "UPDATE protected_apps SET is_enabled = ? WHERE id = ?;",
                new SqliteParameter(isEnabled),
                new SqliteParameter(id));
        }

        return Task.CompletedTask;
    }

    public Task RemoveProtectedAppAsync(long id)
    {
        lock (_gate)
        {
            using var db = Open();
            db.Execute("DELETE FROM protected_apps WHERE id = ?;", new SqliteParameter(id));
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<MonitoredFolder>> GetFoldersAsync()
    {
        lock (_gate)
        {
            using var db = Open();
            var folders = db.Query(
                    "SELECT id, path, created_at FROM monitored_folders ORDER BY path;")
                .Select(ReadFolder)
                .ToArray();
            return Task.FromResult<IReadOnlyList<MonitoredFolder>>(folders);
        }
    }

    public Task AddFolderAsync(string path)
    {
        var normalizedPath = Path.GetFullPath(path);
        lock (_gate)
        {
            using var db = Open();
            db.Execute(
                """
                INSERT OR IGNORE INTO monitored_folders(path, created_at)
                VALUES(?, ?);
                """,
                new SqliteParameter(normalizedPath),
                new SqliteParameter(DateTimeOffset.Now));
        }

        return Task.CompletedTask;
    }

    public Task RemoveFolderAsync(long id)
    {
        lock (_gate)
        {
            using var db = Open();
            db.Execute(
                "DELETE FROM monitored_folders WHERE id = ?;",
                new SqliteParameter(id));
        }

        return Task.CompletedTask;
    }

    public Task CreateSessionAsync(WatchSession session)
    {
        lock (_gate)
        {
            using var db = Open();
            db.Execute(
                """
                INSERT INTO sessions(id, started_at, ended_at, state, low_confidence, folder_snapshot_json, abnormal_reason)
                VALUES(?, ?, ?, ?, ?, ?, ?);
                """,
                new SqliteParameter(session.Id),
                new SqliteParameter(session.StartedAt),
                new SqliteParameter(session.EndedAt),
                new SqliteParameter(session.State),
                new SqliteParameter(session.LowConfidence),
                new SqliteParameter(session.FolderSnapshotJson),
                new SqliteParameter(session.AbnormalReason));
        }

        return Task.CompletedTask;
    }

    public Task EndSessionAsync(Guid sessionId, DateTimeOffset endedAt)
    {
        lock (_gate)
        {
            using var db = Open();
            db.Execute(
                """
                UPDATE sessions
                SET ended_at = ?, state = ?, low_confidence = 0
                WHERE id = ?;
                """,
                new SqliteParameter(endedAt),
                new SqliteParameter(ProtectionSessionState.Completed),
                new SqliteParameter(sessionId));
        }

        return Task.CompletedTask;
    }

    public Task DeleteSessionAsync(Guid sessionId)
    {
        lock (_gate)
        {
            using var db = Open();
            db.Execute("DELETE FROM file_events WHERE session_id = ?;", new SqliteParameter(sessionId));
            db.Execute("DELETE FROM sessions WHERE id = ?;", new SqliteParameter(sessionId));
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<WatchSession>> GetActiveSessionsAsync()
    {
        lock (_gate)
        {
            using var db = Open();
            var sessions = db.Query(
                    "SELECT * FROM sessions WHERE state = ? ORDER BY started_at;",
                    new SqliteParameter(ProtectionSessionState.Active))
                .Select(ReadSession)
                .ToArray();
            return Task.FromResult<IReadOnlyList<WatchSession>>(sessions);
        }
    }

    public Task MarkSessionAbnormalAsync(Guid sessionId, DateTimeOffset endedAt, string reason)
    {
        lock (_gate)
        {
            using var db = Open();
            db.Execute(
                """
                UPDATE sessions
                SET ended_at = COALESCE(ended_at, ?),
                    state = ?,
                    low_confidence = 1,
                    abnormal_reason = ?
                WHERE id = ?;
                """,
                new SqliteParameter(endedAt),
                new SqliteParameter(ProtectionSessionState.AbnormalTermination),
                new SqliteParameter(reason),
                new SqliteParameter(sessionId));
        }

        return Task.CompletedTask;
    }

    public Task AddFileEventAsync(FileEventRecord fileEvent)
    {
        lock (_gate)
        {
            using var db = Open();
            db.Execute(
                """
                INSERT INTO file_events(session_id, timestamp, event_type, path, old_path)
                VALUES(?, ?, ?, ?, ?);
                """,
                new SqliteParameter(fileEvent.SessionId),
                new SqliteParameter(fileEvent.Timestamp),
                new SqliteParameter(fileEvent.EventType),
                new SqliteParameter(fileEvent.Path),
                new SqliteParameter(fileEvent.OldPath));
        }

        return Task.CompletedTask;
    }

    public Task<WatchSession?> GetLatestSessionAsync()
    {
        lock (_gate)
        {
            using var db = Open();
            var row = db.Query(
                "SELECT * FROM sessions ORDER BY started_at DESC LIMIT 1;").FirstOrDefault();
            return Task.FromResult(row is null ? null : ReadSession(row));
        }
    }

    public Task<WatchSession?> GetSessionAsync(Guid sessionId)
    {
        lock (_gate)
        {
            using var db = Open();
            var row = db.Query(
                    "SELECT * FROM sessions WHERE id = ? LIMIT 1;",
                    new SqliteParameter(sessionId))
                .FirstOrDefault();
            return Task.FromResult(row is null ? null : ReadSession(row));
        }
    }

    public Task<IReadOnlyList<FileEventRecord>> GetSessionEventsAsync(Guid sessionId)
    {
        lock (_gate)
        {
            using var db = Open();
            var events = db.Query(
                    """
                    SELECT id, session_id, timestamp, event_type, path, old_path
                    FROM file_events
                    WHERE session_id = ?
                    ORDER BY timestamp, id;
                    """,
                    new SqliteParameter(sessionId))
                .Select(ReadFileEvent)
                .ToArray();
            return Task.FromResult<IReadOnlyList<FileEventRecord>>(events);
        }
    }

    private SqliteConnectionLite Open() => new(_dbPath);

    private static MonitoredFolder ReadFolder(IReadOnlyDictionary<string, object?> row)
    {
        return new MonitoredFolder(
            ReadLong(row, "id"),
            ReadString(row, "path"),
            ReadDateTimeOffset(row, "created_at"));
    }

    private static WatchSession ReadSession(IReadOnlyDictionary<string, object?> row)
    {
        return new WatchSession(
            Guid.Parse(ReadString(row, "id")),
            ReadDateTimeOffset(row, "started_at"),
            ReadNullableDateTimeOffset(row, "ended_at"),
            Enum.Parse<ProtectionSessionState>(ReadString(row, "state")),
            ReadLong(row, "low_confidence") == 1,
            ReadString(row, "folder_snapshot_json"),
            ReadNullableString(row, "abnormal_reason"));
    }

    private static FileEventRecord ReadFileEvent(IReadOnlyDictionary<string, object?> row)
    {
        return new FileEventRecord(
            ReadLong(row, "id"),
            Guid.Parse(ReadString(row, "session_id")),
            ReadDateTimeOffset(row, "timestamp"),
            Enum.Parse<FileEventType>(ReadString(row, "event_type")),
            ReadString(row, "path"),
            ReadNullableString(row, "old_path"));
    }

    private static ProtectedApp ReadProtectedApp(IReadOnlyDictionary<string, object?> row)
    {
        return new ProtectedApp(
            ReadLong(row, "id"),
            ReadString(row, "display_name"),
            ReadString(row, "process_name"),
            ReadNullableString(row, "executable_path"),
            ReadLong(row, "is_enabled") == 1,
            ReadDateTimeOffset(row, "created_at"));
    }

    private static string NormalizeProcessName(string processName)
    {
        var trimmed = processName.Trim();
        return trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? trimmed[..^4]
            : trimmed;
    }

    private static string ReadString(IReadOnlyDictionary<string, object?> row, string key) =>
        Convert.ToString(row[key]) ?? string.Empty;

    private static string? ReadNullableString(IReadOnlyDictionary<string, object?> row, string key) =>
        row.TryGetValue(key, out var value) ? Convert.ToString(value) : null;

    private static long ReadLong(IReadOnlyDictionary<string, object?> row, string key) =>
        Convert.ToInt64(row[key]);

    private static DateTimeOffset ReadDateTimeOffset(IReadOnlyDictionary<string, object?> row, string key) =>
        DateTimeOffset.Parse(ReadString(row, key));

    private static DateTimeOffset? ReadNullableDateTimeOffset(IReadOnlyDictionary<string, object?> row, string key)
    {
        var value = ReadNullableString(row, key);
        return DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
    }
}
