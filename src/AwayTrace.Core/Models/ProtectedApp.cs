namespace AwayTrace.Core.Models;

public sealed record ProtectedApp(
    long Id,
    string DisplayName,
    string ProcessName,
    string? ExecutablePath,
    bool IsEnabled,
    DateTimeOffset CreatedAt);
