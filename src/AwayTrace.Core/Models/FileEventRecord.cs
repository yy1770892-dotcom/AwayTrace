namespace AwayTrace.Core.Models;

public sealed record FileEventRecord(
    long Id,
    Guid SessionId,
    DateTimeOffset Timestamp,
    FileEventType EventType,
    string Path,
    string? OldPath);
