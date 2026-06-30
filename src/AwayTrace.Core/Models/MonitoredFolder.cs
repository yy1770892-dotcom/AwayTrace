namespace AwayTrace.Core.Models;

public sealed record MonitoredFolder(
    long Id,
    string Path,
    DateTimeOffset CreatedAt);
