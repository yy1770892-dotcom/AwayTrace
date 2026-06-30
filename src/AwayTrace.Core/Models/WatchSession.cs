namespace AwayTrace.Core.Models;

public sealed record WatchSession(
    Guid Id,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    ProtectionSessionState State,
    bool LowConfidence,
    string FolderSnapshotJson,
    string? AbnormalReason);
