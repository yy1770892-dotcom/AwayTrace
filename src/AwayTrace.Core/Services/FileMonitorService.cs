using AwayTrace.Core.Models;
using AwayTrace.Core.Storage;

namespace AwayTrace.Core.Services;

public sealed class FileMonitorService : IDisposable
{
    private readonly AwayTraceDatabase _database;
    private readonly FileChangeDebouncer _debouncer;
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly object _gate = new();
    private Guid? _sessionId;
    private bool _disposed;

    public FileMonitorService(AwayTraceDatabase database, FileChangeDebouncer? debouncer = null)
    {
        _database = database;
        _debouncer = debouncer ?? new FileChangeDebouncer();
    }

    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _sessionId is not null;
            }
        }
    }

    public Task StartAsync(Guid sessionId, IEnumerable<string> folders)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Stop();

        lock (_gate)
        {
            _sessionId = sessionId;
            foreach (var folder in folders)
            {
                if (!Directory.Exists(folder))
                {
                    _ = RecordEventAsync(FileEventType.WatcherError, folder, "감시 폴더가 존재하지 않습니다.");
                    continue;
                }

                var watcher = new FileSystemWatcher(folder)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName
                        | NotifyFilters.DirectoryName
                        | NotifyFilters.LastWrite
                        | NotifyFilters.CreationTime
                        | NotifyFilters.Size,
                    EnableRaisingEvents = true
                };

                watcher.Created += OnCreated;
                watcher.Changed += OnChanged;
                watcher.Deleted += OnDeleted;
                watcher.Renamed += OnRenamed;
                watcher.Error += OnError;
                _watchers.Add(watcher);
            }
        }

        return Task.CompletedTask;
    }

    public void Stop()
    {
        lock (_gate)
        {
            foreach (var watcher in _watchers)
            {
                watcher.EnableRaisingEvents = false;
                watcher.Created -= OnCreated;
                watcher.Changed -= OnChanged;
                watcher.Deleted -= OnDeleted;
                watcher.Renamed -= OnRenamed;
                watcher.Error -= OnError;
                watcher.Dispose();
            }

            _watchers.Clear();
            _sessionId = null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Stop();
        _disposed = true;
    }

    private void OnCreated(object sender, FileSystemEventArgs e) =>
        _ = RecordEventAsync(FileEventType.Created, e.FullPath, null);

    private void OnChanged(object sender, FileSystemEventArgs e) =>
        _ = RecordEventAsync(FileEventType.Changed, e.FullPath, null);

    private void OnDeleted(object sender, FileSystemEventArgs e) =>
        _ = RecordEventAsync(FileEventType.Deleted, e.FullPath, null);

    private void OnRenamed(object sender, RenamedEventArgs e) =>
        _ = RecordEventAsync(FileEventType.Renamed, e.FullPath, e.OldFullPath);

    private void OnError(object sender, ErrorEventArgs e)
    {
        var path = sender is FileSystemWatcher watcher ? watcher.Path : "FileSystemWatcher";
        _ = RecordEventAsync(FileEventType.WatcherError, path, e.GetException().Message);
    }

    private async Task RecordEventAsync(FileEventType eventType, string path, string? oldPath)
    {
        Guid? sessionId;
        lock (_gate)
        {
            sessionId = _sessionId;
        }

        if (sessionId is null)
        {
            return;
        }

        var now = DateTimeOffset.Now;
        if (!_debouncer.ShouldRecord(eventType, path, oldPath, now))
        {
            return;
        }

        await _database.AddFileEventAsync(new FileEventRecord(
            0,
            sessionId.Value,
            now,
            eventType,
            path,
            oldPath));
    }
}
