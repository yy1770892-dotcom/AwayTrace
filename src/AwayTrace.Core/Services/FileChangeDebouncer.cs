using AwayTrace.Core.Models;

namespace AwayTrace.Core.Services;

public sealed class FileChangeDebouncer
{
    private readonly TimeSpan _window;
    private readonly Dictionary<string, DateTimeOffset> _lastSeen = new(StringComparer.OrdinalIgnoreCase);

    public FileChangeDebouncer(TimeSpan? window = null)
    {
        _window = window ?? TimeSpan.FromSeconds(2);
    }

    public bool ShouldRecord(FileEventType eventType, string path, string? oldPath, DateTimeOffset timestamp)
    {
        if (eventType is FileEventType.System or FileEventType.WatcherError)
        {
            return true;
        }

        var key = BuildKey(eventType, path, oldPath);
        if (_lastSeen.TryGetValue(key, out var previous) && timestamp - previous < _window)
        {
            return false;
        }

        _lastSeen[key] = timestamp;
        RemoveStaleEntries(timestamp);
        return true;
    }

    private static string BuildKey(FileEventType eventType, string path, string? oldPath)
    {
        var normalizedPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedOldPath = string.IsNullOrWhiteSpace(oldPath)
            ? string.Empty
            : Path.GetFullPath(oldPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return $"{eventType}|{normalizedOldPath}|{normalizedPath}";
    }

    private void RemoveStaleEntries(DateTimeOffset now)
    {
        if (_lastSeen.Count < 512)
        {
            return;
        }

        foreach (var item in _lastSeen.Where(item => now - item.Value > _window * 3).ToArray())
        {
            _lastSeen.Remove(item.Key);
        }
    }
}
