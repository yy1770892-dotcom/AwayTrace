namespace AwayTrace.Core.Models;

public sealed record PcUsageEvent(
    long Id,
    DateTimeOffset Timestamp,
    PcUsageEventType EventType,
    string Description,
    string Source);
